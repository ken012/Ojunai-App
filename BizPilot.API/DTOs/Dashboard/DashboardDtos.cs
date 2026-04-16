namespace BizPilot.API.DTOs.Dashboard;

public class DashboardOverviewDto
{
    public decimal TodaySales { get; set; }
    public int TodaySaleCount { get; set; }
    public decimal TodayExpenses { get; set; }
    public decimal OutstandingReceivables { get; set; }
    public decimal OutstandingPayables { get; set; }
    public int LowStockCount { get; set; }
    public List<TrendPointDto> SalesTrend { get; set; } = new();
    public List<TrendPointDto> ExpenseTrend { get; set; } = new();
}

public class TrendPointDto
{
    public string Date { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public class RecentActivityDto
{
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class ActivityFeedDto
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty; // sale, expense, inventory, payment_received, payment_made, hold
    public string Description { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
    public string? ContactName { get; set; }
    public string? RecordedBy { get; set; }
    public string? Source { get; set; }
    public string? PaymentStatus { get; set; }
    public string? PaymentMethod { get; set; }
    public string? Details { get; set; } // extra info like item list or category
    public DateTime CreatedAtUtc { get; set; }
}

public class DashboardInsightsDto
{
    public List<TopProductInsightDto> TopProducts { get; set; } = new();
    public List<CategoryBreakdownDto> ExpenseCategories { get; set; } = new();
    public List<PaymentStatusBreakdownDto> PaymentStatus { get; set; } = new();
    public List<AgingBucketDto> ReceivablesAging { get; set; } = new();
    public List<DailyNetDto> DailyNet { get; set; } = new();
    public List<TopCustomerDto> TopCustomers { get; set; } = new();
}

public class TopProductInsightDto
{
    public string ProductName { get; set; } = string.Empty;
    public decimal Revenue { get; set; }
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = string.Empty;
}

public class CategoryBreakdownDto
{
    public string Category { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public class PaymentStatusBreakdownDto
{
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public int Count { get; set; }
}

public class AgingBucketDto
{
    public string Bucket { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public class DailyNetDto
{
    public string Date { get; set; } = string.Empty;
    public decimal Sales { get; set; }
    public decimal Expenses { get; set; }
    public decimal Net { get; set; }
}

public class TopCustomerDto
{
    public string ContactName { get; set; } = string.Empty;
    public decimal Revenue { get; set; }
    public int SaleCount { get; set; }
}
