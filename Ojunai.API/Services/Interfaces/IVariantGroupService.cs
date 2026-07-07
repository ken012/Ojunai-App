using Ojunai.API.DTOs.Variants;

namespace Ojunai.API.Services.Interfaces;

public interface IVariantGroupService
{
    Task<VariantGroupDto> CreateAsync(Guid businessId, CreateVariantGroupRequest request);
    Task<List<VariantGroupDto>> ListAsync(Guid businessId);
    Task<VariantGroupDto> GetAsync(Guid businessId, Guid groupId);
    Task<VariantGroupDto> AddVariantAsync(Guid businessId, Guid groupId, AddVariantRequest request);
    /// <summary>Dissolve the group: its variant products become standalone (kept). The group row is deleted.</summary>
    Task UngroupAsync(Guid businessId, Guid groupId);
}
