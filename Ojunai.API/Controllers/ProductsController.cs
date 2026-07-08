using Ojunai.API.Common;
using Ojunai.API.DTOs.Products;
using Ojunai.API.DTOs.Inventory;
using Ojunai.API.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Ojunai.API.Controllers;

[Route("api/products")]
public class ProductsController : OjunaiBaseController
{
    private readonly IProductService _products;
    private readonly IProductBatchService _batches;
    private readonly Data.AppDbContext _db;
    private readonly PlanGuard _planGuard;

    public ProductsController(IProductService products, IProductBatchService batches, Data.AppDbContext db, PlanGuard planGuard) { _products = products; _batches = batches; _db = db; _planGuard = planGuard; }

    [HttpGet]
    [RequirePermission(Permission.ViewStock)]
    public async Task<ActionResult<ApiResponse<PaginatedResult<ProductDto>>>> GetAll(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] string? category = null,
        [FromQuery] string? stockLevel = null,
        [FromQuery] bool excludeVariants = false)
    {
        var result = await _products.GetAllAsync(BusinessId, page, pageSize, search, category, stockLevel, excludeVariants);
        return Ok(ApiResponse<PaginatedResult<ProductDto>>.Ok(result));
    }

    [HttpGet("low-stock")]
    [RequirePermission(Permission.ViewStock)]
    public async Task<ActionResult<ApiResponse<List<ProductDto>>>> GetLowStock()
    {
        var result = await _products.GetLowStockAsync(BusinessId);
        return Ok(ApiResponse<List<ProductDto>>.Ok(result));
    }

    /// <summary>
    /// Per-stock-level counts honoring optional search + category filters. Powers the
    /// Inventory page's filter chip counts ("All 47 · In stock 30 · Low 12 · Out 5") without
    /// loading every product client-side.
    /// </summary>
    [HttpGet("stats")]
    [RequirePermission(Permission.ViewStock)]
    public async Task<ActionResult<ApiResponse<ProductStockStatsDto>>> GetStats(
        [FromQuery] string? search = null,
        [FromQuery] string? category = null)
    {
        var result = await _products.GetStockStatsAsync(BusinessId, search, category);
        return Ok(ApiResponse<ProductStockStatsDto>.Ok(result));
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(Permission.ViewStock)]
    public async Task<ActionResult<ApiResponse<ProductDto>>> GetById(Guid id)
    {
        var result = await _products.GetByIdAsync(BusinessId, id);
        return Ok(ApiResponse<ProductDto>.Ok(result));
    }

    /// <summary>Scan-to-lookup: find a product by its barcode. 404 if no active product has it.</summary>
    [HttpGet("by-barcode/{barcode}")]
    [RequirePermission(Permission.ViewStock)]
    public async Task<ActionResult<ApiResponse<ProductDto>>> GetByBarcode(string barcode)
    {
        var result = await _products.GetByBarcodeAsync(BusinessId, barcode);
        return result == null
            ? NotFound(ApiResponse<ProductDto>.Fail("No product found with that barcode."))
            : Ok(ApiResponse<ProductDto>.Ok(result));
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

    [HttpGet("expiring")]
    [RequirePermission(Permission.ViewStock)]
    public async Task<ActionResult<ApiResponse<List<ProductBatchDto>>>> Expiring([FromQuery] int days = 30)
        => Ok(ApiResponse<List<ProductBatchDto>>.Ok(await _batches.ExpiringAsync(BusinessId, days)));

    [HttpGet("{id:guid}/batches")]
    [RequirePermission(Permission.ViewStock)]
    public async Task<ActionResult<ApiResponse<List<ProductBatchDto>>>> GetBatches(Guid id)
        => Ok(ApiResponse<List<ProductBatchDto>>.Ok(await _batches.ListAsync(BusinessId, id)));

    [HttpPost("{id:guid}/batches/{batchId:guid}/write-off")]
    [RequirePermission(Permission.ManageStock)]
    public async Task<ActionResult<ApiResponse<List<ProductBatchDto>>>> WriteOffBatch(Guid id, Guid batchId, [FromBody] WriteOffBatchRequest request)
    {
        var user = await _db.Users.FindAsync(UserId);
        var result = await _batches.WriteOffAsync(BusinessId, id, batchId, request, user?.Id, user?.FullName);
        return Ok(ApiResponse<List<ProductBatchDto>>.Ok(result, "Lot written off."));
    }

    [HttpGet("{id:guid}/bundle")]
    [RequirePermission(Permission.ViewStock)]
    public async Task<ActionResult<ApiResponse<BundleDto>>> GetBundle(Guid id)
        => Ok(ApiResponse<BundleDto>.Ok(await _products.GetBundleAsync(BusinessId, id)));

    [HttpPut("{id:guid}/bundle")]
    [RequirePermission(Permission.ManageStock)]
    public async Task<ActionResult<ApiResponse<BundleDto>>> SetBundle(Guid id, [FromBody] SetBundleRequest request)
        => Ok(ApiResponse<BundleDto>.Ok(await _products.SetBundleAsync(BusinessId, id, request), "Bundle saved."));
}
