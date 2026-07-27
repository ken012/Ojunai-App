using Microsoft.EntityFrameworkCore;
using Ojunai.API.Common;
using Ojunai.API.Data;
using Ojunai.API.Models;
using Ojunai.API.Services;
using Xunit;

namespace Ojunai.Tests.Security;

/// <summary>
/// Per-location reports/dashboard. When a location is selected (multi-location business) the money figures
/// scope to that location; "All"/single-location = business-wide, unchanged. Ledger stays business-wide.
/// EF InMemory.
/// </summary>
public class MultiLocationReportsTests
{
    private static AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase("mlrep-" + Guid.NewGuid()).Options);

    private static ReportService Reports(AppDbContext db) => new(db, new LocationStockService(db));

    private static async Task<Business> AddBizAsync(AppDbContext db)
    {
        var biz = new Business { Name = "T", AccountNumber = "A" + Guid.NewGuid().ToString("N")[..9] };
        db.Businesses.Add(biz);
        await db.SaveChangesAsync(); // mirror seeds the default location
        return biz;
    }

    private static async Task<Location> AddLocAsync(AppDbContext db, Guid bizId, string name)
    {
        var l = new Location { BusinessId = bizId, Name = name, IsActive = true };
        db.Locations.Add(l);
        await db.SaveChangesAsync();
        return l;
    }

    private static async Task AddSaleAsync(AppDbContext db, Guid bizId, Guid? locId, decimal amount)
    {
        db.Sales.Add(new Sale
        {
            BusinessId = bizId, LocationId = locId, TotalAmount = amount,
            PaymentStatus = PaymentStatus.Paid, Source = "Manual", CreatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private static async Task AddExpenseAsync(AppDbContext db, Guid bizId, Guid? locId, decimal amount)
    {
        db.Expenses.Add(new Expense
        {
            BusinessId = bizId, LocationId = locId, Amount = amount, Category = "Misc",
            ExpenseType = "operating", Source = "Manual", CreatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    // default 100 sales / 40 expenses, Branch B 50 sales / 10 expenses. Business-wide = 150 / 50.
    private static async Task<(Business biz, Guid defaultLoc, Location branchB)> SeedAsync(AppDbContext db)
    {
        var biz = await AddBizAsync(db);
        var defaultLoc = biz.DefaultLocationId!.Value;
        var branchB = await AddLocAsync(db, biz.Id, "B");
        await AddSaleAsync(db, biz.Id, defaultLoc, 100);
        await AddSaleAsync(db, biz.Id, branchB.Id, 50);
        await AddExpenseAsync(db, biz.Id, defaultLoc, 40);
        await AddExpenseAsync(db, biz.Id, branchB.Id, 10);
        return (biz, defaultLoc, branchB);
    }

    [Fact]
    public async Task Dashboard_MultiLocation_ScopesToSelectedLocation()
    {
        using var db = NewContext();
        var (biz, defaultLoc, branchB) = await SeedAsync(db);

        // No selection → business-wide.
        var all = await Reports(db).GetDashboardOverviewAsync(biz.Id);
        Assert.Equal(150m, all.TodaySales);
        Assert.Equal(150m, all.MonthlySales);
        Assert.Equal(50m, all.TodayExpenses);

        // Branch B → only B's figures.
        try { LocationScope.Current = branchB.Id; var b = await Reports(db).GetDashboardOverviewAsync(biz.Id);
            Assert.Equal(50m, b.TodaySales);
            Assert.Equal(50m, b.MonthlySales);
            Assert.Equal(10m, b.TodayExpenses);
            Assert.Equal(40m, b.MonthlyProfit); // 50 sales - 10 expenses
        }
        finally { LocationScope.Current = null; }

        // Default location → only default's figures.
        try { LocationScope.Current = defaultLoc; var d = await Reports(db).GetDashboardOverviewAsync(biz.Id);
            Assert.Equal(100m, d.TodaySales);
            Assert.Equal(40m, d.TodayExpenses);
        }
        finally { LocationScope.Current = null; }
    }

    [Fact]
    public async Task DailySummary_MultiLocation_ScopesToSelectedLocation()
    {
        using var db = NewContext();
        var (biz, _, branchB) = await SeedAsync(db);

        var all = await Reports(db).GetDailySummaryAsync(biz.Id, null);
        Assert.Equal(150m, all.TotalSales);
        Assert.Equal(50m, all.TotalExpenses);

        try { LocationScope.Current = branchB.Id; var b = await Reports(db).GetDailySummaryAsync(biz.Id, null);
            Assert.Equal(50m, b.TotalSales);
            Assert.Equal(10m, b.TotalExpenses);
            Assert.Equal(40m, b.NetCashIn);
        }
        finally { LocationScope.Current = null; }
    }

    [Fact]
    public async Task Dashboard_SingleLocation_BusinessWide_Unchanged()
    {
        using var db = NewContext();
        var biz = await AddBizAsync(db); // one active location
        var defaultLoc = biz.DefaultLocationId!.Value;
        await AddSaleAsync(db, biz.Id, defaultLoc, 100);
        await AddSaleAsync(db, biz.Id, null, 30); // a legacy/unstamped sale

        // Even with a stale header, single-location resolves to null scope → business-wide (both sales counted).
        try { LocationScope.Current = defaultLoc; var d = await Reports(db).GetDashboardOverviewAsync(biz.Id);
            Assert.Equal(130m, d.TodaySales);
        }
        finally { LocationScope.Current = null; }
    }

    [Fact]
    public async Task MonthlyTrend_MultiLocation_ScopesToSelectedLocation()
    {
        using var db = NewContext();
        var (biz, _, branchB) = await SeedAsync(db);

        var all = await Reports(db).GetMonthlyTrendAsync(biz.Id, 3);
        Assert.Equal(150m, all.Points.Sum(p => p.Revenue));

        try { LocationScope.Current = branchB.Id; var b = await Reports(db).GetMonthlyTrendAsync(biz.Id, 3);
            Assert.Equal(50m, b.Points.Sum(p => p.Revenue));
            Assert.Equal(10m, b.Points.Sum(p => p.Expenses));
        }
        finally { LocationScope.Current = null; }
    }
}
