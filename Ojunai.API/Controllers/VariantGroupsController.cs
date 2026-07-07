using Ojunai.API.Common;
using Ojunai.API.DTOs.Variants;
using Ojunai.API.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Ojunai.API.Controllers;

[Route("api/variant-groups")]
public class VariantGroupsController : OjunaiBaseController
{
    private readonly IVariantGroupService _variants;
    public VariantGroupsController(IVariantGroupService variants) => _variants = variants;

    [HttpGet]
    [RequirePermission(Permission.ViewStock)]
    public async Task<ActionResult<ApiResponse<List<VariantGroupDto>>>> List()
        => Ok(ApiResponse<List<VariantGroupDto>>.Ok(await _variants.ListAsync(BusinessId)));

    [HttpGet("{id:guid}")]
    [RequirePermission(Permission.ViewStock)]
    public async Task<ActionResult<ApiResponse<VariantGroupDto>>> Get(Guid id)
        => Ok(ApiResponse<VariantGroupDto>.Ok(await _variants.GetAsync(BusinessId, id)));

    [HttpPost]
    [RequirePermission(Permission.ManageStock)]
    public async Task<ActionResult<ApiResponse<VariantGroupDto>>> Create([FromBody] CreateVariantGroupRequest request)
    {
        var result = await _variants.CreateAsync(BusinessId, request);
        return CreatedAtAction(nameof(Get), new { id = result.Id },
            ApiResponse<VariantGroupDto>.Ok(result, "Variant style created."));
    }

    [HttpPost("{id:guid}/variants")]
    [RequirePermission(Permission.ManageStock)]
    public async Task<ActionResult<ApiResponse<VariantGroupDto>>> AddVariant(Guid id, [FromBody] AddVariantRequest request)
        => Ok(ApiResponse<VariantGroupDto>.Ok(await _variants.AddVariantAsync(BusinessId, id, request), "Variant added."));

    [HttpPost("{id:guid}/ungroup")]
    [RequirePermission(Permission.ManageStock)]
    public async Task<ActionResult<ApiResponse<object>>> Ungroup(Guid id)
    {
        await _variants.UngroupAsync(BusinessId, id);
        return Ok(ApiResponse<object>.Ok(null!, "Style dissolved — variants kept as standalone products."));
    }
}
