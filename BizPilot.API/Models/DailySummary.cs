namespace BizPilot.API.Models;

public class DailySummary
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BusinessId { get; set; }
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
