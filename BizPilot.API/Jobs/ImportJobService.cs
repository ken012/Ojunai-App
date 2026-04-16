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

            switch (job.Type)
            {
                case ImportJobType.Inventory:
                    await ProcessInventoryRowsAsync(job, rows, user, errors);
                    break;
                case ImportJobType.Sales:
                    await ProcessSalesRowsAsync(job, rows, user, errors);
                    break;
                case ImportJobType.Expenses:
                    await ProcessExpensesRowsAsync(job, rows, user, errors);
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

    private async Task ProcessInventoryRowsAsync(ImportJob job, List<Dictionary<string, string>> rows, User? user, List<string> errors)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var rowNum = i + 2;
            try
            {
                var name = row.GetValueOrDefault("productname");
                var qtyStr = row.GetValueOrDefault("quantity");

                if (string.IsNullOrEmpty(name)) { errors.Add($"Row {rowNum}: Missing ProductName"); continue; }
                var qty = CsvParser.ParseDecimal(qtyStr);
                if (!qty.HasValue || qty.Value <= 0) { errors.Add($"Row {rowNum}: Invalid quantity for '{name}'"); continue; }

                var unit = row.GetValueOrDefault("unit") ?? UnitInferrer.Infer(name);
                var costPrice = CsvParser.ParseDecimal(row.GetValueOrDefault("costprice"));
                var sellingPrice = CsvParser.ParseDecimal(row.GetValueOrDefault("sellingprice"));
                var csvCategory = row.GetValueOrDefault("category");
                var csvSubcategory = row.GetValueOrDefault("subcategory");
                var csvThreshold = CsvParser.ParseDecimal(row.GetValueOrDefault("threshold"));

                var existing = await _db.Products.FirstOrDefaultAsync(p =>
                    p.BusinessId == job.BusinessId && p.IsActive && p.Name.ToLower() == name.ToLower());

                if (existing == null)
                {
                    var (inferredCat, inferredSubcat) = CategoryInferrer.Infer(name);
                    await _products.CreateAsync(job.BusinessId, new CreateProductRequest
                    {
                        Name = name,
                        Unit = unit,
                        CostPrice = costPrice,
                        SellingPrice = sellingPrice,
                        InitialStock = qty.Value,
                        LowStockThreshold = csvThreshold ?? 5,
                        Category = csvCategory ?? inferredCat,
                        Subcategory = csvSubcategory ?? inferredSubcat
                    }, user?.Id, user?.FullName);
                }
                else
                {
                    var effectiveCost = costPrice ?? existing.CostPrice;
                    await _inventory.StockInAsync(job.BusinessId, new StockInRequest
                    {
                        ProductId = existing.Id,
                        Quantity = qty.Value,
                        UnitCost = effectiveCost
                    }, user?.Id, user?.FullName);

                    if (sellingPrice.HasValue && !existing.SellingPrice.HasValue) existing.SellingPrice = sellingPrice.Value;
                    if (costPrice.HasValue && !existing.CostPrice.HasValue) existing.CostPrice = costPrice.Value;
                }

                var expenseCost = costPrice ?? existing?.CostPrice;
                if (expenseCost.HasValue && expenseCost.Value > 0)
                {
                    await _expenses.CreateAsync(job.BusinessId, new CreateExpenseRequest
                    {
                        Category = "Inventory",
                        Amount = qty.Value * expenseCost.Value,
                        Notes = $"Import: {qty.Value:0.##} {unit} of {name} @ ₦{expenseCost.Value:N0}"
                    }, EntrySource.Import, user?.Id, user?.FullName);
                }

                job.SuccessCount++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Import row {Row} failed for job {JobId}", rowNum, job.Id);
                errors.Add($"Row {rowNum}: {FriendlyRowError(ex)}");
            }

            job.ProcessedRows = i + 1;
            if ((i + 1) % ProgressBatchSize == 0) await _db.SaveChangesAsync();
        }

        await _db.SaveChangesAsync();
    }

    private async Task ProcessSalesRowsAsync(ImportJob job, List<Dictionary<string, string>> rows, User? user, List<string> errors)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var rowNum = i + 2;
            try
            {
                var name = row.GetValueOrDefault("productname");
                var qtyStr = row.GetValueOrDefault("quantity");
                if (string.IsNullOrEmpty(name)) { errors.Add($"Row {rowNum}: Missing ProductName"); continue; }
                var qty = CsvParser.ParseDecimal(qtyStr);
                if (!qty.HasValue || qty.Value <= 0) { errors.Add($"Row {rowNum}: Invalid quantity for '{name}'"); continue; }

                var product = await _db.Products.FirstOrDefaultAsync(p =>
                    p.BusinessId == job.BusinessId && p.IsActive && p.Name.ToLower() == name.ToLower());
                if (product == null) { errors.Add($"Row {rowNum}: Product '{name}' not found in inventory"); continue; }

                var unitPrice = CsvParser.ParseDecimal(row.GetValueOrDefault("unitprice"));
                if (!unitPrice.HasValue || unitPrice.Value <= 0) { errors.Add($"Row {rowNum}: UnitPrice is required for '{name}'"); continue; }

                var customerName = row.GetValueOrDefault("customername");
                Guid? contactId = null;
                if (!string.IsNullOrEmpty(customerName))
                {
                    var contact = await _db.Contacts.FirstOrDefaultAsync(c =>
                        c.BusinessId == job.BusinessId && c.Name.ToLower() == customerName.ToLower());
                    if (contact == null)
                    {
                        contact = new Contact
                        {
                            BusinessId = job.BusinessId,
                            Name = customerName,
                            Type = ContactType.Customer,
                            Source = EntrySource.Import
                        };
                        _db.Contacts.Add(contact);
                        await _db.SaveChangesAsync();
                    }
                    contactId = contact.Id;
                }

                var statusStr = row.GetValueOrDefault("paymentstatus") ?? "Paid";
                var status = Enum.TryParse<PaymentStatus>(statusStr, true, out var ps) ? ps : PaymentStatus.Paid;

                await _sales.CreateAsync(job.BusinessId, new CreateSaleRequest
                {
                    Items = new List<SaleItemRequest>
                    {
                        new() { ProductId = product.Id, Quantity = qty.Value, UnitPrice = unitPrice.Value }
                    },
                    ContactId = contactId,
                    PaymentStatus = status
                }, EntrySource.Import, user?.Id, user?.FullName);

                job.SuccessCount++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Import row {Row} failed for job {JobId}", rowNum, job.Id);
                errors.Add($"Row {rowNum}: {FriendlyRowError(ex)}");
            }

            job.ProcessedRows = i + 1;
            if ((i + 1) % ProgressBatchSize == 0) await _db.SaveChangesAsync();
        }

        await _db.SaveChangesAsync();
    }

    private async Task ProcessExpensesRowsAsync(ImportJob job, List<Dictionary<string, string>> rows, User? user, List<string> errors)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var rowNum = i + 2;
            try
            {
                var category = row.GetValueOrDefault("category") ?? "General";
                var amount = CsvParser.ParseDecimal(row.GetValueOrDefault("amount"));
                if (!amount.HasValue || amount.Value <= 0) { errors.Add($"Row {rowNum}: Invalid amount"); continue; }

                await _expenses.CreateAsync(job.BusinessId, new CreateExpenseRequest
                {
                    Category = category,
                    Amount = amount.Value,
                    PaidTo = row.GetValueOrDefault("paidto"),
                    Notes = row.GetValueOrDefault("notes")
                }, EntrySource.Import, user?.Id, user?.FullName);

                job.SuccessCount++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Import row {Row} failed for job {JobId}", rowNum, job.Id);
                errors.Add($"Row {rowNum}: {FriendlyRowError(ex)}");
            }

            job.ProcessedRows = i + 1;
            if ((i + 1) % ProgressBatchSize == 0) await _db.SaveChangesAsync();
        }

        await _db.SaveChangesAsync();
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
