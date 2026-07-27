using Ojunai.API.Common;
using Ojunai.API.Data;
using Ojunai.API.Models;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Services;

/// <summary>
/// Helper for the gated multi-location write branches (Phase 2 hardening). The write services call
/// <see cref="SelectedLocationForAsync"/> once; a null result means "no specific location is in play"
/// (single-location, "All locations", bot/background) and the caller takes its existing business-wide
/// path. It returns null WITHOUT a DB query whenever no location was selected, so the single-location hot
/// path pays nothing. Per-location stock reads live here too. See docs/multi-location-spec.md.
/// </summary>
public class LocationStockService
{
    private readonly AppDbContext _db;

    public LocationStockService(AppDbContext db) => _db = db;

    /// <summary>The ambient selected location (X-Location-Id → <see cref="LocationScope"/>) IF the business
    /// is genuinely multi-location (MORE THAN ONE active location) AND the selected location is one of them —
    /// else null. The ">1 active location" gate is CRITICAL: it must match the dual-write mirror exactly (which
    /// only routes per-location deltas when activeCount > 1). If the service took the per-location branch while
    /// the mirror took the single-location branch (or vice-versa), product.CurrentStock and ProductLocationStock
    /// would silently diverge. Returns null without a query when nothing is selected.</summary>
    public async Task<Guid?> SelectedLocationForAsync(Guid businessId)
    {
        if (LocationScope.Current is not { } locId) return null;
        var activeIds = await _db.Locations
            .Where(l => l.BusinessId == businessId && l.IsActive)
            .Select(l => l.Id)
            .ToListAsync();
        return activeIds.Count > 1 && activeIds.Contains(locId) ? locId : null;
    }

    /// <summary>A product's stock at a location (0 when it has no row there).</summary>
    public async Task<decimal> StockAtAsync(Guid productId, Guid locationId)
        => (await _db.ProductLocationStocks
                .FirstOrDefaultAsync(x => x.ProductId == productId && x.LocationId == locationId))
            ?.CurrentStock ?? 0m;

    /// <summary>Per-location stock for many products at once (avoids N+1 in the sale path). Missing = 0.</summary>
    public async Task<Dictionary<Guid, decimal>> StockAtAsync(IReadOnlyCollection<Guid> productIds, Guid locationId)
        => await _db.ProductLocationStocks
            .Where(x => x.LocationId == locationId && productIds.Contains(x.ProductId))
            .ToDictionaryAsync(x => x.ProductId, x => x.CurrentStock);
}
