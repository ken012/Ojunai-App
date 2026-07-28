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
    private readonly LocationStockService _locStock;

    public ProductService(AppDbContext db, IActivityLogger activity, LocationStockService locStock)
    {
        _db = db;
        _activity = activity;
        _locStock = locStock;
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

        await OverlayLocationStockAsync(businessId, items);
        await AttachStockByLocationAsync(businessId, items);

        return new PaginatedResult<ProductDto>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }

    /// <summary>
    /// Attaches a per-branch stock breakdown to each product for MULTI-location businesses so the inventory
    /// list can show "where is this stock" at a glance, independent of the selected-location filter. Single-
    /// location businesses (one active location) get null — no breakdown, no wasted payload. One small
    /// Locations query + one ProductLocationStocks query for the page's products (0 where a branch has no row).
    /// </summary>
    private async Task AttachStockByLocationAsync(Guid businessId, List<ProductDto> items)
    {
        if (items.Count == 0) return;

        var locations = await _db.Locations
            .Where(l => l.BusinessId == businessId && l.IsActive)
            .OrderByDescending(l => l.IsDefault).ThenBy(l => l.CreatedAtUtc)
            .Select(l => new { l.Id, l.Name, l.IsDefault })
            .ToListAsync();
        if (locations.Count <= 1) return; // single-location → nothing to break down

        var ids = items.Where(i => !i.IsBundle).Select(i => i.Id).ToList();
        var byKey = (await _db.ProductLocationStocks
                .Where(x => x.BusinessId == businessId && ids.Contains(x.ProductId))
                .Select(x => new { x.ProductId, x.LocationId, x.CurrentStock })
                .ToListAsync())
            .ToDictionary(x => (x.ProductId, x.LocationId), x => x.CurrentStock);

        foreach (var item in items)
        {
            if (item.IsBundle) continue; // bundles hold no stock of their own
            item.StockByLocation = locations
                .Select(l => new LocationStockDto
                {
                    LocationId = l.Id,
                    LocationName = l.Name,
                    IsDefault = l.IsDefault,
                    Stock = byKey.GetValueOrDefault((item.Id, l.Id), 0m),
                })
                .ToList();
        }
    }

    /// <summary>
    /// Per-location read overlay (multi-location Phase 2b). When a specific location is selected for the
    /// request (X-Location-Id → <see cref="LocationScope"/>) AND the business has more than one active
    /// location, replace each product's business-wide CurrentStock with that location's stock (0 when the
    /// product has no row there). Absent/invalid selection ⇒ business-wide, unchanged — so single-location
    /// businesses (which never send the header) run ZERO extra queries. Uses the SAME gate
    /// (<see cref="LocationStockService.SelectedLocationForAsync"/>) the write mirror uses, so reads and
    /// writes can never disagree about whether a request is location-scoped.
    /// </summary>
    private async Task OverlayLocationStockAsync(Guid businessId, List<ProductDto> items)
    {
        if (items.Count == 0) return;
        if (await _locStock.SelectedLocationForAsync(businessId) is not { } locId) return;

        var ids = items.Select(i => i.Id).ToList();
        var stockByProduct = await _db.ProductLocationStocks
            .Where(x => x.LocationId == locId && ids.Contains(x.ProductId))
            .ToDictionaryAsync(x => x.ProductId, x => x.CurrentStock);
        foreach (var item in items)
        {
            item.CurrentStock = stockByProduct.GetValueOrDefault(item.Id, 0m);
            item.IsLowStock = !item.IsBundle && item.CurrentStock <= item.LowStockThreshold;
        }
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
        if (await _locStock.SelectedLocationForAsync(businessId) is { } locId)
        {
            // Per-location low-stock: compare each product's stock AT THIS LOCATION (0 when it has no PLS row
            // there) against its threshold. A product can be low at one branch while fine business-wide, so we
            // cannot pre-filter on the business-wide CurrentStock — LEFT JOIN keeps rows with no location stock.
            // NOTE: uses the product's business-wide LowStockThreshold (not the per-location PLS override, which
            // has no UI yet and is deferred to Phase 3) so low-stock, stats chips and the list stay consistent.
            var rows = await (
                from p in _db.Products
                where p.BusinessId == businessId && p.IsActive && !p.IsBundle
                join s in _db.ProductLocationStocks.Where(x => x.LocationId == locId)
                    on p.Id equals s.ProductId into gj
                from s in gj.DefaultIfEmpty()
                where (s == null ? 0m : s.CurrentStock) <= p.LowStockThreshold
                orderby (s == null ? 0m : s.CurrentStock)
                select new { Product = p, LocStock = s == null ? 0m : s.CurrentStock })
                .ToListAsync();

            return rows.Select(r =>
            {
                var dto = ToDto(r.Product);
                dto.CurrentStock = r.LocStock;
                dto.IsLowStock = !dto.IsBundle && r.LocStock <= dto.LowStockThreshold;
                return dto;
            }).ToList();
        }

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

        if (await _locStock.SelectedLocationForAsync(businessId) is { } locId)
        {
            // Per-location chip counts: bucket each product by its stock AT THIS LOCATION (0 when it has no
            // PLS row there) instead of the business-wide CurrentStock, so the chips match the overlaid list.
            // Threshold stays the product's business-wide LowStockThreshold (per-location override deferred, see GetLowStockAsync).
            var locStats = await (
                from p in query
                join s in _db.ProductLocationStocks.Where(x => x.LocationId == locId)
                    on p.Id equals s.ProductId into gj
                from s in gj.DefaultIfEmpty()
                select new { p.LowStockThreshold, Stock = s == null ? 0m : s.CurrentStock })
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Total = g.Count(),
                    OutOfStock = g.Count(x => x.Stock <= 0),
                    Low = g.Count(x => x.Stock > 0 && x.Stock <= x.LowStockThreshold),
                    Sufficient = g.Count(x => x.Stock > x.LowStockThreshold),
                })
                .FirstOrDefaultAsync();

            return new ProductStockStatsDto
            {
                Total = locStats?.Total ?? 0,
                OutOfStock = locStats?.OutOfStock ?? 0,
                Low = locStats?.Low ?? 0,
                Sufficient = locStats?.Sufficient ?? 0,
            };
        }

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
