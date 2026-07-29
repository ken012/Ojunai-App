using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Ojunai.API.Common;
using Ojunai.API.Data;
using Ojunai.API.Models;
using Ojunai.API.Services;
using Xunit;

namespace Ojunai.Tests.Security;

/// <summary>
/// Activity feed is location-aware: audit "action" rows now carry a LocationId (stamped from the ambient
/// LocationScope at write time) and the feed both FILTERS actions to the selected branch and LABELS every row
/// with its branch name — for multi-location businesses only. Single-location stays label-free + unfiltered.
/// </summary>
public class MultiLocationActivityTests
{
    private static AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("mlact-" + Guid.NewGuid())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    private static ReportService Reports(AppDbContext db) => new(db, new LocationStockService(db));

    private static async Task<Business> AddBizAsync(AppDbContext db)
    {
        var biz = new Business { Name = "T", AccountNumber = "A" + Guid.NewGuid().ToString("N")[..9], Plan = "scale" };
        db.Businesses.Add(biz);
        await db.SaveChangesAsync();
        return biz;
    }

    private static async Task<Location> AddLocAsync(AppDbContext db, Guid bizId, string name)
    {
        var l = new Location { BusinessId = bizId, Name = name, IsActive = true };
        db.Locations.Add(l);
        await db.SaveChangesAsync();
        return l;
    }

    private static async Task AddActionAsync(AppDbContext db, Guid bizId, string summary, Guid? at)
    {
        db.ActivityLogEntries.Add(new ActivityLogEntry
        {
            BusinessId = bizId, Action = "product.updated", EntityType = "Product",
            Summary = summary, ActorName = "Owner", ActorChannel = "dashboard", LocationId = at,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Actions_FilterAndLabel_ByBranch()
    {
        await using var db = NewContext();
        var biz = await AddBizAsync(db);
        var mainId = biz.DefaultLocationId!.Value;
        var ikeja = await AddLocAsync(db, biz.Id, "Ikeja");
        await AddActionAsync(db, biz.Id, "edited Rice at Main", at: mainId);
        await AddActionAsync(db, biz.Id, "edited Beans at Ikeja", at: ikeja.Id);

        // Viewing Ikeja → only the Ikeja action, labeled "Ikeja".
        using (LocationScope.Push(ikeja.Id))
        {
            var feed = await Reports(db).GetActivityFeedAsync(biz.Id, "action", 1, 50, null, null, null);
            var actions = feed.Items.Where(i => i.Type == "action").ToList();
            Assert.Single(actions);
            Assert.Contains("Ikeja", actions[0].Description);
            Assert.Equal("Ikeja", actions[0].LocationName);
        }

        // "All locations" → both actions, each labeled with its branch.
        var all = (await Reports(db).GetActivityFeedAsync(biz.Id, "action", 1, 50, null, null, null))
            .Items.Where(i => i.Type == "action").ToList();
        Assert.Equal(2, all.Count);
        Assert.Contains(all, a => a.LocationName == "Ikeja");
        Assert.Contains(all, a => a.LocationName == "Main");
    }

    [Fact]
    public async Task SingleLocation_NoLabel_NoFilter()
    {
        await using var db = NewContext();
        var biz = await AddBizAsync(db); // only the default location
        await AddActionAsync(db, biz.Id, "edited Rice", at: biz.DefaultLocationId);

        // Even under a pushed scope, a single-location business resolves locId to null → unfiltered + unlabeled.
        using (LocationScope.Push(biz.DefaultLocationId))
        {
            var feed = await Reports(db).GetActivityFeedAsync(biz.Id, "action", 1, 50, null, null, null);
            var actions = feed.Items.Where(i => i.Type == "action").ToList();
            Assert.Single(actions);
            Assert.Null(actions[0].LocationName); // single-location → no branch chip
        }
    }
}
