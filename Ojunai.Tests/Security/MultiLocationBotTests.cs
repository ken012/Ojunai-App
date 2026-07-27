using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Ojunai.API.Common;
using Ojunai.API.Data;
using Ojunai.API.DTOs.Sales;
using Ojunai.API.Models;
using Ojunai.API.Services;
using Xunit;

namespace Ojunai.Tests.Security;

/// <summary>
/// Bot branch switching. The WhatsApp bot sets the ambient LocationScope from the sender's persisted
/// User.SelectedLocationId via LocationAccessService.ResolveEffectiveLocationAsync, then records through
/// SalesService. These tests reproduce that exact composition (WhatsAppService itself is too heavy to
/// construct) and lock the key guarantees: owners attribute to their picked branch; un-selected = unchanged
/// (null); and a restricted staffer is PINNED to their assigned branch even if their selection is foreign.
/// </summary>
public class MultiLocationBotTests
{
    private static AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("mlbot-" + Guid.NewGuid())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    private static async Task<Business> AddBizAsync(AppDbContext db)
    {
        var biz = new Business { Name = "T", AccountNumber = "A" + Guid.NewGuid().ToString("N")[..9] };
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

    private static async Task<User> AddUserAsync(AppDbContext db, Guid bizId, UserRole role, Guid? selected = null)
    {
        var u = new User { BusinessId = bizId, FullName = "U", PhoneNumber = "p" + Guid.NewGuid().ToString("N")[..12], Role = role, SelectedLocationId = selected };
        db.Users.Add(u);
        await db.SaveChangesAsync();
        return u;
    }

    // A product with `total` business-wide stock, all routed to `at` (via the dual-write mirror) so a
    // location-scoped sale there passes the per-location availability check.
    private static async Task<Product> AddProductWithStockAtAsync(AppDbContext db, Guid bizId, Guid at, decimal total)
    {
        var p = new Product { BusinessId = bizId, Name = "W", CurrentStock = 0, IsActive = true };
        db.Products.Add(p);
        await db.SaveChangesAsync();
        try { LocationScope.Current = at; p.CurrentStock += total; await db.SaveChangesAsync(); }
        finally { LocationScope.Current = null; }
        return p;
    }

    // Reproduces the bot's scope-setting + sale.
    private static async Task<Sale> BotSaleAsync(AppDbContext db, User user, Guid productId)
    {
        var access = new LocationAccessService(db);
        var sales = new SalesService(db, new LocationStockService(db));
        try
        {
            LocationScope.Current = await access.ResolveEffectiveLocationAsync(user.BusinessId, user.Id, user.Role, user.SelectedLocationId);
            var req = new CreateSaleRequest { Items = { new SaleItemRequest { ProductId = productId, Quantity = 1, UnitPrice = 10 } } };
            var dto = await sales.CreateAsync(user.BusinessId, req, "WhatsApp", user.Id, user.FullName);
            return (await db.Sales.FindAsync(dto.Id))!;
        }
        finally { LocationScope.Current = null; }
    }

    [Fact]
    public async Task Owner_WithSelectedBranch_BotSale_AttributedToThatBranch()
    {
        using var db = NewContext();
        var biz = await AddBizAsync(db);
        var branchB = await AddLocAsync(db, biz.Id, "B");
        var owner = await AddUserAsync(db, biz.Id, UserRole.Owner, selected: branchB.Id);
        var p = await AddProductWithStockAtAsync(db, biz.Id, branchB.Id, 50);

        var sale = await BotSaleAsync(db, owner, p.Id);
        Assert.Equal(branchB.Id, sale.LocationId);
    }

    [Fact]
    public async Task Owner_NoSelection_BotSale_BusinessWide_Unchanged()
    {
        using var db = NewContext();
        var biz = await AddBizAsync(db);
        await AddLocAsync(db, biz.Id, "B"); // multi-location
        var owner = await AddUserAsync(db, biz.Id, UserRole.Owner, selected: null);
        // Stock at the default so a business-wide sale passes availability.
        var p = await AddProductWithStockAtAsync(db, biz.Id, biz.DefaultLocationId!.Value, 50);

        var sale = await BotSaleAsync(db, owner, p.Id);
        Assert.Null(sale.LocationId); // un-selected → business-wide, exactly as before the feature
    }

    [Fact]
    public async Task RestrictedStaff_PinnedToAssignedBranch_EvenIfSelectionIsForeign()
    {
        // Security: a Sales staffer assigned ONLY to Branch B, whose SelectedLocationId points at Branch C
        // (not theirs), must have their bot sale pinned to B — they can't attribute to a branch they can't access.
        using var db = NewContext();
        var biz = await AddBizAsync(db);
        var branchB = await AddLocAsync(db, biz.Id, "B");
        var branchC = await AddLocAsync(db, biz.Id, "C");
        var staff = await AddUserAsync(db, biz.Id, UserRole.Sales, selected: branchC.Id);
        db.UserLocations.Add(new UserLocation { UserId = staff.Id, LocationId = branchB.Id });
        await db.SaveChangesAsync();
        var p = await AddProductWithStockAtAsync(db, biz.Id, branchB.Id, 50);

        var sale = await BotSaleAsync(db, staff, p.Id);
        Assert.Equal(branchB.Id, sale.LocationId); // pinned to their assigned B, NOT the foreign C
    }
}
