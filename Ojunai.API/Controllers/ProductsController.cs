using Ojunai.API.Common;
using Ojunai.API.DTOs.Products;
using Ojunai.API.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Ojunai.API.Controllers;

[Route("api/products")]
public class ProductsController : OjunaiBaseController
{
    private readonly IProductService _products;
    private readonly Data.AppDbContext _db;
    private readonly PlanGuard _planGuard;

    public ProductsController(IProductService products, Data.AppDbContext db, PlanGuard planGuard) { _products = products; _db = db; _planGuard = planGuard; }

    [HttpGet]
    [RequirePermission(Permission.ViewStock)]
    public async Task<ActionResult<ApiResponse<PaginatedResult<ProductDto>>>> GetAll(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? search = null)
    {
        var result = await _products.GetAllAsync(BusinessId, page, pageSize, search);
        return Ok(ApiResponse<PaginatedResult<ProductDto>>.Ok(result));
    }

    [HttpGet("low-stock")]
    [RequirePermission(Permission.ViewStock)]
    public async Task<ActionResult<ApiResponse<List<ProductDto>>>> GetLowStock()
    {
        var result = await _products.GetLowStockAsync(BusinessId);
        return Ok(ApiResponse<List<ProductDto>>.Ok(result));
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(Permission.ViewStock)]
    public async Task<ActionResult<ApiResponse<ProductDto>>> GetById(Guid id)
    {
        var result = await _products.GetByIdAsync(BusinessId, id);
        return Ok(ApiResponse<ProductDto>.Ok(result));
    }

    [HttpPost]
    [RequirePermission(Permission.ManageStock)]
    public async Task<ActionResult<ApiResponse<ProductDto>>> Create([FromBody] CreateProductRequest request)
    {
        var limitError = await _planGuard.CheckProductLimitAsync(BusinessId);
        if (limitError != null) return BadRequest(ApiResponse<ProductDto>.Fail(limitError));

        var user = await _db.Users.FindAsync(UserId);
        var result = await _products.CreateAsync(BusinessId, request, user?.Id, user?.FullName);
        return CreatedAtAction(nameof(GetById), new { id = result.Id },
            ApiResponse<ProductDto>.Ok(result, "Product created."));
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(Permission.ManageStock)]
    public async Task<ActionResult<ApiResponse<ProductDto>>> Update(Guid id, [FromBody] UpdateProductRequest request)
    {
        var user = await _db.Users.FindAsync(UserId);
        var result = await _products.UpdateAsync(BusinessId, id, request, user?.Id, user?.FullName);
        return Ok(ApiResponse<ProductDto>.Ok(result));
    }

    [HttpPatch("{id:guid}/price")]
    [RequirePermission(Permission.ManageStock)]
    public async Task<ActionResult<ApiResponse<ProductDto>>> UpdatePrice(Guid id, [FromBody] UpdatePriceRequest request)
    {
        var result = await _products.UpdatePriceAsync(BusinessId, id, request);
        return Ok(ApiResponse<ProductDto>.Ok(result));
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(Permission.ManageStock)]
    public async Task<ActionResult<ApiResponse<object>>> Delete(Guid id)
    {
        await _products.DeleteAsync(BusinessId, id);
        return Ok(ApiResponse<object>.Ok(null!, "Product deleted."));
    }
}
