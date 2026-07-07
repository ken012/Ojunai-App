using Ojunai.API.Common;
using Ojunai.API.DTOs.Purchasing;
using Ojunai.API.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Ojunai.API.Controllers;

[Route("api/purchase-orders")]
public class PurchaseOrdersController : OjunaiBaseController
{
    private readonly IPurchaseOrderService _po;
    private readonly Data.AppDbContext _db;

    public PurchaseOrdersController(IPurchaseOrderService po, Data.AppDbContext db)
    {
        _po = po;
        _db = db;
    }

    [HttpGet]
    [RequirePermission(Permission.ViewStock)]
    public async Task<ActionResult<ApiResponse<PaginatedResult<PurchaseOrderDto>>>> List(
        [FromQuery] string? status = "all", [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var result = await _po.ListAsync(BusinessId, status, page, Math.Clamp(pageSize, 1, 100));
        return Ok(ApiResponse<PaginatedResult<PurchaseOrderDto>>.Ok(result));
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(Permission.ViewStock)]
    public async Task<ActionResult<ApiResponse<PurchaseOrderDto>>> GetById(Guid id)
        => Ok(ApiResponse<PurchaseOrderDto>.Ok(await _po.GetByIdAsync(BusinessId, id)));

    [HttpPost]
    [RequirePermission(Permission.ManageStock)]
    public async Task<ActionResult<ApiResponse<PurchaseOrderDto>>> Create([FromBody] CreatePurchaseOrderRequest request)
    {
        var user = await _db.Users.FindAsync(UserId);
        var result = await _po.CreateAsync(BusinessId, request, user?.Id, user?.FullName);
        return CreatedAtAction(nameof(GetById), new { id = result.Id },
            ApiResponse<PurchaseOrderDto>.Ok(result, "Purchase order created."));
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(Permission.ManageStock)]
    public async Task<ActionResult<ApiResponse<PurchaseOrderDto>>> Update(Guid id, [FromBody] UpdatePurchaseOrderRequest request)
        => Ok(ApiResponse<PurchaseOrderDto>.Ok(await _po.UpdateAsync(BusinessId, id, request)));

    [HttpPost("{id:guid}/send")]
    [RequirePermission(Permission.ManageStock)]
    public async Task<ActionResult<ApiResponse<PurchaseOrderDto>>> MarkSent(Guid id)
        => Ok(ApiResponse<PurchaseOrderDto>.Ok(await _po.MarkSentAsync(BusinessId, id), "Marked as sent."));

    [HttpPost("{id:guid}/receive")]
    [RequirePermission(Permission.ManageStock)]
    public async Task<ActionResult<ApiResponse<PurchaseOrderDto>>> Receive(Guid id, [FromBody] ReceivePurchaseOrderRequest request)
    {
        var user = await _db.Users.FindAsync(UserId);
        var result = await _po.ReceiveAsync(BusinessId, id, request, user?.Id, user?.FullName);
        return Ok(ApiResponse<PurchaseOrderDto>.Ok(result, "Stock received."));
    }

    [HttpPost("{id:guid}/cancel")]
    [RequirePermission(Permission.ManageStock)]
    public async Task<ActionResult<ApiResponse<PurchaseOrderDto>>> Cancel(Guid id)
        => Ok(ApiResponse<PurchaseOrderDto>.Ok(await _po.CancelAsync(BusinessId, id), "Purchase order cancelled."));
}
