using Ojunai.API.Common;
using Ojunai.API.DTOs.Stocktaking;
using Ojunai.API.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Ojunai.API.Controllers;

[Route("api/stocktakes")]
public class StocktakesController : OjunaiBaseController
{
    private readonly IStocktakeService _stocktakes;
    private readonly Data.AppDbContext _db;

    public StocktakesController(IStocktakeService stocktakes, Data.AppDbContext db)
    {
        _stocktakes = stocktakes;
        _db = db;
    }

    [HttpGet]
    [RequirePermission(Permission.ViewStock)]
    public async Task<ActionResult<ApiResponse<PaginatedResult<StocktakeDto>>>> List(
        [FromQuery] string? status = "all", [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var result = await _stocktakes.ListAsync(BusinessId, status, page, Math.Clamp(pageSize, 1, 100));
        return Ok(ApiResponse<PaginatedResult<StocktakeDto>>.Ok(result));
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(Permission.ViewStock)]
    public async Task<ActionResult<ApiResponse<StocktakeDto>>> GetById(Guid id)
        => Ok(ApiResponse<StocktakeDto>.Ok(await _stocktakes.GetByIdAsync(BusinessId, id)));

    [HttpPost]
    [RequirePermission(Permission.ManageStock)]
    public async Task<ActionResult<ApiResponse<StocktakeDto>>> Create([FromBody] CreateStocktakeRequest request)
    {
        var user = await _db.Users.FindAsync(UserId);
        var result = await _stocktakes.CreateAsync(BusinessId, request, user?.Id, user?.FullName);
        return CreatedAtAction(nameof(GetById), new { id = result.Id },
            ApiResponse<StocktakeDto>.Ok(result, "Stock count started."));
    }

    [HttpPut("{id:guid}/counts")]
    [RequirePermission(Permission.ManageStock)]
    public async Task<ActionResult<ApiResponse<StocktakeDto>>> SaveCounts(Guid id, [FromBody] SaveCountsRequest request)
        => Ok(ApiResponse<StocktakeDto>.Ok(await _stocktakes.SaveCountsAsync(BusinessId, id, request)));

    [HttpPost("{id:guid}/complete")]
    [RequirePermission(Permission.ManageStock)]
    public async Task<ActionResult<ApiResponse<StocktakeDto>>> Complete(Guid id)
    {
        var user = await _db.Users.FindAsync(UserId);
        var result = await _stocktakes.CompleteAsync(BusinessId, id, user?.Id, user?.FullName);
        return Ok(ApiResponse<StocktakeDto>.Ok(result, "Stock count applied."));
    }

    [HttpPost("{id:guid}/cancel")]
    [RequirePermission(Permission.ManageStock)]
    public async Task<ActionResult<ApiResponse<StocktakeDto>>> Cancel(Guid id)
        => Ok(ApiResponse<StocktakeDto>.Ok(await _stocktakes.CancelAsync(BusinessId, id), "Stock count cancelled."));
}
