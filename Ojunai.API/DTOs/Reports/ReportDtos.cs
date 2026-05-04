using Ojunai.API.DTOs.Products;
using Ojunai.API.DTOs.Ledger;

namespace Ojunai.API.DTOs.Reports;

public class DailySummaryDto
{
    public string Date { get; set; } = string.Empty;
    public decimal TotalSales { get; set; }
    public int SaleCount { get; set; }
    public decimal TotalExpenses { get; set; }
    public decimal NetCashIn { get; set; }
    public decimal OutstandingReceivables { get; set; }
    public decimal OutstandingPayables { get; set; }
    public int LowStockCount { get; set; }
    public List<ProductDto> LowStockItems { get; set; } = new();
}

public class WeeklySummaryDto
{
    public string WeekStart { get; set; } = string.Empty;
    public string WeekEnd { get; set; } = string.Empty;
    public decimal TotalSales { get; set; }
    public decimal TotalExpenses { get; set; }
    public decimal EstimatedProfit { get; set; }
    public bool IsProfitEstimate { get; set; }
    public List<TopProductDto> TopProducts { get; set; } = new();
    public List<ProductDto> LowStockItems { get; set; } = new();
    public List<OutstandingBalanceDto> TopDebtors { get; set; } = new();
    public List<OutstandingBalanceDto> TopSupplierBalances { get; set; } = new();
}

public class TopProductDto
{
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal TotalQuantitySold { get; set; }
    public decimal TotalRevenue { get; set; }
}

public class CashPositionDto
{
    public decimal TotalSalesThisMonth { get; set; }
    public decimal TotalExpensesThisMonth { get; set; }
    public decimal EstimatedCashIn { get; set; }
    public decimal OutstandingReceivables { get; set; }
    public decimal OutstandingPayables { get; set; }
    public decimal NetPosition { get; set; }
    public bool IsEstimate { get; set; } = true;
}

public class DeadStockItemDto
{
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal CurrentStock { get; set; }
    public int DaysSinceLastSale { get; set; }
}

public class StockoutPredictionDto
{
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal CurrentStock { get; set; }
    public decimal DailyRate { get; set; }
    public decimal DaysLeft { get; set; }
    public decimal RestockQty { get; set; }
    public string Urgency { get; set; } = string.Empty;
}

public class ProductProfitDto
{
    public string ProductName { get; set; } = string.Empty;
    public decimal Revenue { get; set; }
    public decimal Cost { get; set; }
    public decimal Profit { get; set; }
    public decimal Margin { get; set; }
}

public class StaffSalesDto
{
    public string StaffName { get; set; } = string.Empty;
    public decimal TotalRevenue { get; set; }
    public int SaleCount { get; set; }
    public List<StaffSaleItemDto> Items { get; set; } = new();
}

public class StaffSaleItemDto
{
    public string ProductName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal Revenue { get; set; }
}

// ────────────────────────────────────────────────────────────────────────
// Advanced reports (all behind the advanced_reports plan feature)
// ────────────────────────────────────────────────────────────────────────

public class AgingContactDto
{
    public string ContactName { get; set; } = string.Empty;
    public Guid ContactId { get; set; }
    public decimal Bucket0To30 { get; set; }
    public decimal Bucket31To60 { get; set; }
    public decimal Bucket61To90 { get; set; }
    public decimal Bucket90Plus { get; set; }
    public decimal Total { get; set; }
    public int OldestDays { get; set; }
}

public class AgingReportDto
{
    public decimal Total0To30 { get; set; }
    public decimal Total31To60 { get; set; }
    public decimal Total61To90 { get; set; }
    public decimal Total90Plus { get; set; }
    public decimal GrandTotal { get; set; }
    public List<AgingContactDto> Contacts { get; set; } = new();
}

public class MonthlyPnlDto
{
    public string Month { get; set; } = string.Empty;
    public string PreviousMonth { get; set; } = string.Empty;

    public decimal Revenue { get; set; }
    public decimal PreviousRevenue { get; set; }

    public decimal CostOfGoodsSold { get; set; }
    public decimal PreviousCostOfGoodsSold { get; set; }

    public decimal GrossProfit { get; set; }
    public decimal PreviousGrossProfit { get; set; }

    public decimal OperatingExpenses { get; set; }
    public decimal PreviousOperatingExpenses { get; set; }

    public decimal NetProfit { get; set; }
    public decimal PreviousNetProfit { get; set; }

    public decimal GrossMarginPercent { get; set; }
    public decimal NetMarginPercent { get; set; }
    public bool IsEstimate { get; set; }
}

public class ExpenseBreakdownDto
{
    public string Month { get; set; } = string.Empty;
    public decimal TotalExpenses { get; set; }
    public List<ExpenseCategoryDto> Categories { get; set; } = new();
}

public class ExpenseCategoryDto
{
    public string Category { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal PercentOfTotal { get; set; }
    public int EntryCount { get; set; }
}

public class InventoryTurnoverDto
{
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal CurrentStock { get; set; }
    public decimal SoldLast30Days { get; set; }
    public decimal DailyVelocity { get; set; }
    public decimal DaysOfStockRemaining { get; set; }
    public decimal CostOfGoodsSold { get; set; }
    public decimal InventoryValue { get; set; }
    public decimal TurnoverRatio { get; set; }
    public string Classification { get; set; } = "Slow"; // Fast | Healthy | Slow | Dead
}

public class TopCustomerDetailDto
{
    public Guid ContactId { get; set; }
    public string ContactName { get; set; } = string.Empty;
    public decimal TotalRevenue { get; set; }
    public int TransactionCount { get; set; }
    public decimal PercentOfTotal { get; set; }
    public DateTime? LastPurchaseAtUtc { get; set; }
}

public class TopCustomersReportDto
{
    public decimal TotalRevenue { get; set; }
    public decimal TopCustomerPercent { get; set; }
    public bool ConcentrationRisk { get; set; }
    public List<TopCustomerDetailDto> Customers { get; set; } = new();
}

public class SalesHeatmapCellDto
{
    public int DayOfWeek { get; set; } // 0 = Sunday
    public int Hour { get; set; } // 0-23
    public decimal Revenue { get; set; }
    public int SaleCount { get; set; }
}

public class SalesHeatmapDto
{
    public int WeeksAnalyzed { get; set; }
    public decimal PeakRevenue { get; set; }
    public int PeakDayOfWeek { get; set; }
    public int PeakHour { get; set; }
    public List<SalesHeatmapCellDto> Cells { get; set; } = new();
}

public class MonthlyTrendPointDto
{
    public string Month { get; set; } = string.Empty;
    public decimal Revenue { get; set; }
    public decimal Expenses { get; set; }
    public decimal Profit { get; set; }
    public int TransactionCount { get; set; }
}

public class MonthlyTrendDto
{
    public List<MonthlyTrendPointDto> Points { get; set; } = new();
}

public class PaymentMethodMonthDto
{
    public string Month { get; set; } = string.Empty;
    public decimal Cash { get; set; }
    public decimal Transfer { get; set; }
    public decimal Pos { get; set; }
    public decimal Credit { get; set; }
    public decimal Other { get; set; }
}

public class PaymentMethodSplitDto
{
    public List<PaymentMethodMonthDto> Months { get; set; } = new();
    public decimal TotalCash { get; set; }
    public decimal TotalTransfer { get; set; }
    public decimal TotalPos { get; set; }
    public decimal TotalCredit { get; set; }
    public decimal TotalOther { get; set; }
}

public class CustomerReliabilityDto
{
    public Guid ContactId { get; set; }
    public string ContactName { get; set; } = string.Empty;
    public int PaidReceivables { get; set; }
    public decimal AverageDaysToPay { get; set; }
    public decimal TotalPaid { get; set; }
    public string Classification { get; set; } = "Unknown"; // Prompt | Regular | Slow | Late
}

public class WastageReportDto
{
    public string Period { get; set; } = string.Empty;
    public decimal TotalValue { get; set; }
    public int EventCount { get; set; }
    public List<WastageItemDto> TopProducts { get; set; } = new();
}

public class WastageItemDto
{
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal QuantityDamaged { get; set; }
    public decimal EstimatedLoss { get; set; }
    public int EventCount { get; set; }
}

public class AvgTransactionPointDto
{
    public string Month { get; set; } = string.Empty;
    public decimal AverageValue { get; set; }
    public int TransactionCount { get; set; }
}

public class AvgTransactionValueDto
{
    public List<AvgTransactionPointDto> Points { get; set; } = new();
}

public class RetentionMonthDto
{
    public string Month { get; set; } = string.Empty;
    public int NewCustomers { get; set; }
    public int ReturningCustomers { get; set; }
    public decimal NewRevenue { get; set; }
    public decimal ReturningRevenue { get; set; }
}

public class CustomerRetentionDto
{
    public List<RetentionMonthDto> Months { get; set; } = new();
}

public class ReorderSuggestionDto
{
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal CurrentStock { get; set; }
    public decimal DailyVelocity { get; set; }
    public decimal SuggestedReorderQty { get; set; }
    public decimal EstimatedCost { get; set; }
    public string Urgency { get; set; } = "Normal"; // Critical | High | Normal
}

public class ProductAffinityDto
{
    public string ProductA { get; set; } = string.Empty;
    public string ProductB { get; set; } = string.Empty;
    public int CoOccurrenceCount { get; set; }
    public decimal CombinedRevenue { get; set; }
}

public class WeeklySalesPointDto
{
    public string WeekStart { get; set; } = string.Empty;
    public string WeekEnd { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public decimal Revenue { get; set; }
    public int SaleCount { get; set; }
    public decimal AvgOrderValue { get; set; }
    public decimal? GrowthPercent { get; set; }
    public decimal MovingAvg { get; set; }
}

public class WeeklySalesTrendDto
{
    public List<WeeklySalesPointDto> Weeks { get; set; } = new();
    public decimal AvgWeeklyRevenue { get; set; }
    public decimal BestWeekRevenue { get; set; }
    public string BestWeekLabel { get; set; } = string.Empty;
    public decimal WorstWeekRevenue { get; set; }
    public string WorstWeekLabel { get; set; } = string.Empty;
    public decimal AvgGrowthPercent { get; set; }
    public int TotalWeeks { get; set; }
    public decimal TotalRevenue { get; set; }
    public int TotalSales { get; set; }
}

public class SalesComparisonDto
{
    public decimal CurrentRevenue { get; set; }
    public int CurrentSaleCount { get; set; }
    public decimal CurrentAvgOrder { get; set; }
    public decimal PreviousRevenue { get; set; }
    public int PreviousSaleCount { get; set; }
    public decimal PreviousAvgOrder { get; set; }
    public decimal RevenueChangePercent { get; set; }
    public decimal SaleCountChangePercent { get; set; }
    public decimal AvgOrderChangePercent { get; set; }
    public string CurrentLabel { get; set; } = string.Empty;
    public string PreviousLabel { get; set; } = string.Empty;
}

public class CategoryRevenueItemDto
{
    public string Category { get; set; } = string.Empty;
    public decimal Revenue { get; set; }
    public int SaleCount { get; set; }
    public decimal PercentOfTotal { get; set; }
}

public class CategoryRevenueDto
{
    public List<CategoryRevenueItemDto> Categories { get; set; } = new();
    public decimal TotalRevenue { get; set; }
    public decimal UncategorizedRevenue { get; set; }
}

public class OutstandingDebtSummaryDto
{
    public decimal TotalReceivables { get; set; }
    public decimal TotalPayables { get; set; }
    public decimal NetPosition { get; set; }
    public int OverdueContactCount { get; set; }
    public List<OutstandingContactDto> TopReceivables { get; set; } = new();
    public List<OutstandingContactDto> TopPayables { get; set; } = new();
}

public class OutstandingContactDto
{
    public Guid ContactId { get; set; }
    public string ContactName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public int DaysOld { get; set; }
}

public class CashFlowForecastDto
{
    public List<CashFlowWeekDto> Actuals { get; set; } = new();
    public List<CashFlowWeekDto> Forecast { get; set; } = new();
    public decimal ProjectedMonthEndBalance { get; set; }
    public decimal AvgWeeklyCashIn { get; set; }
    public decimal AvgWeeklyCashOut { get; set; }
}

public class CashFlowWeekDto
{
    public string Label { get; set; } = string.Empty;
    public decimal CashIn { get; set; }
    public decimal CashOut { get; set; }
    public decimal Net { get; set; }
    public decimal RunningBalance { get; set; }
}
