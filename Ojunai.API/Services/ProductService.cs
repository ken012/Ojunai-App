using Ojunai.API.Common;
using Ojunai.API.Data;
using Ojunai.API.DTOs.Products;
using Ojunai.API.Models;
using Ojunai.API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Services;

public class ProductService : IProductService
{
    private readonly AppDbContext _db;
    private readonly IActivityLogger _activity;

    public ProductService(AppDbContext db, IActivityLogger activity)
    {
        _db = db;
        _activity = activity;
    }

    public async Task<PaginatedResult<ProductDto>> GetAllAsync(
        Guid businessId, int page, int pageSize,
        string? search, string? category = null, string? stockLevel = null, bool excludeVariants = false)
    {
        var query = _db.Products
            .Where(p => p.BusinessId == businessId && p.IsActive);

        // The inventory list opts into this so variant members show grouped on the Variants page,
        // not as loose rows here. The sales/search picker doesn't set it, so variants stay sellable.
        if (excludeVariants)
            query = query.Where(p => p.VariantGroupId == null);

        if (!string.IsNullOrWhiteSpace(search))
        {
            // Prefix-first matching. Single-letter searches only match the START of the
            // product name — otherwise word-prefix turns "C" into a match for every product
            // containing "Cufflinks" (or "Coral", "Citrine", etc), which buries the actually
            // C-prefixed products under a wall of unrelated rows. For 2+ characters the
            // signal is strong enough that word-prefix is useful (so "Cuff" matches every
            // "Art Deco X Gold Cufflinks").
            var prefix = $"{search}%";
            if (search.Length >= 2)
            {
                var wordPrefix = $"% {search}%";
                query = query.Where(p =>
                    EF.Functions.ILike(p.Name, prefix)
                    || EF.Functions.ILike(p.Name, wordPrefix)
                    || (p.SKU != null && EF.Functions.ILike(p.SKU, prefix))
                    || (p.SKU != null && EF.Functions.ILike(p.SKU, wordPrefix)));
            }
            else
            {
                query = query.Where(p =>
                    EF.Functions.ILike(p.Name, prefix)
                    || (p.SKU != null && EF.Functions.ILike(p.SKU, prefix)));
            }
        }

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(p => p.Category == category);

        // stockLevel filter:
        //   "low"        → at or below the per-product threshold but still some stock
        //   "out"        → zero stock
        //   "sufficient" → above the threshold
        //   (anything else / null) → no filter
        // The threshold lives on the Product itself so the SQL is just an inline comparison.
        var normalized = stockLevel?.Trim().ToLowerInvariant();
        if (normalized == "low")
            query = query.Where(p => p.CurrentStock <= p.LowStockThreshold && p.CurrentStock > 0);
        else if (normalized == "out")
            query = query.Where(p => p.CurrentStock <= 0 && !p.IsBundle);
        else if (normalized == "sufficient")
            query = query.Where(p => p.CurrentStock > p.LowStockThreshold);

        var total = await query.CountAsync();
        var items = await query
            .OrderBy(p => p.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => ToDto(p))
            .ToListAsync();

        return new PaginatedResult<ProductDto>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<ProductDto> GetByIdAsync(Guid businessId, Guid productId)
    {
        var product = await _db.Products
            .FirstOrDefaultAsync(p => p.Id == productId && p.BusinessId == businessId)
            ?? throw new KeyNotFoundException("Product not found.");
        return ToDto(product);
    }

    public async Task<ProductDto> CreateAsync(Guid businessId, CreateProductRequest request, Guid? recordedByUserId = null, string? recordedByName = null, DateTime? createdAtUtc = null)
    {
        // Case-insensitive duplicate check — "Art Deco Anklet" and "art deco anklet" are the
        // same product. Lookup paths (sale-time matching in EntityResolverService) already
        // compare with .ToLower(), so allowing a casing-divergent duplicate to slip through
        // here would create rows that can't be reliably picked at sale-time.
        var exists = await _db.Products.AnyAsync(p =>
            p.BusinessId == businessId && p.Name.ToLower() == request.Name.ToLower() && p.IsActive);
        if (exists)
            throw new InvalidOperationException($"Product '{request.Name}' already exists.");

        // Auto-infer unit if left as default
        var unit = request.Unit;
        if (string.IsNullOrWhiteSpace(unit) || unit == "unit" || unit == "bag")
            unit = Common.UnitInferrer.Infer(request.Name);

        // Auto-infer category if not provided
        var category = request.Category;
        var subcategory = request.Subcategory;
        if (string.IsNullOrWhiteSpace(category))
        {
            var (inferredCat, inferredSub) = Common.CategoryInferrer.Infer(request.Name);
            category = inferredCat;
            subcategory = subcategory ?? inferredSub;
        }

        var effectiveDate = createdAtUtc ?? DateTime.UtcNow;

        var product = new Product
        {
            BusinessId = businessId,
            Name = request.Name,
            SKU = request.SKU,
            Unit = unit,
            CostPrice = request.CostPrice,
            SellingPrice = request.SellingPrice,
            CurrentStock = request.InitialStock,
            LowStockThreshold = request.LowStockThreshold,
            Category = category,
            Subcategory = subcategory,
            Barcode = string.IsNullOrWhiteSpace(request.Barcode) ? null : request.Barcode.Trim(),
            SupplierId = request.SupplierId,
            LeadTimeDays = request.LeadTimeDays,
            RecordedByUserId = recordedByUserId,
            RecordedByName = recordedByName,
            CreatedAtUtc = effectiveDate
        };
        _db.Products.Add(product);

        if (request.InitialStock > 0)
        {
            _db.InventoryTransactions.Add(new InventoryTransaction
            {
                BusinessId = businessId,
                ProductId = product.Id,
                Type = InventoryTransactionType.StockIn,
                Quantity = request.InitialStock,
                Notes = "Initial stock",
                RecordedByUserId = recordedByUserId,
                RecordedByName = recordedByName,
                CreatedAtUtc = effectiveDate
            });
        }

        await _activity.LogAsync(businessId, "product.created", "Product", product.Id, product.Name,
            $"added product “{product.Name}”");

        await _db.SaveChangesAsync();
        return ToDto(product);
    }

    public async Task<ProductDto> UpdateAsync(Guid businessId, Guid productId, UpdateProductRequest request, Guid? recordedByUserId = null, string? recordedByName = null)
    {
        var product = await _db.Products
            .FirstOrDefaultAsync(p => p.Id == productId && p.BusinessId == businessId)
            ?? throw new KeyNotFoundException("Product not found.");

        // Snapshot for the audit diff before we mutate.
        var oldName = product.Name;
        var oldSelling = product.SellingPrice;
        var oldCost = product.CostPrice;
        var oldThreshold = product.LowStockThreshold;
        var wasActive = product.IsActive;

        if (request.Name != null) product.Name = request.Name;
        if (request.SKU != null) product.SKU = string.IsNullOrWhiteSpace(request.SKU) ? null : request.SKU.Trim();
        if (request.Unit != null) product.Unit = request.Unit;
        if (request.CostPrice.HasValue) product.CostPrice = request.CostPrice;
        if (request.SellingPrice.HasValue) product.SellingPrice = request.SellingPrice;
        if (request.LowStockThreshold.HasValue) product.LowStockThreshold = request.LowStockThreshold.Value;
        if (request.Category != null) product.Category = string.IsNullOrWhiteSpace(request.Category) ? null : request.Category.Trim();
        if (request.Subcategory != null) product.Subcategory = string.IsNullOrWhiteSpace(request.Subcategory) ? null : request.Subcategory.Trim();
        if (request.IsActive.HasValue) product.IsActive = request.IsActive.Value;
        if (request.Aliases != null) product.Aliases = request.Aliases.Count > 0 ? System.Text.Json.JsonSerializer.Serialize(request.Aliases) : null;
        if (request.VoiceDescription != null) product.VoiceDescription = string.IsNullOrWhiteSpace(request.VoiceDescription) ? null : request.VoiceDescription.Trim();
        if (request.Barcode != null) product.Barcode = string.IsNullOrWhiteSpace(request.Barcode) ? null : request.Barcode.Trim();
        if (request.SupplierId.HasValue) product.SupplierId = request.SupplierId.Value == Guid.Empty ? null : request.SupplierId;
        if (request.LeadTimeDays.HasValue) product.LeadTimeDays = request.LeadTimeDays.Value;
        if (request.TracksBatches.HasValue) product.TracksBatches = request.TracksBatches.Value;
        if (recordedByUserId.HasValue) { product.RecordedByUserId = recordedByUserId; product.RecordedByName = recordedByName; }

        var changes = new List<string>();
        if (product.Name != oldName) changes.Add($"name “{oldName}” → “{product.Name}”");
        if (product.SellingPrice != oldSelling) changes.Add($"price {oldSelling:0.##} → {product.SellingPrice:0.##}");
        if (product.CostPrice != oldCost) changes.Add($"cost {oldCost:0.##} → {product.CostPrice:0.##}");
        if (product.LowStockThreshold != oldThreshold) changes.Add($"threshold {oldThreshold:0.##} → {product.LowStockThreshold:0.##}");
        if (product.IsActive != wasActive) changes.Add(product.IsActive ? "restored" : "archived");
        var summary = changes.Count > 0
            ? $"edited “{product.Name}”: {string.Join(", ", changes)}"
            : $"edited product “{product.Name}”";
        await _activity.LogAsync(businessId, "product.updated", "Product", product.Id, product.Name, summary);

        await _db.SaveChangesAsync();
        return ToDto(product);
    }

    public async Task<ProductDto?> GetByBarcodeAsync(Guid businessId, string barcode)
    {
        var code = barcode?.Trim();
        if (string.IsNullOrEmpty(code)) return null;
        var product = await _db.Products
            .Where(p => p.BusinessId == businessId && p.IsActive && p.Barcode == code)
            .OrderByDescending(p => p.CreatedAtUtc)
            .FirstOrDefaultAsync();
        return product == null ? null : ToDto(product);
    }

    public async Task<ProductDto> UpdatePriceAsync(Guid businessId, Guid productId, UpdatePriceRequest request)
    {
        var product = await _db.Products
            .FirstOrDefaultAsync(p => p.Id == productId && p.BusinessId == businessId)
            ?? throw new KeyNotFoundException("Product not found.");

        var oldSelling = product.SellingPrice;
        var oldCost = product.CostPrice;
        if (request.SellingPrice.HasValue) product.SellingPrice = request.SellingPrice;
        if (request.CostPrice.HasValue) product.CostPrice = request.CostPrice;

        var parts = new List<string>();
        if (product.SellingPrice != oldSelling) parts.Add($"price {oldSelling:0.##} → {product.SellingPrice:0.##}");
        if (product.CostPrice != oldCost) parts.Add($"cost {oldCost:0.##} → {product.CostPrice:0.##}");
        if (parts.Count > 0)
            await _activity.LogAsync(businessId, "product.price_updated", "Product", product.Id, product.Name,
                $"{product.Name}: {string.Join(", ", parts)}");

        await _db.SaveChangesAsync();
        return ToDto(product);
    }

    public async Task DeleteAsync(Guid businessId, Guid productId)
    {
        var product = await _db.Products
            .FirstOrDefaultAsync(p => p.Id == productId && p.BusinessId == businessId)
            ?? throw new KeyNotFoundException("Product not found.");

        product.IsActive = false;
        await _activity.LogAsync(businessId, "product.deleted", "Product", product.Id, product.Name,
            $"deleted product “{product.Name}”");
        await _db.SaveChangesAsync();
    }

    public async Task<List<ProductDto>> GetLowStockAsync(Guid businessId)
    {
        return await _db.Products
            .Where(p => p.BusinessId == businessId && p.IsActive && !p.IsBundle && p.CurrentStock <= p.LowStockThreshold)
            .OrderBy(p => p.CurrentStock)
            .Select(p => ToDto(p))
            .ToListAsync();
    }

    public async Task<ProductStockStatsDto> GetStockStatsAsync(Guid businessId, string? search, string? category)
    {
        // Bundles aren't stocked; variant members are shown grouped on the Variants page. Exclude both
        // so the chip counts match the inventory list (which also hides variant members).
        var query = _db.Products.Where(p => p.BusinessId == businessId && p.IsActive && !p.IsBundle && p.VariantGroupId == null);
        if (!string.IsNullOrWhiteSpace(search))
        {
            // Must mirror the same prefix-first matching as GetAllAsync so the filter chips
            // and the list view show consistent counts. See GetAllAsync for the rationale on
            // the single-letter carve-out.
            var prefix = $"{search}%";
            if (search.Length >= 2)
            {
                var wordPrefix = $"% {search}%";
                query = query.Where(p =>
                    EF.Functions.ILike(p.Name, prefix)
                    || EF.Functions.ILike(p.Name, wordPrefix)
                    || (p.SKU != null && EF.Functions.ILike(p.SKU, prefix))
                    || (p.SKU != null && EF.Functions.ILike(p.SKU, wordPrefix)));
            }
            else
            {
                query = query.Where(p =>
                    EF.Functions.ILike(p.Name, prefix)
                    || (p.SKU != null && EF.Functions.ILike(p.SKU, prefix)));
            }
        }
        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(p => p.Category == category);

        // One round-trip — count up the three buckets with conditional aggregates so the DB
        // does all the work. Total is the sum of all three.
        var stats = await query
            .GroupBy(p => 1)
            .Select(g => new
            {
                Total = g.Count(),
                OutOfStock = g.Count(p => p.CurrentStock <= 0),
                Low = g.Count(p => p.CurrentStock > 0 && p.CurrentStock <= p.LowStockThreshold),
                Sufficient = g.Count(p => p.CurrentStock > p.LowStockThreshold),
            })
            .FirstOrDefaultAsync();

        return new ProductStockStatsDto
        {
            Total = stats?.Total ?? 0,
            OutOfStock = stats?.OutOfStock ?? 0,
            Low = stats?.Low ?? 0,
            Sufficient = stats?.Sufficient ?? 0,
        };
    }

    private static ProductDto ToDto(Product p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        SKU = p.SKU,
        Unit = p.Unit,
        CostPrice = p.CostPrice,
        SellingPrice = p.SellingPrice,
        CurrentStock = p.CurrentStock,
        LowStockThreshold = p.LowStockThreshold,
        IsLowStock = !p.IsBundle && p.CurrentStock <= p.LowStockThreshold,
        IsActive = p.IsActive,
        Category = p.Category,
        Subcategory = p.Subcategory,
        Source = p.Source,
        RecordedByName = p.RecordedByName,
        Aliases = string.IsNullOrEmpty(p.Aliases) ? null : System.Text.Json.JsonSerializer.Deserialize<List<string>>(p.Aliases),
        VoiceDescription = p.VoiceDescription,
        Barcode = p.Barcode,
        SupplierId = p.SupplierId,
        LeadTimeDays = p.LeadTimeDays,
        IsBundle = p.IsBundle,
        TracksBatches = p.TracksBatches,
        CreatedAtUtc = p.CreatedAtUtc
    };

    public async Task<BundleDto> GetBundleAsync(Guid businessId, Guid productId)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == productId && p.BusinessId == businessId)
            ?? throw new KeyNotFoundException("Product not found.");

        var comps = await _db.BundleComponents
            .Where(c => c.BusinessId == businessId && c.BundleProductId == productId)
            .ToListAsync();

        var names = await _db.Products
            .Where(p => p.BusinessId == businessId && comps.Select(c => c.ComponentProductId).Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => new { p.Name, p.Unit, p.CurrentStock });

        return new BundleDto
        {
            ProductId = product.Id,
            IsBundle = product.IsBundle,
            Components = comps.Select(c => new BundleComponentDto
            {
                ComponentProductId = c.ComponentProductId,
                ComponentName = names.GetValueOrDefault(c.ComponentProductId)?.Name ?? "(deleted)",
                Unit = names.GetValueOrDefault(c.ComponentProductId)?.Unit ?? "unit",
                ComponentStock = names.GetValueOrDefault(c.ComponentProductId)?.CurrentStock ?? 0,
                Quantity = c.Quantity,
            }).ToList(),
        };
    }

    public async Task<BundleDto> SetBundleAsync(Guid businessId, Guid productId, SetBundleRequest request)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == productId && p.BusinessId == businessId)
            ?? throw new KeyNotFoundException("Product not found.");

        // Replace the component set wholesale.
        var existing = await _db.BundleComponents
            .Where(c => c.BusinessId == businessId && c.BundleProductId == productId)
            .ToListAsync();
        _db.BundleComponents.RemoveRange(existing);

        var componentCount = 0;
        if (request.IsBundle)
        {
            var comps = (request.Components ?? new List<SetBundleComponentInput>())
                .Where(c => c.Quantity > 0 && c.ComponentProductId != productId) // no self-reference
                .ToList();
            if (comps.Count == 0)
                throw new InvalidOperationException("A bundle needs at least one component.");

            var validIds = await _db.Products
                .Where(p => p.BusinessId == businessId && comps.Select(c => c.ComponentProductId).Contains(p.Id) && !p.IsBundle)
                .Select(p => p.Id)
                .ToListAsync();
            foreach (var c in comps)
            {
                if (!validIds.Contains(c.ComponentProductId))
                    throw new InvalidOperationException("A component must be an existing, non-bundle product.");
                _db.BundleComponents.Add(new BundleComponent
                {
                    BusinessId = businessId,
                    BundleProductId = productId,
                    ComponentProductId = c.ComponentProductId,
                    Quantity = c.Quantity,
                });
            }
            product.IsBundle = true;
            componentCount = comps.Count;
        }
        else
        {
            product.IsBundle = false;
        }

        var bundleSummary = product.IsBundle
            ? $"set “{product.Name}” as a bundle ({componentCount} components)"
            : $"removed bundle from “{product.Name}”";
        await _activity.LogAsync(businessId, "product.bundle_updated", "Product", product.Id, product.Name, bundleSummary);
        await _db.SaveChangesAsync();
        return await GetBundleAsync(businessId, productId);
    }
}
