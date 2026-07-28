using Ojunai.API.Common;
using Ojunai.API.Data;
using Ojunai.API.DTOs.Inventory;
using Ojunai.API.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Ojunai.API.Controllers;

/// <summary>
/// Stock transfers between a business's locations (multi-location Phase 3). Gated by ManageStock, which only
/// Owner/Admin hold — and they are all-access — so there's no per-location access check to make here.
/// </summary>
[Route("api/inventory/transfers")]
public class StockTransfersController : OjunaiBaseController
{
    private readonly IStockTransferService _transfers;
    private readonly AppDbContext _db;

    public StockTransfersController(IStockTransferService transfers, AppDbContext db)
    {
        _transfers = transfers;
        _db = db;
    }

    [HttpPost]
    [RequirePermission(Permission.ManageStock)]
    public async Task<ActionResult<ApiResponse<StockTransferDto>>> Create([FromBody] CreateStockTransferRequest request)
    {
        var user = await _db.Users.FindAsync(UserId);
        var result = await _transfers.TransferAsync(BusinessId, request, user?.Id, user?.FullName);
        return Ok(ApiResponse<StockTransferDto>.Ok(result,
            $"Transferred {result.Quantity:0.####} {UnitFormat.Plural(result.Quantity, result.Unit)} of {result.ProductName} from {result.FromLocationName} to {result.ToLocationName}."));
    }

    [HttpGet]
    [RequirePermission(Permission.ViewStock)]
    public async Task<ActionResult<ApiResponse<PaginatedResult<StockTransferDto>>>> GetAll(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] Guid? productId = null, [FromQuery] Guid? locationId = null)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var result = await _transfers.GetAllAsync(BusinessId, page, pageSize, productId, locationId);
        return Ok(ApiResponse<PaginatedResult<StockTransferDto>>.Ok(result));
    }
}
