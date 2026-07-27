using Microsoft.EntityFrameworkCore;
using Ojunai.API.Common;
using Ojunai.API.Data;
using Ojunai.API.Models;
using Ojunai.API.Services.Interfaces;
using Xunit;

namespace Ojunai.Tests.Security;

/// <summary>
/// Tests for the multi-location entitlement gate (PlanGuard.CanUseMultiLocationAsync / GetLocationQuotaAsync):
/// "keep Scale+ included, add-on for lower tiers." Scale/Enterprise (legacy HasMultiBranch or PricingV2
/// catalog) get it; Pro/Professional and below do NOT (unless they hold an active multi_location add-on).
/// EF InMemory — no external DB.
/// </summary>
public class MultiLocationEntitlementTests
{
    private sealed class NoopActivityLogger : IActivityLogger
    {
        public Task LogAsync(Guid businessId, string action, string entityType, Guid? entityId,
            string? entityName, string summary, string? details = null, ActivityActor? actor = null)
            => Task.CompletedTask;
    }

    private static (AppDbContext db, PlanGuard guard) NewGuard()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("mlgate-" + Guid.NewGuid()).Options);
        return (db, new PlanGuard(db, new NoopActivityLogger()));
    }

    private static async Task<Guid> AddBusinessAsync(AppDbContext db, string plan, bool pricingV2 = false)
    {
        var biz = new Business
        {
            Name = "T",
            AccountNumber = "A" + Guid.NewGuid().ToString("N")[..9],
            Plan = plan,
            PricingV2Enabled = pricingV2,
        };
        db.Businesses.Add(biz);
        await db.SaveChangesAsync();
        return biz.Id;
    }

    [Theory]
    [InlineData("starter", false)]
    [InlineData("lite", false)]
    [InlineData("operator", false)]
    [InlineData("pro", false)]    // Pro does NOT get multi-location (kept at Scale+)
    [InlineData("scale", true)]   // legacy HasMultiBranch = Scale only
    public async Task Legacy_MultiLocation_IsScaleOnly(string plan, bool expected)
    {
        var (db, guard) = NewGuard();
        var id = await AddBusinessAsync(db, plan, pricingV2: false);
        Assert.Equal(expected, await guard.CanUseMultiLocationAsync(id));
    }

    [Theory]
    [InlineData("operator", false)]
    [InlineData("professional", false)] // v2 Professional does NOT include multi_location
    [InlineData("scale", true)]
    [InlineData("enterprise", true)]
    public async Task PricingV2_MultiLocation_IsScaleAndEnterprise(string plan, bool expected)
    {
        var (db, guard) = NewGuard();
        var id = await AddBusinessAsync(db, plan, pricingV2: true);
        Assert.Equal(expected, await guard.CanUseMultiLocationAsync(id));
    }

    [Fact]
    public async Task ActiveAddOn_EnablesMultiLocation_OnAnyTier()
    {
        var (db, guard) = NewGuard();
        var id = await AddBusinessAsync(db, "starter", pricingV2: false);
        Assert.False(await guard.CanUseMultiLocationAsync(id)); // starter alone → no

        db.BusinessAddOns.Add(new BusinessAddOn { BusinessId = id, AddOnCode = "addon.multi_location", Status = "active" });
        await db.SaveChangesAsync();
        Assert.True(await guard.CanUseMultiLocationAsync(id));  // + active add-on → yes
    }

    [Fact]
    public async Task CancelledAddOn_DoesNotEnable()
    {
        var (db, guard) = NewGuard();
        var id = await AddBusinessAsync(db, "pro", pricingV2: false);
        db.BusinessAddOns.Add(new BusinessAddOn { BusinessId = id, AddOnCode = "addon.multi_location", Status = "cancelled" });
        await db.SaveChangesAsync();
        Assert.False(await guard.CanUseMultiLocationAsync(id));
    }

    [Fact]
    public async Task UnrelatedActiveAddOn_DoesNotEnable()
    {
        var (db, guard) = NewGuard();
        var id = await AddBusinessAsync(db, "pro", pricingV2: false);
        db.BusinessAddOns.Add(new BusinessAddOn { BusinessId = id, AddOnCode = "addon.branded_pdf", Status = "active" });
        await db.SaveChangesAsync();
        Assert.False(await guard.CanUseMultiLocationAsync(id));
    }

    [Theory]
    [InlineData("pro", false, 1)]     // not entitled → 1 (their default location only)
    [InlineData("scale", false, 10)]  // entitled → the default fixed cap
    public async Task Quota_NonEntitledIsOne_EntitledIsCap(string plan, bool v2, int expected)
    {
        var (db, guard) = NewGuard();
        var id = await AddBusinessAsync(db, plan, pricingV2: v2);
        Assert.Equal(expected, await guard.GetLocationQuotaAsync(id));
    }

    [Fact]
    public async Task Quota_Enterprise_IsUnlimited()
    {
        var (db, guard) = NewGuard();
        var id = await AddBusinessAsync(db, "enterprise", pricingV2: true);
        Assert.Equal(int.MaxValue, await guard.GetLocationQuotaAsync(id));
    }
}
