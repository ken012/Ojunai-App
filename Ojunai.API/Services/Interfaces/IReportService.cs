using Ojunai.API.DTOs.Dashboard;
using Ojunai.API.DTOs.Reports;

namespace Ojunai.API.Services.Interfaces;

public interface IReportService
{
    Task<DashboardOverviewDto> GetDashboardOverviewAsync(Guid businessId);
    Task<List<RecentActivityDto>> GetRecentActivityAsync(Guid businessId, int limit);
    Task<DailySummaryDto> GetDailySummaryAsync(Guid businessId, DateOnly? date);
    Task<WeeklySummaryDto> GetWeeklySummaryAsync(Guid businessId, DateOnly? weekStart);
    Task<CashPositionDto> GetCashPositionAsync(Guid businessId);
    Task<DashboardInsightsDto> GetInsightsAsync(Guid businessId);
    Task<PaginatedActivityResult> GetActivityFeedAsync(Guid businessId, string? type, int page, int pageSize, string? search, DateTime? startDate, DateTime? endDate, string? source = null);
    Task<List<DeadStockItemDto>> GetDeadStockAsync(Guid businessId);
    Task<List<StockoutPredictionDto>> GetStockoutPredictionsAsync(Guid businessId);
    Task<List<ProductProfitDto>> GetProfitByProductAsync(Guid businessId);
    Task<List<StaffSalesDto>> GetStaffSalesAsync(Guid businessId, string? staffName, DateOnly? date);

    // Advanced reports (gated by the advanced_reports plan feature at the controller layer)
    Task<AgingReportDto> GetAgingReceivablesAsync(Guid businessId);
    Task<AgingReportDto> GetAgingPayablesAsync(Guid businessId);
    Task<MonthlyPnlDto> GetMonthlyPnlAsync(Guid businessId, DateOnly? month);
    Task<ExpenseBreakdownDto> GetExpenseBreakdownAsync(Guid businessId, DateOnly? month);
    Task<List<InventoryTurnoverDto>> GetInventoryTurnoverAsync(Guid businessId);
    Task<TopCustomersReportDto> GetTopCustomersAsync(Guid businessId, int limit);
    Task<SalesHeatmapDto> GetSalesHeatmapAsync(Guid businessId, int weeks, string? timezone = null);
    Task<MonthlyTrendDto> GetMonthlyTrendAsync(Guid businessId, int months);
    Task<PaymentMethodSplitDto> GetPaymentMethodSplitAsync(Guid businessId, int months);
    Task<List<CustomerReliabilityDto>> GetCustomerReliabilityAsync(Guid businessId);
    Task<WastageReportDto> GetWastageReportAsync(Guid businessId, int days);
    Task<AvgTransactionValueDto> GetAvgTransactionValueAsync(Guid businessId, int months);
    Task<CustomerRetentionDto> GetCustomerRetentionAsync(Guid businessId, int months);
    Task<List<ReorderSuggestionDto>> GetReorderSuggestionsAsync(Guid businessId, int safetyDays);
    Task<List<ProductAffinityDto>> GetProductAffinityAsync(Guid businessId, int limit);
    Task<WeeklySalesTrendDto> GetWeeklySalesTrendAsync(Guid businessId, int months);
    Task<SalesComparisonDto> GetSalesComparisonAsync(Guid businessId, string period);
    Task<CategoryRevenueDto> GetCategoryRevenueAsync(Guid businessId, int days);
    Task<OutstandingDebtSummaryDto> GetOutstandingDebtSummaryAsync(Guid businessId);
    Task<CashFlowForecastDto> GetCashFlowForecastAsync(Guid businessId);
}
