using BizPilot.API.Common;
using BizPilot.API.DTOs.Reports;
using BizPilot.API.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BizPilot.API.Controllers;

[Route("api/reports")]
[RequirePermission(Permission.ViewOwnReports)]
public class ReportsController : BizPilotBaseController
{
    private readonly IReportService _reports;
    private readonly PlanGuard _planGuard;

    public ReportsController(IReportService reports, PlanGuard planGuard) { _reports = reports; _planGuard = planGuard; }

    [HttpGet("daily")]
    public async Task<ActionResult<ApiResponse<DailySummaryDto>>> GetDaily([FromQuery] DateOnly? date = null)
    {
        var result = await _reports.GetDailySummaryAsync(BusinessId, date);
        return Ok(ApiResponse<DailySummaryDto>.Ok(result));
    }

    [HttpGet("weekly")]
    [RequirePermission(Permission.ViewAllReports)]
    public async Task<ActionResult<ApiResponse<WeeklySummaryDto>>> GetWeekly([FromQuery] DateOnly? weekStart = null)
    {
        var (allowed, planErr) = await _planGuard.CheckFeatureAsync(BusinessId, "advanced_reports");
        if (!allowed) return BadRequest(ApiResponse<WeeklySummaryDto>.Fail(planErr!));

        var result = await _reports.GetWeeklySummaryAsync(BusinessId, weekStart);
        return Ok(ApiResponse<WeeklySummaryDto>.Ok(result));
    }

    [HttpGet("cash-position")]
    [RequirePermission(Permission.ViewAllReports)]
    public async Task<ActionResult<ApiResponse<CashPositionDto>>> GetCashPosition()
    {
        var (allowed, planErr) = await _planGuard.CheckFeatureAsync(BusinessId, "advanced_reports");
        if (!allowed) return BadRequest(ApiResponse<CashPositionDto>.Fail(planErr!));

        var result = await _reports.GetCashPositionAsync(BusinessId);
        return Ok(ApiResponse<CashPositionDto>.Ok(result));
    }

    [HttpGet("dead-stock")]
    public async Task<ActionResult<ApiResponse<List<DeadStockItemDto>>>> GetDeadStock()
    {
        var result = await _reports.GetDeadStockAsync(BusinessId);
        return Ok(ApiResponse<List<DeadStockItemDto>>.Ok(result));
    }

    [HttpGet("stockout-predictions")]
    public async Task<ActionResult<ApiResponse<List<StockoutPredictionDto>>>> GetStockoutPredictions()
    {
        var result = await _reports.GetStockoutPredictionsAsync(BusinessId);
        return Ok(ApiResponse<List<StockoutPredictionDto>>.Ok(result));
    }

    [HttpGet("profit-by-product")]
    public async Task<ActionResult<ApiResponse<List<ProductProfitDto>>>> GetProfitByProduct()
    {
        var result = await _reports.GetProfitByProductAsync(BusinessId);
        return Ok(ApiResponse<List<ProductProfitDto>>.Ok(result));
    }

    [HttpGet("staff-sales")]
    public async Task<ActionResult<ApiResponse<List<StaffSalesDto>>>> GetStaffSales(
        [FromQuery] string? staffName = null, [FromQuery] DateOnly? date = null)
    {
        var result = await _reports.GetStaffSalesAsync(BusinessId, staffName, date);
        return Ok(ApiResponse<List<StaffSalesDto>>.Ok(result));
    }

    // ── Advanced reports — all gated on advanced_reports (Shop+) ────────────────

    [HttpGet("aging-receivables")]
    [RequirePermission(Permission.ViewAllReports)]
    public async Task<ActionResult<ApiResponse<AgingReportDto>>> GetAgingReceivables()
    {
        var (allowed, err) = await _planGuard.CheckFeatureAsync(BusinessId, "advanced_reports");
        if (!allowed) return BadRequest(ApiResponse<AgingReportDto>.Fail(err!));
        var result = await _reports.GetAgingReceivablesAsync(BusinessId);
        return Ok(ApiResponse<AgingReportDto>.Ok(result));
    }

    [HttpGet("aging-payables")]
    [RequirePermission(Permission.ViewAllReports)]
    public async Task<ActionResult<ApiResponse<AgingReportDto>>> GetAgingPayables()
    {
        var (allowed, err) = await _planGuard.CheckFeatureAsync(BusinessId, "advanced_reports");
        if (!allowed) return BadRequest(ApiResponse<AgingReportDto>.Fail(err!));
        var result = await _reports.GetAgingPayablesAsync(BusinessId);
        return Ok(ApiResponse<AgingReportDto>.Ok(result));
    }

    [HttpGet("monthly-pnl")]
    [RequirePermission(Permission.ViewAllReports)]
    public async Task<ActionResult<ApiResponse<MonthlyPnlDto>>> GetMonthlyPnl([FromQuery] DateOnly? month = null)
    {
        var (allowed, err) = await _planGuard.CheckFeatureAsync(BusinessId, "advanced_reports");
        if (!allowed) return BadRequest(ApiResponse<MonthlyPnlDto>.Fail(err!));
        var result = await _reports.GetMonthlyPnlAsync(BusinessId, month);
        return Ok(ApiResponse<MonthlyPnlDto>.Ok(result));
    }

    [HttpGet("expense-breakdown")]
    [RequirePermission(Permission.ViewAllReports)]
    public async Task<ActionResult<ApiResponse<ExpenseBreakdownDto>>> GetExpenseBreakdown([FromQuery] DateOnly? month = null)
    {
        var (allowed, err) = await _planGuard.CheckFeatureAsync(BusinessId, "advanced_reports");
        if (!allowed) return BadRequest(ApiResponse<ExpenseBreakdownDto>.Fail(err!));
        var result = await _reports.GetExpenseBreakdownAsync(BusinessId, month);
        return Ok(ApiResponse<ExpenseBreakdownDto>.Ok(result));
    }

    [HttpGet("inventory-turnover")]
    [RequirePermission(Permission.ViewAllReports)]
    public async Task<ActionResult<ApiResponse<List<InventoryTurnoverDto>>>> GetInventoryTurnover()
    {
        var (allowed, err) = await _planGuard.CheckFeatureAsync(BusinessId, "advanced_reports");
        if (!allowed) return BadRequest(ApiResponse<List<InventoryTurnoverDto>>.Fail(err!));
        var result = await _reports.GetInventoryTurnoverAsync(BusinessId);
        return Ok(ApiResponse<List<InventoryTurnoverDto>>.Ok(result));
    }

    [HttpGet("top-customers")]
    [RequirePermission(Permission.ViewAllReports)]
    public async Task<ActionResult<ApiResponse<TopCustomersReportDto>>> GetTopCustomers([FromQuery] int limit = 20)
    {
        var (allowed, err) = await _planGuard.CheckFeatureAsync(BusinessId, "advanced_reports");
        if (!allowed) return BadRequest(ApiResponse<TopCustomersReportDto>.Fail(err!));
        var result = await _reports.GetTopCustomersAsync(BusinessId, limit);
        return Ok(ApiResponse<TopCustomersReportDto>.Ok(result));
    }

    [HttpGet("sales-heatmap")]
    [RequirePermission(Permission.ViewAllReports)]
    public async Task<ActionResult<ApiResponse<SalesHeatmapDto>>> GetSalesHeatmap([FromQuery] int weeks = 12)
    {
        var (allowed, err) = await _planGuard.CheckFeatureAsync(BusinessId, "advanced_reports");
        if (!allowed) return BadRequest(ApiResponse<SalesHeatmapDto>.Fail(err!));
        var result = await _reports.GetSalesHeatmapAsync(BusinessId, weeks);
        return Ok(ApiResponse<SalesHeatmapDto>.Ok(result));
    }

    [HttpGet("monthly-trend")]
    [RequirePermission(Permission.ViewAllReports)]
    public async Task<ActionResult<ApiResponse<MonthlyTrendDto>>> GetMonthlyTrend([FromQuery] int months = 12)
    {
        var (allowed, err) = await _planGuard.CheckFeatureAsync(BusinessId, "advanced_reports");
        if (!allowed) return BadRequest(ApiResponse<MonthlyTrendDto>.Fail(err!));
        var result = await _reports.GetMonthlyTrendAsync(BusinessId, months);
        return Ok(ApiResponse<MonthlyTrendDto>.Ok(result));
    }

    [HttpGet("payment-method-split")]
    [RequirePermission(Permission.ViewAllReports)]
    public async Task<ActionResult<ApiResponse<PaymentMethodSplitDto>>> GetPaymentMethodSplit([FromQuery] int months = 6)
    {
        var (allowed, err) = await _planGuard.CheckFeatureAsync(BusinessId, "advanced_reports");
        if (!allowed) return BadRequest(ApiResponse<PaymentMethodSplitDto>.Fail(err!));
        var result = await _reports.GetPaymentMethodSplitAsync(BusinessId, months);
        return Ok(ApiResponse<PaymentMethodSplitDto>.Ok(result));
    }

    [HttpGet("customer-reliability")]
    [RequirePermission(Permission.ViewAllReports)]
    public async Task<ActionResult<ApiResponse<List<CustomerReliabilityDto>>>> GetCustomerReliability()
    {
        var (allowed, err) = await _planGuard.CheckFeatureAsync(BusinessId, "advanced_reports");
        if (!allowed) return BadRequest(ApiResponse<List<CustomerReliabilityDto>>.Fail(err!));
        var result = await _reports.GetCustomerReliabilityAsync(BusinessId);
        return Ok(ApiResponse<List<CustomerReliabilityDto>>.Ok(result));
    }

    [HttpGet("wastage")]
    [RequirePermission(Permission.ViewAllReports)]
    public async Task<ActionResult<ApiResponse<WastageReportDto>>> GetWastage([FromQuery] int days = 30)
    {
        var (allowed, err) = await _planGuard.CheckFeatureAsync(BusinessId, "advanced_reports");
        if (!allowed) return BadRequest(ApiResponse<WastageReportDto>.Fail(err!));
        var result = await _reports.GetWastageReportAsync(BusinessId, days);
        return Ok(ApiResponse<WastageReportDto>.Ok(result));
    }

    [HttpGet("avg-transaction-value")]
    [RequirePermission(Permission.ViewAllReports)]
    public async Task<ActionResult<ApiResponse<AvgTransactionValueDto>>> GetAvgTransactionValue([FromQuery] int months = 12)
    {
        var (allowed, err) = await _planGuard.CheckFeatureAsync(BusinessId, "advanced_reports");
        if (!allowed) return BadRequest(ApiResponse<AvgTransactionValueDto>.Fail(err!));
        var result = await _reports.GetAvgTransactionValueAsync(BusinessId, months);
        return Ok(ApiResponse<AvgTransactionValueDto>.Ok(result));
    }

    [HttpGet("customer-retention")]
    [RequirePermission(Permission.ViewAllReports)]
    public async Task<ActionResult<ApiResponse<CustomerRetentionDto>>> GetCustomerRetention([FromQuery] int months = 6)
    {
        var (allowed, err) = await _planGuard.CheckFeatureAsync(BusinessId, "advanced_reports");
        if (!allowed) return BadRequest(ApiResponse<CustomerRetentionDto>.Fail(err!));
        var result = await _reports.GetCustomerRetentionAsync(BusinessId, months);
        return Ok(ApiResponse<CustomerRetentionDto>.Ok(result));
    }

    [HttpGet("reorder-suggestions")]
    [RequirePermission(Permission.ViewAllReports)]
    public async Task<ActionResult<ApiResponse<List<ReorderSuggestionDto>>>> GetReorderSuggestions([FromQuery] int safetyDays = 7)
    {
        var (allowed, err) = await _planGuard.CheckFeatureAsync(BusinessId, "advanced_reports");
        if (!allowed) return BadRequest(ApiResponse<List<ReorderSuggestionDto>>.Fail(err!));
        var result = await _reports.GetReorderSuggestionsAsync(BusinessId, safetyDays);
        return Ok(ApiResponse<List<ReorderSuggestionDto>>.Ok(result));
    }

    [HttpGet("product-affinity")]
    [RequirePermission(Permission.ViewAllReports)]
    public async Task<ActionResult<ApiResponse<List<ProductAffinityDto>>>> GetProductAffinity([FromQuery] int limit = 20)
    {
        var (allowed, err) = await _planGuard.CheckFeatureAsync(BusinessId, "advanced_reports");
        if (!allowed) return BadRequest(ApiResponse<List<ProductAffinityDto>>.Fail(err!));
        var result = await _reports.GetProductAffinityAsync(BusinessId, limit);
        return Ok(ApiResponse<List<ProductAffinityDto>>.Ok(result));
    }
}
