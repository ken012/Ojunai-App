using Ojunai.API.Common;
using Ojunai.API.Data;
using Ojunai.API.DTOs.Sales;
using Ojunai.API.Models;
using Ojunai.API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Services;

public class SalesService : ISalesService
{
    private readonly AppDbContext _db;

    public SalesService(AppDbContext db) => _db = db;

    /// <summary>
    /// Creates a sale atomically: validates stock, deducts inventory, records inventory transactions, and saves the sale.
    /// Uses optimistic concurrency (Product.Version row token) to prevent two concurrent sales from overselling the same stock.
    /// If a concurrent transaction modified a product's stock between our read and write, we retry with fresh data up to 3 times.
    /// After retries exhaust, we surface a user-friendly "high contention" error — this would indicate sustained heavy load,
    /// not a normal occurrence, so failing loudly is correct.
    /// </summary>
    public async Task<SaleDto> CreateAsync(Guid businessId, CreateSaleRequest request, string source = "Manual", Guid? recordedByUserId = null, string? recordedByName = null)
    {
        const int maxRetries = 3;
        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                return await TryCreateSaleAsync(businessId, request, source, recordedByUserId, recordedByName);
            }
            catch (DbUpdateConcurrencyException) when (attempt < maxRetries - 1)
            {
                // A parallel sale or restock changed a product's stock while we were preparing ours.
                // Detach the stale entity tracking so the next attempt fetches fresh stock values from the database.
                foreach (var entry in _db.ChangeTracker.Entries().ToList())
                    entry.State = EntityState.Detached;
            }
        }
        throw new InvalidOperationException("Could not complete sale due to high contention. Please try again.");
    }

    private async Task<SaleDto> TryCreateSaleAsync(Guid businessId, CreateSaleRequest request, string source, Guid? recordedByUserId, string? recordedByName)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();

        var productIds = request.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await _db.Products
            .Where(p => p.BusinessId == businessId && productIds.Contains(p.Id) && p.IsActive)
            .ToDictionaryAsync(p => p.Id);

        // Bundles: a sold bundle depletes its COMPONENT products' stock, not its own. Load the
        // component maps + component products (tracked, so decrements persist and rowversion
        // concurrency still applies). Non-bundle products are entirely unaffected by this.
        var bundleIds = products.Values.Where(p => p.IsBundle).Select(p => p.Id).ToList();
        var componentsByBundle = new Dictionary<Guid, List<BundleComponent>>();
        var componentProducts = new Dictionary<Guid, Product>();
        if (bundleIds.Count > 0)
        {
            var comps = await _db.BundleComponents
                .Where(c => c.BusinessId == businessId && bundleIds.Contains(c.BundleProductId))
                .ToListAsync();
            componentsByBundle = comps.GroupBy(c => c.BundleProductId).ToDictionary(g => g.Key, g => g.ToList());
            var compIds = comps.Select(c => c.ComponentProductId).Distinct().ToList();
            componentProducts = await _db.Products
                .Where(p => p.BusinessId == businessId && compIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id);
        }

        foreach (var item in request.Items)
        {
            if (!products.TryGetValue(item.ProductId, out var product))
                throw new KeyNotFoundException($"Product {item.ProductId} not found.");
            if (product.IsBundle)
            {
                var comps = componentsByBundle.GetValueOrDefault(product.Id) ?? new List<BundleComponent>();
                if (comps.Count == 0)
                    throw new InvalidOperationException($"'{product.Name}' is a bundle with no items set up yet. Edit it to add components.");
                foreach (var c in comps)
                {
                    if (!componentProducts.TryGetValue(c.ComponentProductId, out var cp))
                        throw new KeyNotFoundException($"A component of '{product.Name}' no longer exists. Fix the bundle before selling it.");
                    var required = c.Quantity * item.Quantity;
                    if (cp.CurrentStock < required)
                        throw new InvalidOperationException($"Not enough '{cp.Name}' to make {item.Quantity:0.##} {product.Name}. Need {required:0.##} {UnitFormat.Plural(required, cp.Unit)}, have {cp.CurrentStock:0.##}.");
                }
            }
            else if (product.CurrentStock < item.Quantity)
                throw new InvalidOperationException($"Insufficient stock for '{product.Name}'. Available: {product.CurrentStock} {UnitFormat.Plural(product.CurrentStock, product.Unit)}.");
        }

        // ── VAT processing ────────────────────────────────────────────────────
        // When the business has VAT enabled and the caller didn't pre-compute VatAmount
        // (dashboard does; chat handlers don't), derive VAT per item based on whether the
        // UnitPrice came from the product catalog or was user-stated:
        //
        //   - UnitPriceFromCatalog = true → stored SellingPrice is NET. Add VAT on top.
        //     Customer ends up paying  price × qty × (1 + rate/100).
        //   - UnitPriceFromCatalog = false → user-typed price is GROSS (the amount the
        //     customer was charged in total). Derive the VAT portion from inside.
        //
        // SaleItem.UnitPrice is always stored as the GROSS unit price so the receipt math
        // ("subtotal = total - vat") and per-line displays both stay consistent.
        var business = await _db.Businesses.FindAsync(businessId);
        bool vatOn = business != null && business.VatEnabled && business.VatRate > 0;
        decimal vatRate = vatOn ? business!.VatRate : 0m;
        decimal autoVatAmount = 0m;
        var grossUnitByIndex = new Dictionary<int, decimal>();
        for (int idx = 0; idx < request.Items.Count; idx++)
        {
            var itemReq = request.Items[idx];
            decimal grossUnit;
            if (vatOn && itemReq.UnitPriceFromCatalog)
            {
                // Stored selling price = net; convert to gross + record VAT
                grossUnit = Math.Round(itemReq.UnitPrice * (1m + vatRate / 100m), 2);
                autoVatAmount += Math.Round(itemReq.UnitPrice * itemReq.Quantity * vatRate / 100m, 2);
            }
            else if (vatOn && request.VatAmount == null)
            {
                // User-typed price = gross; VAT lives inside (only auto-compute when caller didn't)
                grossUnit = itemReq.UnitPrice;
                autoVatAmount += Math.Round(grossUnit * itemReq.Quantity * vatRate / (100m + vatRate), 2);
            }
            else
            {
                grossUnit = itemReq.UnitPrice;
            }
            grossUnitByIndex[idx] = grossUnit;
        }

        var sale = new Sale
        {
            BusinessId = businessId,
            ContactId = request.ContactId,
            PaymentStatus = request.PaymentStatus,
            PaymentMethod = request.PaymentMethod,
            Notes = request.Notes,
            Source = source,
            VatAmount = request.VatAmount ?? autoVatAmount,
            RecordedByUserId = recordedByUserId,
            RecordedByName = recordedByName,
            CreatedAtUtc = request.SaleDate ?? DateTime.UtcNow
        };

        decimal total = 0;
        var inventoryTxns = new List<InventoryTransaction>();

        for (int idx = 0; idx < request.Items.Count; idx++)
        {
            var itemReq = request.Items[idx];
            var product = products[itemReq.ProductId];
            var grossUnit = grossUnitByIndex[idx];
            var lineTotal = itemReq.Quantity * grossUnit;
            total += lineTotal;

            sale.Items.Add(new SaleItem
            {
                ProductId = itemReq.ProductId,
                Quantity = itemReq.Quantity,
                UnitPrice = grossUnit,
                TotalPrice = lineTotal
            });

            if (product.IsBundle)
            {
                // Deplete each component instead of the bundle itself.
                foreach (var c in componentsByBundle.GetValueOrDefault(product.Id) ?? new List<BundleComponent>())
                {
                    var cp = componentProducts[c.ComponentProductId];
                    var qty = c.Quantity * itemReq.Quantity;
                    cp.CurrentStock -= qty;
                    inventoryTxns.Add(new InventoryTransaction
                    {
                        BusinessId = businessId,
                        ProductId = cp.Id,
                        Type = InventoryTransactionType.StockOut,
                        Quantity = qty,
                        Notes = $"Sale (bundle: {product.Name})",
                        RecordedByUserId = recordedByUserId,
                        RecordedByName = recordedByName,
                        CreatedAtUtc = sale.CreatedAtUtc
                    });
                }
            }
            else
            {
                product.CurrentStock -= itemReq.Quantity;

                inventoryTxns.Add(new InventoryTransaction
                {
                    BusinessId = businessId,
                    ProductId = itemReq.ProductId,
                    Type = InventoryTransactionType.StockOut,
                    Quantity = itemReq.Quantity,
                    Notes = $"Sale",
                    RecordedByUserId = recordedByUserId,
                    RecordedByName = recordedByName,
                    CreatedAtUtc = sale.CreatedAtUtc
                });
            }
        }

        sale.TotalAmount = total;

        // Invariant check: TotalAmount must equal sum of item line totals
        var itemsSum = sale.Items.Sum(i => i.TotalPrice);
        if (Math.Abs(sale.TotalAmount - itemsSum) > 0.01m)
            throw new InvalidOperationException($"Sale total mismatch: header {sale.TotalAmount} vs items sum {itemsSum}.");

        _db.Sales.Add(sale);
        _db.InventoryTransactions.AddRange(inventoryTxns);

        // Auto-create receivable for credit sales so it shows in Contacts & Ledger
        if (sale.PaymentStatus != PaymentStatus.Paid && sale.ContactId.HasValue && sale.TotalAmount > 0)
        {
            var itemsSummary = string.Join(", ", sale.Items.Select(i =>
                $"{i.Quantity:0.##} {UnitFormat.Plural(i.Quantity, products[i.ProductId].Unit)} {products[i.ProductId].Name}"));
            _db.LedgerEntries.Add(new LedgerEntry
            {
                BusinessId = businessId,
                ContactId = sale.ContactId.Value,
                EntryType = LedgerEntryType.Receivable,
                Amount = sale.TotalAmount,
                Notes = $"Credit sale: {itemsSummary}",
                Source = source,
                RecordedByUserId = recordedByUserId,
                RecordedByName = recordedByName,
                CreatedAtUtc = sale.CreatedAtUtc
            });
        }

        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        return await GetByIdAsync(businessId, sale.Id);
    }

    public async Task<PaginatedResult<SaleSummaryDto>> GetAllAsync(
        Guid businessId, int page, int pageSize, DateTime? from, DateTime? to,
        string? paymentStatus = null, string? paymentMethod = null, string? source = null, Guid? customerId = null, string? search = null)
    {
        var query = _db.Sales
            .Include(s => s.Contact)
            .Include(s => s.Items).ThenInclude(i => i.Product)
            .Where(s => s.BusinessId == businessId);

        if (from.HasValue) query = query.Where(s => s.CreatedAtUtc >= from.Value);
        if (to.HasValue) query = query.Where(s => s.CreatedAtUtc <= to.Value);
        if (!string.IsNullOrEmpty(paymentStatus) && Enum.TryParse<PaymentStatus>(paymentStatus, true, out var ps))
            query = query.Where(s => s.PaymentStatus == ps);
        if (!string.IsNullOrEmpty(paymentMethod))
            query = query.Where(s => s.PaymentMethod == paymentMethod);
        if (!string.IsNullOrEmpty(source))
            query = query.Where(s => s.Source == source);
        if (customerId.HasValue)
            query = query.Where(s => s.ContactId == customerId);
        if (!string.IsNullOrEmpty(search))
        {
            var startPattern = $"{search}%";
            var wordPattern = $"% {search}%";
            query = query.Where(s =>
                (s.Contact != null && (EF.Functions.ILike(s.Contact.Name, startPattern) || EF.Functions.ILike(s.Contact.Name, wordPattern)))
                || s.Items.Any(i => EF.Functions.ILike(i.Product.Name, startPattern) || EF.Functions.ILike(i.Product.Name, wordPattern)));
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(s => s.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new SaleSummaryDto
            {
                Id = s.Id,
                TotalAmount = s.TotalAmount,
                PaymentStatus = s.PaymentStatus.ToString(),
                PaymentMethod = s.PaymentMethod,
                ItemCount = s.Items.Count,
                ItemSummary = string.Join(", ", s.Items.Select(i => $"{i.Quantity:0.##} {UnitFormat.Plural(i.Quantity, i.Product.Unit)} {i.Product.Name}")),
                ContactId = s.ContactId,
                CustomerName = s.Contact != null ? s.Contact.Name : null,
                RecordedByName = s.RecordedByName,
                Source = s.Source,
                CreatedAtUtc = s.CreatedAtUtc
            })
            .ToListAsync();

        return new PaginatedResult<SaleSummaryDto>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task VoidAsync(Guid businessId, Guid saleId, Guid? voidedByUserId = null, string? voidedByName = null)
    {
        var sale = await _db.Sales
            .Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.Id == saleId && s.BusinessId == businessId)
            ?? throw new KeyNotFoundException("Sale not found.");

        if (sale.IsDeleted)
            throw new InvalidOperationException("Sale is already voided.");

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var productIds = sale.Items.Select(i => i.ProductId).ToList();
            var products = await _db.Products
                .Where(p => productIds.Contains(p.Id) && p.BusinessId == businessId)
                .ToDictionaryAsync(p => p.Id);

            // Build a readable summary of what was in the sale for audit notes
            var saleSummary = string.Join(", ", sale.Items
                .Where(i => products.ContainsKey(i.ProductId))
                .Select(i => $"{i.Quantity:0.##} {UnitFormat.Plural(i.Quantity, products[i.ProductId].Unit)} {products[i.ProductId].Name}"));
            var customerNote = sale.Contact != null ? $" to {sale.Contact.Name}" : "";
            var business = await _db.Businesses.FindAsync(businessId);
            var cs = BillingConfig.Symbol(business?.Currency);

            foreach (var item in sale.Items)
            {
                if (products.TryGetValue(item.ProductId, out var product))
                {
                    product.CurrentStock += item.Quantity;
                    _db.InventoryTransactions.Add(new InventoryTransaction
                    {
                        BusinessId = businessId,
                        ProductId = item.ProductId,
                        Type = InventoryTransactionType.Adjustment,
                        Quantity = item.Quantity,
                        Notes = $"Voided sale: {item.Quantity:0.##} {UnitFormat.Plural(item.Quantity, product.Unit)} {product.Name} ({cs}{item.TotalPrice:N0}) returned to stock",
                        RecordedByUserId = voidedByUserId ?? sale.RecordedByUserId,
                        RecordedByName = voidedByName ?? sale.RecordedByName
                    });
                }
            }

            // Reverse any receivable created for this credit sale
            if (sale.PaymentStatus != PaymentStatus.Paid && sale.ContactId.HasValue && sale.TotalAmount > 0)
            {
                _db.LedgerEntries.Add(new LedgerEntry
                {
                    BusinessId = businessId,
                    ContactId = sale.ContactId.Value,
                    EntryType = LedgerEntryType.ReceivablePayment,
                    Amount = sale.TotalAmount,
                    Notes = $"Voided sale{customerNote}: {saleSummary} ({cs}{sale.TotalAmount:N0}) — receivable reversed",
                    Source = "Adjustment",
                    RecordedByUserId = voidedByUserId,
                    RecordedByName = voidedByName
                });
            }

            sale.IsDeleted = true;
            sale.DeleteReason = "voided";
            sale.DeletedAtUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task ReturnAsync(Guid businessId, Guid saleId, Guid? returnedByUserId = null, string? returnedByName = null)
    {
        var sale = await _db.Sales
            .Include(s => s.Items)
            .Include(s => s.Contact)
            .FirstOrDefaultAsync(s => s.Id == saleId && s.BusinessId == businessId)
            ?? throw new KeyNotFoundException("Sale not found.");

        if (sale.IsDeleted)
            throw new InvalidOperationException("Sale is already voided or returned.");

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var productIds = sale.Items.Select(i => i.ProductId).ToList();
            var products = await _db.Products
                .Where(p => productIds.Contains(p.Id) && p.BusinessId == businessId)
                .ToDictionaryAsync(p => p.Id);

            var saleSummary = string.Join(", ", sale.Items
                .Where(i => products.ContainsKey(i.ProductId))
                .Select(i => $"{i.Quantity:0.##} {UnitFormat.Plural(i.Quantity, products[i.ProductId].Unit)} {products[i.ProductId].Name}"));
            var customerNote = sale.Contact != null ? $" to {sale.Contact.Name}" : "";
            var business = await _db.Businesses.FindAsync(businessId);
            var cs = BillingConfig.Symbol(business?.Currency);

            foreach (var item in sale.Items)
            {
                if (products.TryGetValue(item.ProductId, out var product))
                {
                    product.CurrentStock += item.Quantity;
                    _db.InventoryTransactions.Add(new InventoryTransaction
                    {
                        BusinessId = businessId,
                        ProductId = item.ProductId,
                        Type = InventoryTransactionType.Adjustment,
                        Quantity = item.Quantity,
                        Notes = $"Returned sale: {item.Quantity:0.##} {UnitFormat.Plural(item.Quantity, product.Unit)} {product.Name} ({cs}{item.TotalPrice:N0}) returned to stock",
                        RecordedByUserId = returnedByUserId ?? sale.RecordedByUserId,
                        RecordedByName = returnedByName ?? sale.RecordedByName
                    });
                }
            }

            // Reverse any receivable created for this credit sale
            if (sale.PaymentStatus != PaymentStatus.Paid && sale.ContactId.HasValue && sale.TotalAmount > 0)
            {
                _db.LedgerEntries.Add(new LedgerEntry
                {
                    BusinessId = businessId,
                    ContactId = sale.ContactId.Value,
                    EntryType = LedgerEntryType.ReceivablePayment,
                    Amount = sale.TotalAmount,
                    Notes = $"Returned sale{customerNote}: {saleSummary} ({cs}{sale.TotalAmount:N0}) — receivable reversed",
                    Source = "Adjustment",
                    RecordedByUserId = returnedByUserId,
                    RecordedByName = returnedByName
                });
            }

            sale.IsDeleted = true;
            sale.DeleteReason = "returned";
            sale.DeletedAtUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task<PaginatedResult<SaleSummaryDto>> GetVoidedAsync(Guid businessId, int page, int pageSize)
    {
        var query = _db.Sales
            .IgnoreQueryFilters()
            .Include(s => s.Contact)
            .Include(s => s.Items).ThenInclude(i => i.Product)
            .Where(s => s.BusinessId == businessId && s.IsDeleted && (s.DeleteReason == null || s.DeleteReason == "voided"));

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(s => s.DeletedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new SaleSummaryDto
            {
                Id = s.Id,
                TotalAmount = s.TotalAmount,
                PaymentStatus = s.PaymentStatus.ToString(),
                PaymentMethod = s.PaymentMethod,
                ItemCount = s.Items.Count,
                ItemSummary = string.Join(", ", s.Items.Select(i => $"{i.Quantity:0.##} {UnitFormat.Plural(i.Quantity, i.Product.Unit)} {i.Product.Name}")),
                ContactId = s.ContactId,
                CustomerName = s.Contact != null ? s.Contact.Name : null,
                RecordedByName = s.RecordedByName,
                Source = s.Source,
                CreatedAtUtc = s.CreatedAtUtc,
                DeletedAtUtc = s.DeletedAtUtc
            })
            .ToListAsync();

        return new PaginatedResult<SaleSummaryDto>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<PaginatedResult<SaleSummaryDto>> GetReturnedAsync(Guid businessId, int page, int pageSize)
    {
        var query = _db.Sales
            .IgnoreQueryFilters()
            .Include(s => s.Contact)
            .Include(s => s.Items).ThenInclude(i => i.Product)
            .Where(s => s.BusinessId == businessId && s.IsDeleted && s.DeleteReason == "returned");

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(s => s.DeletedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new SaleSummaryDto
            {
                Id = s.Id,
                TotalAmount = s.TotalAmount,
                PaymentStatus = s.PaymentStatus.ToString(),
                PaymentMethod = s.PaymentMethod,
                ItemCount = s.Items.Count,
                ItemSummary = string.Join(", ", s.Items.Select(i => $"{i.Quantity:0.##} {UnitFormat.Plural(i.Quantity, i.Product.Unit)} {i.Product.Name}")),
                ContactId = s.ContactId,
                CustomerName = s.Contact != null ? s.Contact.Name : null,
                RecordedByName = s.RecordedByName,
                Source = s.Source,
                CreatedAtUtc = s.CreatedAtUtc,
                DeletedAtUtc = s.DeletedAtUtc
            })
            .ToListAsync();

        return new PaginatedResult<SaleSummaryDto>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<SaleDto> GetByIdAsync(Guid businessId, Guid saleId)
    {
        var sale = await _db.Sales
            .Include(s => s.Contact)
            .Include(s => s.Items).ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(s => s.Id == saleId && s.BusinessId == businessId)
            ?? throw new KeyNotFoundException("Sale not found.");

        decimal? contactBalance = null;
        DateTime? dueDate = null;

        if (sale.ContactId.HasValue)
        {
            // Compute the contact's overall outstanding receivable balance:
            // sum of Receivable entries minus sum of ReceivablePayment entries.
            var ledgerEntries = await _db.LedgerEntries
                .Where(e => e.BusinessId == businessId && e.ContactId == sale.ContactId.Value
                    && (e.EntryType == Models.LedgerEntryType.Receivable || e.EntryType == Models.LedgerEntryType.ReceivablePayment))
                .ToListAsync();

            contactBalance = ledgerEntries.Sum(e =>
                e.EntryType == Models.LedgerEntryType.Receivable ? e.Amount : -e.Amount);
            if (contactBalance < 0) contactBalance = 0;

            // Earliest due date from unpaid receivables for this contact.
            dueDate = ledgerEntries
                .Where(e => e.EntryType == Models.LedgerEntryType.Receivable && e.DueDate.HasValue)
                .OrderBy(e => e.DueDate)
                .Select(e => e.DueDate)
                .FirstOrDefault();
        }

        return new SaleDto
        {
            Id = sale.Id,
            TotalAmount = sale.TotalAmount,
            VatAmount = sale.VatAmount,
            PaymentStatus = sale.PaymentStatus.ToString(),
            PaymentMethod = sale.PaymentMethod,
            Notes = sale.Notes,
            ContactId = sale.ContactId,
            CustomerName = sale.Contact?.Name,
            RecordedByName = sale.RecordedByName,
            Source = sale.Source,
            ReceiptNumber = sale.ReceiptNumber,
            CreatedAtUtc = sale.CreatedAtUtc,
            ContactBalance = contactBalance,
            DueDate = dueDate,
            Items = sale.Items.Select(i => new SaleItemDto
            {
                ProductId = i.ProductId,
                ProductName = i.Product.Name,
                Unit = i.Product.Unit,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice,
                TotalPrice = i.TotalPrice
            }).ToList()
        };
    }
}
