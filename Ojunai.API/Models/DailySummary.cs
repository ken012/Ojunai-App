namespace Ojunai.API.Models;

public class DailySummary
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BusinessId { get; set; }
    /// <summary>Location this summary is for (multi-location; null = default/legacy/business-wide). Additive Phase 0;
    /// the (BusinessId, Date) unique key is intentionally unchanged until per-location summaries are read.</summary>
    public Guid? LocationId { get; set; }
    public DateOnly Date { get; set; }
    public decimal TotalSales { get; set; }
    public decimal TotalExpenses { get; set; }
    public decimal NetCashIn { get; set; }
    public decimal OutstandingReceivables { get; set; }
    public decimal OutstandingPayables { get; set; }
    public int LowStockCount { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Business Business { get; set; } = null!;
}
