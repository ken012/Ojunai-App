using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Ojunai.API.Common;
using Ojunai.API.Data;
using Ojunai.API.Models;
using Ojunai.API.Services;
using Ojunai.API.Services.Interfaces;
using Xunit;

namespace Ojunai.Tests.Security;

/// <summary>
/// Per-branch stock breakdown on the inventory list (ProductService.GetAllAsync → ProductDto.StockByLocation):
/// multi-location businesses see where each product's stock sits across branches; single-location stays clean
/// (null, no breakdown, no extra payload). This is the "how can I tell which inventory is for what location"
/// view — independent of the selected-location filter.
/// </summary>
public class MultiLocationInventoryBreakdownTests
{
    private sealed class NoopActivityLogger : IActivityLogger
    {
        public Task LogAsync(Guid businessId, string action, string entityType, Guid? entityId,
            string? entityName, string summary, string? details = null, ActivityActor? actor = null)
            => Task.CompletedTask;
    }

    private static AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("mlinv-" + Guid.NewGuid())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    private static ProductService Products(AppDbContext db) =>
        new(db, new NoopActivityLogger(), new LocationStockService(db));

    private static async Task<Business> AddBizAsync(AppDbContext db)
    {
        var biz = new Business { Name = "T", AccountNumber = "A" + Guid.NewGuid().ToString("N")[..9], Plan = "scale" };
        db.Businesses.Add(biz);
        await db.SaveChangesAsync(); // auto-creates the default "Main" location + (via mirror) primes products later
        return biz;
    }

    private static async Task<Location> AddLocAsync(AppDbContext db, Guid bizId, string name)
    {
        var l = new Location { BusinessId = bizId, Name = name, IsActive = true };
        db.Locations.Add(l);
        await db.SaveChangesAsync();
        return l;
    }

    private static async Task<Product> AddProductAsync(AppDbContext db, Guid bizId, string name, decimal stock)
    {
        var p = new Product { BusinessId = bizId, Name = name, CurrentStock = stock, IsActive = true };
        db.Products.Add(p);
        await db.SaveChangesAsync(); // mirror creates a default-location PLS row = CurrentStock
        return p;
    }

    [Fact]
    public async Task GetAll_MultiLocation_BreaksStockDownByBranch_DefaultFirst()
    {
        await using var db = NewContext();
        var biz = await AddBizAsync(db);
        var mainId = biz.DefaultLocationId!.Value;
        var ikeja = await AddLocAsync(db, biz.Id, "Ikeja");
        await AddProductAsync(db, biz.Id, "Rice", 50); // lands at Main; Ikeja has none

        var page = await Products(db).GetAllAsync(biz.Id, 1, 50, null);
        var rice = page.Items.Single(i => i.Name == "Rice");

        Assert.NotNull(rice.StockByLocation);
        Assert.Equal(2, rice.StockByLocation!.Count);
        Assert.True(rice.StockByLocation[0].IsDefault); // default branch first
        var main = rice.StockByLocation.Single(l => l.LocationId == mainId);
        var ik = rice.StockByLocation.Single(l => l.LocationId == ikeja.Id);
        Assert.Equal(50, main.Stock);
        Assert.Equal(0, ik.Stock); // nothing at Ikeja yet
        Assert.Equal("Ikeja", ik.LocationName);
        // Sum of the branch breakdown equals the business-wide stock (no drift).
        Assert.Equal(rice.CurrentStock, rice.StockByLocation.Sum(l => l.Stock));
    }

    [Fact]
    public async Task GetAll_SingleLocation_NoBreakdown()
    {
        await using var db = NewContext();
        var biz = await AddBizAsync(db); // only the default location
        await AddProductAsync(db, biz.Id, "Rice", 50);

        var page = await Products(db).GetAllAsync(biz.Id, 1, 50, null);

        Assert.Null(page.Items.Single().StockByLocation); // single-location → clean, no breakdown
    }
}
