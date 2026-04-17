using BizPilot.API.Common;
using BizPilot.API.DTOs.Contacts;

namespace BizPilot.API.Services.Interfaces;

public interface IContactService
{
    Task<PaginatedResult<ContactDto>> GetAllAsync(Guid businessId, int page, int pageSize, string? search, string? type);
    Task<ContactDto> GetByIdAsync(Guid businessId, Guid contactId);
    Task<ContactDto> CreateAsync(Guid businessId, CreateContactRequest request);
    Task<ContactDto> UpdateAsync(Guid businessId, Guid contactId, CreateContactRequest request);
    Task DeleteAsync(Guid businessId, Guid contactId);
}
