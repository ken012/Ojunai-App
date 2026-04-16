using BizPilot.API.Common;
using BizPilot.API.DTOs.Inventory;
using BizPilot.API.DTOs.Sales;
using BizPilot.API.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BizPilot.API.Controllers;

[Route("api/stock-holds")]
public class StockHoldsController : BizPilotBaseController
{
    private readonly IStockHoldService _holds;
    private readonly PlanGuard _planGuard;

    public StockHoldsController(IStockHoldService holds, PlanGuard planGuard) { _holds = holds; _planGuard = planGuard; }

    [HttpPost]
    [RequirePermission(Permission.ManageStock)]
    public async Task<ActionResult<ApiResponse<StockHoldDto>>> Create([FromBody] CreateHoldRequest request)
    {
        var (allowed, planErr) = await _planGuard.CheckFeatureAsync(BusinessId, "stock_holds");
        if (!allowed) return BadRequest(ApiResponse<StockHoldDto>.Fail(planErr!));

        var result = await _holds.CreateHoldAsync(BusinessId, request.ProductId, request.ContactName, request.Quantity, request.Notes);
        return Ok(ApiResponse<StockHoldDto>.Ok(result, "Stock held."));
    }

    [HttpGet]
    [RequirePermission(Permission.ViewStock)]
    public async Task<ActionResult<ApiResponse<List<StockHoldDto>>>> GetActive()
    {
        var result = await _holds.GetActiveHoldsAsync(BusinessId);
        return Ok(ApiResponse<List<StockHoldDto>>.Ok(result));
    }

    [HttpPost("{id:guid}/release")]
    [RequirePermission(Permission.ManageStock)]
    public async Task<ActionResult<ApiResponse<StockHoldDto>>> Release(Guid id)
    {
        var result = await _holds.ReleaseHoldAsync(BusinessId, id);
        return Ok(ApiResponse<StockHoldDto>.Ok(result, "Hold released."));
    }

    [HttpPost("{id:guid}/convert")]
    [RequirePermission(Permission.RecordSales)]
    public async Task<ActionResult<ApiResponse<SaleDto>>> Convert(Guid id)
    {
        var result = await _holds.ConvertToSaleAsync(BusinessId, id);
        return Ok(ApiResponse<SaleDto>.Ok(result, "Hold converted to sale."));
    }
}
