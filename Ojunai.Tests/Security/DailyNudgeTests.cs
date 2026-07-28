using Ojunai.API.DTOs.Reports;
using Ojunai.API.Jobs;
using Xunit;

namespace Ojunai.Tests.Security;

/// <summary>
/// Content rules for the opt-in morning nudge (DailyNudgeJobService.FormatNudge). The scheduling/gating
/// (local-hour, once-per-day, opt-in, channel, WhatsApp pack gate) is straightforward guard code that
/// mirrors SummaryJobService; the value here is the message content: soonest stockouts first with a reorder
/// quantity, the single most-overdue debtor, and — critically — SILENCE when nothing is actionable so the
/// nudge never becomes noise.
/// </summary>
public class DailyNudgeTests
{
    private static StockoutPredictionDto Stockout(string name, decimal daysLeft, decimal restock = 0, string unit = "unit")
        => new() { ProductName = name, DaysLeft = daysLeft, RestockQty = restock, Unit = unit };

    private static OutstandingDebtSummaryDto Debt(params (string name, decimal amount, int daysOld)[] receivables)
        => new()
        {
            TopReceivables = receivables
                .Select(r => new OutstandingContactDto { ContactName = r.name, Amount = r.amount, DaysOld = r.daysOld })
                .ToList()
        };

    [Fact]
    public void NothingActionable_ReturnsNull()
    {
        Assert.Null(DailyNudgeJobService.FormatNudge("₦", new List<StockoutPredictionDto>(), Debt()));
    }

    [Fact]
    public void Stockout_And_Debt_ProduceActionableLines()
    {
        var msg = DailyNudgeJobService.FormatNudge(
            "₦",
            new List<StockoutPredictionDto> { Stockout("Rice", 2, restock: 40, unit: "bag") },
            Debt(("Ama", 12000, 30)));

        Assert.NotNull(msg);
        Assert.Contains("Rice", msg);
        Assert.Contains("~2 days of stock left", msg);
        Assert.Contains("reorder 40 bags", msg);
        Assert.Contains("Ama", msg);
        Assert.Contains("₦12,000", msg);
        Assert.Contains("30 days overdue", msg);
    }

    [Fact]
    public void ZeroDaysLeft_ReadsAsOutNow()
    {
        var msg = DailyNudgeJobService.FormatNudge("₦", new List<StockoutPredictionDto> { Stockout("Beans", 0) }, Debt());
        Assert.Contains("out of stock now", msg);
    }

    [Fact]
    public void Stockouts_BeyondHorizon_AreExcluded()
    {
        // 20 days out is not "act now" — should be filtered; with no debt either, the nudge is silent.
        var msg = DailyNudgeJobService.FormatNudge("₦", new List<StockoutPredictionDto> { Stockout("Sugar", 20, restock: 5) }, Debt());
        Assert.Null(msg);
    }

    [Fact]
    public void Stockouts_SortedSoonestFirst_AndCappedAtThree()
    {
        var msg = DailyNudgeJobService.FormatNudge(
            "₦",
            new List<StockoutPredictionDto>
            {
                Stockout("D", 9), Stockout("A", 1), Stockout("C", 5), Stockout("B", 3),
            },
            Debt());

        Assert.NotNull(msg);
        // Only the three soonest (A=1, B=3, C=5) appear; D=9 is dropped.
        Assert.Contains("*A*", msg);
        Assert.Contains("*B*", msg);
        Assert.Contains("*C*", msg);
        Assert.DoesNotContain("*D*", msg);
        // Soonest first.
        Assert.True(msg.IndexOf("*A*", StringComparison.Ordinal) < msg.IndexOf("*B*", StringComparison.Ordinal));
        Assert.True(msg.IndexOf("*B*", StringComparison.Ordinal) < msg.IndexOf("*C*", StringComparison.Ordinal));
    }

    [Fact]
    public void Debt_OnlyCountsSevenDaysPlus_AndPicksMostOverdue()
    {
        // 3-day debt is too fresh to nag about; 45-day is the one to surface.
        var msg = DailyNudgeJobService.FormatNudge(
            "₦",
            new List<StockoutPredictionDto>(),
            Debt(("Fresh", 5000, 3), ("Stale", 8000, 45)));

        Assert.NotNull(msg);
        Assert.Contains("Stale", msg);
        Assert.Contains("45 days overdue", msg);
        Assert.DoesNotContain("Fresh", msg);
    }

    [Fact]
    public void Stockout_WithoutRestockQty_OmitsReorderClause()
    {
        var msg = DailyNudgeJobService.FormatNudge("₦", new List<StockoutPredictionDto> { Stockout("Salt", 4, restock: 0) }, Debt());
        Assert.Contains("Salt", msg);
        Assert.DoesNotContain("reorder", msg);
    }
}
