using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Ojunai.API.Common;
using Ojunai.API.Data;
using Ojunai.API.Jobs;
using Ojunai.API.Models;
using Ojunai.API.Services.Interfaces;
using Xunit;

namespace Ojunai.Tests.Security;

/// <summary>
/// Downgrade reconciliation: the recurring LocationQuotaReconcileJobService soft-deactivates locations a
/// business holds beyond its current quota (an unentitled business's quota is 1), keeping the default. Never
/// deletes; within-quota businesses are untouched. EF InMemory.
/// </summary>
public class MultiLocationDowngradeTests
{
    private sealed class NoopActivityLogger : IActivityLogger
    {
        public Task LogAsync(Guid businessId, string action, string entityType, Guid? entityId,
            string? entityName, string summary, string? details = null, ActivityActor? actor = null)
            => Task.CompletedTask;
    }

    private static AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase("mldown-" + Guid.NewGuid()).Options);

    private static LocationQuotaReconcileJobService Job(AppDbContext db) =>
        new(db, new PlanGuard(db, new NoopActivityLogger()), NullLogger<LocationQuotaReconcileJobService>.Instance);

    private static async Task<Business> AddBizAsync(AppDbContext db, string plan)
    {
        var biz = new Business { Name = "T", AccountNumber = "A" + Guid.NewGuid().ToString("N")[..9], Plan = plan };
        db.Businesses.Add(biz);
        await db.SaveChangesAsync(); // mirror seeds the default location
        return biz;
    }

    private static async Task AddLocAsync(AppDbContext db, Guid bizId, string name)
    {
        db.Locations.Add(new Location { BusinessId = bizId, Name = name, IsActive = true });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Reconcile_UnentitledOverQuota_DeactivatesExtras_KeepsDefault()
    {
        using var db = NewContext();
        var biz = await AddBizAsync(db, "starter"); // not entitled → quota 1
        var main = biz.DefaultLocationId!.Value;
        await AddLocAsync(db, biz.Id, "B");
        await AddLocAsync(db, biz.Id, "C");
        Assert.Equal(3, await db.Locations.CountAsync(l => l.BusinessId == biz.Id && l.IsActive));

        await Job(db).ReconcileAsync();

        var active = await db.Locations.Where(l => l.BusinessId == biz.Id && l.IsActive).ToListAsync();
        Assert.Single(active);
        Assert.Equal(main, active[0].Id);           // only the default remains active
        Assert.Equal(3, await db.Locations.CountAsync(l => l.BusinessId == biz.Id)); // none deleted
    }

    [Fact]
    public async Task Reconcile_EntitledWithinQuota_NoChange()
    {
        using var db = NewContext();
        var biz = await AddBizAsync(db, "scale"); // entitled → quota well above 3
        await AddLocAsync(db, biz.Id, "B");
        await AddLocAsync(db, biz.Id, "C");

        await Job(db).ReconcileAsync();

        Assert.Equal(3, await db.Locations.CountAsync(l => l.BusinessId == biz.Id && l.IsActive));
    }

    [Fact]
    public async Task Reconcile_SingleLocation_NoChange()
    {
        using var db = NewContext();
        var biz = await AddBizAsync(db, "starter");

        await Job(db).ReconcileAsync();

        Assert.Single(await db.Locations.Where(l => l.BusinessId == biz.Id && l.IsActive).ToListAsync());
    }
}
