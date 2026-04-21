using System.Text;
using System.Text.Json;
using BizPilot.API.Common;
using BizPilot.API.Data;
using BizPilot.API.DTOs.Expenses;
using BizPilot.API.DTOs.Products;
using BizPilot.API.DTOs.Sales;
using BizPilot.API.DTOs.Inventory;
using BizPilot.API.Models;
using BizPilot.API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BizPilot.API.Jobs;

/// <summary>
/// Hangfire-backed worker that executes CSV imports in the background. Imports of any size use this path
/// (frontend polls <c>GET /api/import/jobs/{id}</c> for progress), which removes the HTTP timeout ceiling
/// that previously capped imports at roughly 10,000 rows.
///
/// The raw CSV text is persisted on the ImportJob row so the worker is self-contained — it doesn't need
/// the original HTTP context. Once the job finishes, <c>RawCsvText</c> is cleared to keep the table small.
///
/// Progress is flushed every <see cref="ProgressBatchSize"/> rows so polling clients see live updates.
/// Errors beyond <see cref="MaxErrorsStored"/> are summarized rather than stored individually, since a
/// catastrophically broken CSV could otherwise bloat the database.
/// </summary>
public class ImportJobService
{
    private readonly AppDbContext _db;
    private readonly IProductService _products;
    private readonly ISalesService _sales;
    private readonly IExpenseService _expenses;
    private readonly IInventoryService _inventory;
    private readonly IWhatsAppService _whatsApp;
    private readonly ILogger<ImportJobService> _logger;

    // Flush progress + SaveChanges every N rows. 200 is a compromise between progress granularity and DB load.
    private const int ProgressBatchSize = 200;

    // Cap the number of detailed errors we retain so a completely broken CSV can't blow up the errors JSON column.
    private const int MaxErrorsStored = 500;

    /// <summary>
    /// Filters exception messages for user-facing display. Business-logic exceptions (stock, validation,
    /// format) carry helpful messages and are surfaced. Infrastructure exceptions (driver, network, ORM)
    /// get a generic message so internal details don't leak to end users. Full ex goes to logs either way.
    /// </summary>
    private static string FriendlyRowError(Exception ex) =>
        ex is InvalidOperationException or KeyNotFoundException or ArgumentException or FormatException
            ? ex.Message
            : "unexpected error processing this row";

    public ImportJobService(
        AppDbContext db,
        IProductService products,
        ISalesService sales,
        IExpenseService expenses,
        IInventoryService inventory,
        IWhatsAppService whatsApp,
        ILogger<ImportJobService> logger)
    {
        _db = db;
        _products = products;
        _sales = sales;
        _expenses = expenses;
        _inventory = inventory;
        _whatsApp = whatsApp;
        _logger = logger;
    }

    /// <summary>
    /// Hangfire entry point. Loads the job row, dispatches to the right handler based on Type, and
    /// marks completion/failure. Any unhandled exception becomes a Failed job with FailureReason populated.
    /// </summary>
    public async Task RunAsync(Guid jobId)
    {
        var job = await _db.ImportJobs.FirstOrDefaultAsync(j => j.Id == jobId);
        if (job == null)
        {
            _logger.LogWarning("ImportJob {JobId} not found — skipping", jobId);
            return;
        }

        if (job.Status != ImportJobStatus.Queued)
        {
            // Re-enqueued by Hangfire retry; skip to avoid double-processing.
            _logger.LogInformation("ImportJob {JobId} status is {Status}, skipping", jobId, job.Status);
            return;
        }

        job.Status = ImportJobStatus.Running;
        job.StartedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        try
        {
            var csvText = job.RawCsvText ?? "";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csvText));
            var rows = CsvParser.Parse(stream);
            job.TotalRows = rows.Count;

            var errors = new List<string>();
            var user = await _db.Users.FindAsync(job.UserId);
            var business = await _db.Businesses.FindAsync(job.BusinessId);
            var cs = BillingConfig.Symbol(business?.Currency);

            switch (job.Type)
            {
                case ImportJobType.Inventory:
                    await ProcessInventoryRowsAsync(job, rows, user, errors, cs);
                    break;
                case ImportJobType.Sales:
                    await ProcessSalesRowsAsync(job, rows, user, errors, cs);
                    break;
                case ImportJobType.Expenses:
                    await ProcessExpensesRowsAsync(job, rows, user, errors, cs);
                    break;
                case ImportJobType.Contacts:
                    await ProcessContactsRowsAsync(job, rows, errors);
                    break;
                case ImportJobType.ContactsWithLedger:
                    await ProcessContactsWithLedgerRowsAsync(job, rows, user, errors, cs);
                    break;
            }

            job.ErrorsJson = JsonSerializer.Serialize(errors.Take(MaxErrorsStored).ToList());
            job.ErrorCount = errors.Count;
            job.Status = ImportJobStatus.Completed;
            job.CompletedAtUtc = DateTime.UtcNow;
            // Free up the large text column once processing is done.
            job.RawCsvText = null;
            await _db.SaveChangesAsync();

            await SendCompletionMessageAsync(job);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Import job {JobId} failed", jobId);
            job.Status = ImportJobStatus.Failed;
            // Safe user-facing failure reason. Full exception is already logged above for diagnosis.
            var safeMessage = ex is InvalidOperationException or KeyNotFoundException or ArgumentException
                ? ex.Message
                : "Unexpected error. The import was rolled back — please contact support.";
            job.FailureReason = safeMessage.Length > 500 ? safeMessage[..500] : safeMessage;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.RawCsvText = null;
            try { await _db.SaveChangesAsync(); }
            catch (Exception saveEx) { _logger.LogError(saveEx, "Could not persist failure for job {JobId}", jobId); }
        }
    }

    private async Task ProcessInventoryRowsAsync(ImportJob job, List<Dictionary<string, string>> rows, User? user, List<string> errors, string cs)
    {
        var productCache = await _db.Products
            .Where(p => p.BusinessId == job.BusinessId && p.IsActive)
            .ToDictionaryAsync(p => p.Name.ToLower(), p => p);

        _db.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var rowNum = i + 2;
                try
                {
                    var name = row.GetValueOrDefault("productname");
                    if (!ValidateName(name, rowNum, "ProductName", errors)) continue;

                    var qty = CsvParser.ParseDecimal(row.GetValueOrDefault("quantity"));
                    if (!ValidateQuantity(qty, rowNum, name!, errors)) continue;

                    var unit = row.GetValueOrDefault("unit") ?? UnitInferrer.Infer(name!);
                    var costPrice = CsvParser.ParseDecimal(row.GetValueOrDefault("costprice"));
                    var sellingPrice = CsvParser.ParseDecimal(row.GetValueOrDefault("sellingprice"));
                    if (costPrice.HasValue && costPrice.Value > 100_000_000) { errors.Add($"Row {rowNum}: Cost price {cs}{costPrice.Value:N0} for '{name}' seems unusually large"); continue; }
                    if (sellingPrice.HasValue && sellingPrice.Value > 100_000_000) { errors.Add($"Row {rowNum}: Selling price {cs}{sellingPrice.Value:N0} for '{name}' seems unusually large"); continue; }

                    var invDateStr = row.GetValueOrDefault("date") ?? row.GetValueOrDefault("stockdate");
                    if (string.IsNullOrWhiteSpace(invDateStr)) { errors.Add($"Row {rowNum}: Date is required for inventory import. Add a 'Date' column (format: YYYY-MM-DD)."); continue; }
                    if (!DateTime.TryParse(invDateStr, out var invParsedDate)) { errors.Add($"Row {rowNum}: Invalid date '{invDateStr}'. Use format: YYYY-MM-DD."); continue; }
                    var invDate = DateTime.SpecifyKind(invParsedDate, DateTimeKind.Utc);

                    var csvCategory = row.GetValueOrDefault("category");
                    var csvSubcategory = row.GetValueOrDefault("subcategory");
                    var csvThreshold = CsvParser.ParseDecimal(row.GetValueOrDefault("threshold"));

                    Product product;
                    if (!productCache.TryGetValue(name!.ToLower(), out var existing))
                    {
                        var (inferredCat, inferredSubcat) = CategoryInferrer.Infer(name!);
                        var inferredUnit = string.IsNullOrWhiteSpace(unit) || unit == "unit" || unit == "bag"
                            ? UnitInferrer.Infer(name!) : unit;

                        product = new Product
                        {
                            BusinessId = job.BusinessId,
                            Name = name!,
                            Unit = inferredUnit,
                            CostPrice = costPrice,
                            SellingPrice = sellingPrice,
                            CurrentStock = qty!.Value,
                            LowStockThreshold = csvThreshold ?? 5,
                            Category = csvCategory ?? inferredCat,
                            Subcategory = csvSubcategory ?? inferredSubcat,
                            Source = EntrySource.Import,
                            ImportBatchId = job.Id,
                            RecordedByUserId = user?.Id,
                            RecordedByName = user?.FullName,
                            CreatedAtUtc = invDate
                        };
                        _db.Products.Add(product);
                        productCache[name!.ToLower()] = product;

                        if (qty!.Value > 0)
                        {
                            _db.InventoryTransactions.Add(new InventoryTransaction
                            {
                                BusinessId = job.BusinessId,
                                ProductId = product.Id,
                                Type = InventoryTransactionType.StockIn,
                                Quantity = qty.Value,
                                UnitCost = costPrice,
                                Notes = "Initial stock",
                                RecordedByUserId = user?.Id,
                                RecordedByName = user?.FullName,
                                CreatedAtUtc = invDate
                            });
                        }
                    }
                    else
                    {
                        product = existing;
                        product.CurrentStock += qty!.Value;
                        if (costPrice.HasValue) product.CostPrice = costPrice;
                        if (sellingPrice.HasValue && !product.SellingPrice.HasValue) product.SellingPrice = sellingPrice;
                        _db.Entry(product).State = EntityState.Modified;

                        _db.InventoryTransactions.Add(new InventoryTransaction
                        {
                            BusinessId = job.BusinessId,
                            ProductId = product.Id,
                            Type = InventoryTransactionType.StockIn,
                            Quantity = qty!.Value,
                            UnitCost = costPrice ?? product.CostPrice,
                            Notes = "Import stock-in",
                            RecordedByUserId = user?.Id,
                            RecordedByName = user?.FullName,
                            CreatedAtUtc = invDate
                        });
                    }

                    var expenseCost = costPrice ?? product.CostPrice;
                    if (expenseCost.HasValue && expenseCost.Value > 0)
                    {
                        _db.Expenses.Add(new Expense
                        {
                            BusinessId = job.BusinessId,
                            Category = "Inventory",
                            ExpenseType = "cogs",
                            Amount = qty.Value * expenseCost.Value,
                            Notes = $"Import: {qty.Value:0.##} {unit} of {name} @ {cs}{expenseCost.Value:N0}",
                            Source = EntrySource.Import,
                            RecordedByUserId = user?.Id,
                            RecordedByName = user?.FullName,
                            CreatedAtUtc = invDate,
                            ImportBatchId = job.Id
                        });
                    }

                    job.SuccessCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Import row {Row} failed for job {JobId}", rowNum, job.Id);
                    errors.Add($"Row {rowNum}: {FriendlyRowError(ex)}");
                }

                job.ProcessedRows = i + 1;
                if ((i + 1) % ProgressBatchSize == 0)
                {
                    _db.ChangeTracker.DetectChanges();
                    await _db.SaveChangesAsync();
                }
            }

            _db.ChangeTracker.DetectChanges();
            await _db.SaveChangesAsync();
        }
        finally
        {
            _db.ChangeTracker.AutoDetectChangesEnabled = true;
        }
    }

    private async Task ProcessSalesRowsAsync(ImportJob job, List<Dictionary<string, string>> rows, User? user, List<string> errors, string cs)
    {
        var productCache = await _db.Products
            .Where(p => p.BusinessId == job.BusinessId && p.IsActive)
            .ToDictionaryAsync(p => p.Name.ToLower(), p => p);
        var contactCache = await _db.Contacts
            .Where(c => c.BusinessId == job.BusinessId)
            .ToDictionaryAsync(c => c.Name.ToLower(), c => c);

        _db.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var rowNum = i + 2;
                try
                {
                    var name = row.GetValueOrDefault("productname");
                    if (!ValidateName(name, rowNum, "ProductName", errors)) continue;

                    var qty = CsvParser.ParseDecimal(row.GetValueOrDefault("quantity"));
                    if (!ValidateQuantity(qty, rowNum, name!, errors)) continue;

                    productCache.TryGetValue(name!.ToLower(), out var product);
                    if (product == null) { errors.Add($"Row {rowNum}: Product '{name}' not found in inventory"); continue; }

                    var unitPrice = CsvParser.ParseDecimal(row.GetValueOrDefault("unitprice"));
                    if (!unitPrice.HasValue || unitPrice.Value <= 0) { errors.Add($"Row {rowNum}: UnitPrice is required for '{name}'. Should be a positive number."); continue; }
                    if (unitPrice.Value > 100_000_000) { errors.Add($"Row {rowNum}: UnitPrice {cs}{unitPrice.Value:N0} for '{name}' seems unusually large"); continue; }

                    if (product.CurrentStock < qty!.Value)
                    {
                        errors.Add($"Row {rowNum}: Insufficient stock for '{name}'. Available: {product.CurrentStock:0.##} {product.Unit}.");
                        continue;
                    }

                    var customerName = row.GetValueOrDefault("customername");
                    if (customerName != null && customerName.Length > 200) { errors.Add($"Row {rowNum}: Customer name too long (max 200 characters)"); continue; }
                    Guid? contactId = null;
                    if (!string.IsNullOrEmpty(customerName))
                    {
                        if (!contactCache.TryGetValue(customerName.ToLower(), out var contact))
                        {
                            contact = new Contact
                            {
                                BusinessId = job.BusinessId,
                                Name = customerName,
                                Type = ContactType.Customer,
                                Source = EntrySource.Import,
                                ImportBatchId = job.Id
                            };
                            _db.Contacts.Add(contact);
                            contactCache[customerName.ToLower()] = contact;
                        }
                        contactId = contact.Id;
                    }

                    var dateStr = row.GetValueOrDefault("date") ?? row.GetValueOrDefault("saledate");
                    if (string.IsNullOrWhiteSpace(dateStr)) { errors.Add($"Row {rowNum}: Date is required for sales import. Add a 'Date' column (format: YYYY-MM-DD)."); continue; }
                    if (!DateTime.TryParse(dateStr, out var parsedDate)) { errors.Add($"Row {rowNum}: Invalid date '{dateStr}'. Use format: YYYY-MM-DD."); continue; }
                    var saleDate = DateTime.SpecifyKind(parsedDate, DateTimeKind.Utc);

                    var statusStr = row.GetValueOrDefault("paymentstatus") ?? "Paid";
                    if (!string.IsNullOrEmpty(statusStr) && statusStr != "Paid" && !ValidPaymentStatuses.Contains(statusStr))
                    {
                        errors.Add($"Row {rowNum}: Unknown payment status '{statusStr}' for '{name}'. Use: Paid, Unpaid, or PartiallyPaid. Defaulting to Paid.");
                    }
                    var status = Enum.TryParse<PaymentStatus>(statusStr, true, out var ps) ? ps : PaymentStatus.Paid;
                    var paymentMethod = row.GetValueOrDefault("paymentmethod");

                    var lineTotal = qty.Value * unitPrice.Value;

                    var sale = new Sale
                    {
                        BusinessId = job.BusinessId,
                        ContactId = contactId,
                        TotalAmount = lineTotal,
                        PaymentStatus = status,
                        PaymentMethod = paymentMethod,
                        Source = EntrySource.Import,
                        RecordedByUserId = user?.Id,
                        RecordedByName = user?.FullName,
                        CreatedAtUtc = saleDate
                    };
                    sale.Items.Add(new SaleItem
                    {
                        ProductId = product.Id,
                        Quantity = qty.Value,
                        UnitPrice = unitPrice.Value,
                        TotalPrice = lineTotal
                    });
                    _db.Sales.Add(sale);

                    product.CurrentStock -= qty.Value;
                    _db.Entry(product).State = EntityState.Modified;

                    _db.InventoryTransactions.Add(new InventoryTransaction
                    {
                        BusinessId = job.BusinessId,
                        ProductId = product.Id,
                        Type = InventoryTransactionType.StockOut,
                        Quantity = qty.Value,
                        Notes = "Sale",
                        RecordedByUserId = user?.Id,
                        RecordedByName = user?.FullName,
                        CreatedAtUtc = saleDate
                    });

                    if (status != PaymentStatus.Paid && contactId.HasValue && lineTotal > 0)
                    {
                        _db.LedgerEntries.Add(new LedgerEntry
                        {
                            BusinessId = job.BusinessId,
                            ContactId = contactId.Value,
                            EntryType = LedgerEntryType.Receivable,
                            Amount = lineTotal,
                            Notes = "Credit sale",
                            Source = EntrySource.Import,
                            RecordedByUserId = user?.Id,
                            RecordedByName = user?.FullName,
                            ImportBatchId = job.Id,
                            CreatedAtUtc = saleDate
                        });
                    }

                    job.SuccessCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Import row {Row} failed for job {JobId}", rowNum, job.Id);
                    errors.Add($"Row {rowNum}: {FriendlyRowError(ex)}");
                }

                job.ProcessedRows = i + 1;
                if ((i + 1) % ProgressBatchSize == 0)
                {
                    _db.ChangeTracker.DetectChanges();
                    await _db.SaveChangesAsync();
                }
            }

            _db.ChangeTracker.DetectChanges();
            await _db.SaveChangesAsync();
        }
        finally
        {
            _db.ChangeTracker.AutoDetectChangesEnabled = true;
        }
    }

    private async Task ProcessExpensesRowsAsync(ImportJob job, List<Dictionary<string, string>> rows, User? user, List<string> errors, string cs)
    {
        _db.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var rowNum = i + 2;
                try
                {
                    var category = row.GetValueOrDefault("category") ?? "General";
                    if (category.Length > 100) { errors.Add($"Row {rowNum}: Category too long (max 100 characters): '{category[..30]}...'"); continue; }

                    var amount = CsvParser.ParseDecimal(row.GetValueOrDefault("amount"));
                    if (!ValidateAmount(amount, rowNum, $"category '{category}'", errors, cs)) continue;

                    var expDateStr = row.GetValueOrDefault("date") ?? row.GetValueOrDefault("expensedate");
                    if (string.IsNullOrWhiteSpace(expDateStr)) { errors.Add($"Row {rowNum}: Date is required for expense import. Add a 'Date' column (format: YYYY-MM-DD)."); continue; }
                    if (!DateTime.TryParse(expDateStr, out var expParsedDate)) { errors.Add($"Row {rowNum}: Invalid date '{expDateStr}'. Use format: YYYY-MM-DD."); continue; }

                    var expenseType = row.GetValueOrDefault("expensetype") ?? "operating";
                    var paymentMethod = row.GetValueOrDefault("paymentmethod");

                    _db.Expenses.Add(new Expense
                    {
                        BusinessId = job.BusinessId,
                        Category = category,
                        ExpenseType = expenseType,
                        Amount = amount!.Value,
                        PaidTo = row.GetValueOrDefault("paidto"),
                        PaymentMethod = paymentMethod,
                        Notes = row.GetValueOrDefault("notes"),
                        Source = EntrySource.Import,
                        RecordedByUserId = user?.Id,
                        RecordedByName = user?.FullName,
                        CreatedAtUtc = DateTime.SpecifyKind(expParsedDate, DateTimeKind.Utc),
                        ImportBatchId = job.Id
                    });

                    job.SuccessCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Import row {Row} failed for job {JobId}", rowNum, job.Id);
                    errors.Add($"Row {rowNum}: {FriendlyRowError(ex)}");
                }

                job.ProcessedRows = i + 1;
                if ((i + 1) % ProgressBatchSize == 0)
                {
                    _db.ChangeTracker.DetectChanges();
                    await _db.SaveChangesAsync();
                }
            }

            _db.ChangeTracker.DetectChanges();
            await _db.SaveChangesAsync();
        }
        finally
        {
            _db.ChangeTracker.AutoDetectChangesEnabled = true;
        }
    }

    // ─── Shared CSV validation helpers ──────────────────────────────────────────
    //
    // Each validator returns the cleaned value on success or null/false on failure, adding a
    // descriptive error. The error messages deliberately suggest "check your CSV column order"
    // when the data looks like a misplaced column — that's the #1 cause of bad imports.

    private static string? ValidatePhone(string? raw, int rowNum, string contactName, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Trim();
        if (trimmed.Length > 0 && !char.IsDigit(trimmed[0]) && trimmed[0] != '+')
        {
            errors.Add($"Row {rowNum}: '{trimmed}' doesn't look like a phone number for '{contactName}' — check your CSV column order");
            return null;
        }
        if (trimmed.Length > 20)
        {
            errors.Add($"Row {rowNum}: Phone number too long for '{contactName}' (max 20 characters)");
            return null;
        }
        return trimmed;
    }

    private static bool ValidateName(string? name, int rowNum, string fieldLabel, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(name)) { errors.Add($"Row {rowNum}: Missing {fieldLabel}"); return false; }
        if (name.Length > 200) { errors.Add($"Row {rowNum}: {fieldLabel} too long (max 200 characters): '{name[..50]}...'"); return false; }
        // Detect likely misplaced numeric column in a name field
        if (decimal.TryParse(name.Replace(",", ""), out _))
        {
            errors.Add($"Row {rowNum}: {fieldLabel} '{name}' looks like a number, not a name — check your CSV column order");
            return false;
        }
        return true;
    }

    private static bool ValidateAmount(decimal? amount, int rowNum, string context, List<string> errors, string cs = "₦")
    {
        if (!amount.HasValue || amount.Value <= 0)
        {
            errors.Add($"Row {rowNum}: Invalid or missing amount for {context}. Amount should be a positive number.");
            return false;
        }
        if (amount.Value > 100_000_000)
        {
            errors.Add($"Row {rowNum}: Amount {cs}{amount.Value:N0} for {context} seems unusually large (over {cs}100M). Check your CSV data.");
            return false;
        }
        return true;
    }

    private static bool ValidateQuantity(decimal? qty, int rowNum, string productName, List<string> errors)
    {
        if (!qty.HasValue || qty.Value <= 0)
        {
            errors.Add($"Row {rowNum}: Invalid quantity for '{productName}'. Should be a positive number.");
            return false;
        }
        if (qty.Value > 1_000_000)
        {
            errors.Add($"Row {rowNum}: Quantity {qty.Value:N0} for '{productName}' seems unusually large (over 1M). Check your CSV data.");
            return false;
        }
        return true;
    }

    private static readonly HashSet<string> ValidLedgerTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "receivable", "owes me", "they owe", "customer", "debt",
        "payable", "i owe", "we owe", "supplier", "creditor"
    };

    private static readonly HashSet<string> ValidPaymentStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "paid", "unpaid", "partiallypaid", "partially paid", "credit"
    };

    private async Task ProcessContactsRowsAsync(ImportJob job, List<Dictionary<string, string>> rows, List<string> errors)
    {
        var contactCache = await _db.Contacts
            .Where(c => c.BusinessId == job.BusinessId)
            .ToDictionaryAsync(c => c.Name.ToLower(), c => c);

        _db.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var rowNum = i + 2;
                try
                {
                    var name = row.GetValueOrDefault("contactname") ?? row.GetValueOrDefault("productname");
                    if (!ValidateName(name, rowNum, "Contact Name", errors)) continue;

                    var phone = ValidatePhone(row.GetValueOrDefault("phonenumber"), rowNum, name!, errors);
                    var typeStr = row.GetValueOrDefault("contacttype") ?? "Customer";
                    if (!string.IsNullOrEmpty(typeStr) && !Enum.TryParse<ContactType>(typeStr, true, out _) && typeStr != "Customer")
                    {
                        errors.Add($"Row {rowNum}: Unknown contact type '{typeStr}' for '{name}'. Use: Customer, Supplier, or Both. Defaulting to Customer.");
                    }
                    var contactType = Enum.TryParse<ContactType>(typeStr, true, out var ct) ? ct : ContactType.Customer;

                    if (contactCache.TryGetValue(name!.ToLower(), out var existing))
                    {
                        if (!string.IsNullOrEmpty(phone) && string.IsNullOrEmpty(existing.PhoneNumber))
                        {
                            existing.PhoneNumber = phone;
                            _db.Entry(existing).State = EntityState.Modified;
                        }
                        if (existing.Type != contactType && contactType == ContactType.Both)
                        {
                            existing.Type = ContactType.Both;
                            _db.Entry(existing).State = EntityState.Modified;
                        }
                    }
                    else
                    {
                        var contact = new Contact
                        {
                            BusinessId = job.BusinessId,
                            Name = name!.Trim(),
                            PhoneNumber = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(),
                            Type = contactType,
                            Source = EntrySource.Import,
                            ImportBatchId = job.Id
                        };
                        _db.Contacts.Add(contact);
                        contactCache[name!.ToLower()] = contact;
                    }

                    job.SuccessCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Import row {Row} failed for job {JobId}", rowNum, job.Id);
                    errors.Add($"Row {rowNum}: {FriendlyRowError(ex)}");
                }

                job.ProcessedRows = i + 1;
                if ((i + 1) % ProgressBatchSize == 0)
                {
                    _db.ChangeTracker.DetectChanges();
                    await _db.SaveChangesAsync();
                }
            }

            _db.ChangeTracker.DetectChanges();
            await _db.SaveChangesAsync();
        }
        finally
        {
            _db.ChangeTracker.AutoDetectChangesEnabled = true;
        }
    }

    private async Task ProcessContactsWithLedgerRowsAsync(ImportJob job, List<Dictionary<string, string>> rows, User? user, List<string> errors, string cs)
    {
        var contactCache = await _db.Contacts
            .Where(c => c.BusinessId == job.BusinessId)
            .ToDictionaryAsync(c => c.Name.ToLower(), c => c);

        _db.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var rowNum = i + 2;
                try
                {
                    var name = row.GetValueOrDefault("contactname") ?? row.GetValueOrDefault("productname");
                    if (!ValidateName(name, rowNum, "Contact Name", errors)) continue;

                    var amount = CsvParser.ParseDecimal(row.GetValueOrDefault("amount"));
                    if (!ValidateAmount(amount, rowNum, $"'{name}'", errors, cs)) continue;

                    var ledgerTypeStr = row.GetValueOrDefault("ledgertype") ?? "receivable";
                    if (!ValidLedgerTypes.Contains(ledgerTypeStr))
                    {
                        errors.Add($"Row {rowNum}: Invalid ledger type '{ledgerTypeStr}' for '{name}'. Use 'Receivable' (they owe you) or 'Payable' (you owe them). Check CSV column order.");
                        continue;
                    }

                    var isReceivable = ledgerTypeStr.ToLowerInvariant() switch
                    {
                        "receivable" or "owes me" or "they owe" or "customer" or "debt" => true,
                        _ => false
                    };

                    var phone = ValidatePhone(row.GetValueOrDefault("phonenumber"), rowNum, name!, errors);
                    var typeStr = row.GetValueOrDefault("contacttype");
                    var contactType = !string.IsNullOrEmpty(typeStr) && Enum.TryParse<ContactType>(typeStr, true, out var ct)
                        ? ct
                        : (isReceivable ? ContactType.Customer : ContactType.Supplier);

                    var notes = row.GetValueOrDefault("notes");

                    if (!contactCache.TryGetValue(name!.ToLower(), out var contact))
                    {
                        contact = new Contact
                        {
                            BusinessId = job.BusinessId,
                            Name = name!.Trim(),
                            PhoneNumber = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(),
                            Type = contactType,
                            Source = EntrySource.Import,
                            ImportBatchId = job.Id
                        };
                        _db.Contacts.Add(contact);
                        contactCache[name!.ToLower()] = contact;
                    }

                    var ledgerDateStr = row.GetValueOrDefault("date") ?? row.GetValueOrDefault("debtdate");
                    if (string.IsNullOrWhiteSpace(ledgerDateStr)) { errors.Add($"Row {rowNum}: Date is required for ledger import. Add a 'Date' column (format: YYYY-MM-DD)."); continue; }
                    if (!DateTime.TryParse(ledgerDateStr, out var ledgerParsedDate)) { errors.Add($"Row {rowNum}: Invalid date '{ledgerDateStr}'. Use format: YYYY-MM-DD."); continue; }

                    var entryType = isReceivable ? LedgerEntryType.Receivable : LedgerEntryType.Payable;
                    _db.LedgerEntries.Add(new LedgerEntry
                    {
                        BusinessId = job.BusinessId,
                        ContactId = contact.Id,
                        EntryType = entryType,
                        Amount = amount!.Value,
                        Notes = notes,
                        Source = EntrySource.Import,
                        ImportBatchId = job.Id,
                        RecordedByUserId = user?.Id,
                        RecordedByName = user?.FullName,
                        CreatedAtUtc = DateTime.SpecifyKind(ledgerParsedDate, DateTimeKind.Utc)
                    });

                    job.SuccessCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Import row {Row} failed for job {JobId}", rowNum, job.Id);
                    errors.Add($"Row {rowNum}: {FriendlyRowError(ex)}");
                }

                job.ProcessedRows = i + 1;
                if ((i + 1) % ProgressBatchSize == 0)
                {
                    _db.ChangeTracker.DetectChanges();
                    await _db.SaveChangesAsync();
                }
            }

            _db.ChangeTracker.DetectChanges();
            await _db.SaveChangesAsync();
        }
        finally
        {
            _db.ChangeTracker.AutoDetectChangesEnabled = true;
        }
    }

    private async Task SendCompletionMessageAsync(ImportJob job)
    {
        try
        {
            var owner = await _db.Users.FirstOrDefaultAsync(u =>
                u.BusinessId == job.BusinessId && u.Role == UserRole.Owner && u.IsActive);
            if (owner == null) return;

            var typeLabel = job.Type.ToString().ToLower();
            var icon = job.ErrorCount == 0 ? "✅" : "⚠️";
            var msg = $"{icon} *{char.ToUpper(typeLabel[0]) + typeLabel[1..]} import finished*\n\n" +
                      $"📄 File: {job.FileName}\n" +
                      $"✓ Imported: {job.SuccessCount}\n" +
                      $"✗ Skipped: {job.ErrorCount}\n" +
                      $"📊 Total rows: {job.TotalRows}\n\n" +
                      $"View details: app.bizpilot-ai.com/import";

            await _whatsApp.SendMessageAsync($"whatsapp:{owner.PhoneNumber}", msg, job.BusinessId, owner.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send import completion message for {JobId}", job.Id);
        }
    }
}
