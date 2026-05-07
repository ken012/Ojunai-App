using Ojunai.API.Common;
using Ojunai.API.DTOs.Dashboard;
using Ojunai.API.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Ojunai.API.Controllers;

[Route("api/dashboard")]
public class DashboardController : OjunaiBaseController
{
    private readonly IReportService _reports;
    private readonly PlanGuard _planGuard;

    public DashboardController(IReportService reports, PlanGuard planGuard) { _reports = reports; _planGuard = planGuard; }

    [HttpGet("overview")]
    [RequirePermission(Permission.ViewOwnReports)]
    public async Task<ActionResult<ApiResponse<DashboardOverviewDto>>> GetOverview()
    {
        var result = await _reports.GetDashboardOverviewAsync(BusinessId);
        return Ok(ApiResponse<DashboardOverviewDto>.Ok(result));
    }

    [HttpGet("recent-activity")]
    [RequirePermission(Permission.ViewOwnReports)]
    public async Task<ActionResult<ApiResponse<List<RecentActivityDto>>>> GetRecentActivity([FromQuery] int limit = 10)
    {
        var result = await _reports.GetRecentActivityAsync(BusinessId, limit);
        return Ok(ApiResponse<List<RecentActivityDto>>.Ok(result));
    }

    [HttpGet("insights")]
    [RequirePermission(Permission.ViewOwnReports)]
    public async Task<ActionResult<ApiResponse<DashboardInsightsDto>>> GetInsights()
    {
        var (allowed, planErr) = await _planGuard.CheckFeatureAsync(BusinessId, "monthly_charts");
        if (!allowed) return BadRequest(ApiResponse<DashboardInsightsDto>.Fail(planErr!));

        var result = await _reports.GetInsightsAsync(BusinessId);
        return Ok(ApiResponse<DashboardInsightsDto>.Ok(result));
    }

    [HttpGet("activity")]
    [RequirePermission(Permission.ViewOwnReports)]
    public async Task<ActionResult<ApiResponse<PaginatedActivityResult>>> GetActivityFeed(
        [FromQuery] string? type = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 50,
        [FromQuery] string? search = null, [FromQuery] string? startDate = null, [FromQuery] string? endDate = null,
        [FromQuery] string? source = null)
    {
        // Parse date strings explicitly — HTML date inputs send "2026-04-17" which DateTime? binding
        // sometimes fails to parse. DateOnly handles the yyyy-MM-dd format reliably.
        DateTime? start = null, end = null;
        if (DateOnly.TryParse(startDate, out var sd)) start = sd.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        if (DateOnly.TryParse(endDate, out var ed)) end = ed.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var result = await _reports.GetActivityFeedAsync(BusinessId, type, Math.Max(1, page), Math.Clamp(pageSize, 10, 100), search, start, end, source);
        return Ok(ApiResponse<PaginatedActivityResult>.Ok(result));
    }
}
