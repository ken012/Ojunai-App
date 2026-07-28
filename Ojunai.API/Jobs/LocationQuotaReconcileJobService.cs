using Ojunai.API.Common;
using Ojunai.API.Data;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Jobs;

/// <summary>
/// Multi-location downgrade reconciliation. Plan/add-on downgrades happen across several paths (trial revert +
/// each payment provider's cancellation webhook), so instead of wiring deactivation into all of them, this
/// recurring sweep soft-deactivates any locations a business holds BEYOND its current quota (an unentitled
/// business's quota is 1, so it reverts to its default location only). Never deletes; keeps the default + the
/// oldest up to quota. Stock at a deactivated location rolls up into Product.CurrentStock (the business-wide
/// total), which is exactly what the now-single-location business reads — so nothing is lost. Idempotent.
/// </summary>
public class LocationQuotaReconcileJobService
{
    private readonly AppDbContext _db;
    private readonly PlanGuard _planGuard;
    private readonly ILogger<LocationQuotaReconcileJobService> _logger;

    public LocationQuotaReconcileJobService(AppDbContext db, PlanGuard planGuard, ILogger<LocationQuotaReconcileJobService> logger)
    {
        _db = db;
        _planGuard = planGuard;
        _logger = logger;
    }

    public async Task ReconcileAsync()
    {
        try
        {
            // Only businesses with more than one active location can possibly be over quota.
            var bizIds = await _db.Locations
                .Where(l => l.IsActive)
                .GroupBy(l => l.BusinessId)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToListAsync();

            foreach (var bizId in bizIds)
            {
                var quota = await _planGuard.GetLocationQuotaAsync(bizId);
                var active = await _db.Locations
                    .Where(l => l.BusinessId == bizId && l.IsActive)
                    .OrderByDescending(l => l.IsDefault) // default first — always kept
                    .ThenBy(l => l.CreatedAtUtc)          // then keep the oldest
                    .ToListAsync();
                if (active.Count <= quota) continue;

                var deactivated = 0;
                foreach (var extra in active.Skip(quota).Where(l => !l.IsDefault))
                {
                    extra.IsActive = false;
                    deactivated++;
                }
                if (deactivated > 0)
                {
                    await _db.SaveChangesAsync();
                    _logger.LogInformation("Location quota reconcile: deactivated {Count} over-quota location(s) for business {BusinessId} (quota {Quota})",
                        deactivated, bizId, quota);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Location quota reconcile failed");
        }
    }
}
