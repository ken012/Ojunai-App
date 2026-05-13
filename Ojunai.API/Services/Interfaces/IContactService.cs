using Ojunai.API.Common;
using Ojunai.API.DTOs.Contacts;

namespace Ojunai.API.Services.Interfaces;

public interface IContactService
{
    Task<PaginatedResult<ContactDto>> GetAllAsync(Guid businessId, int page, int pageSize, string? search, string? type, string? balance);
    Task<ContactDto> GetByIdAsync(Guid businessId, Guid contactId);
    Task<ContactDto> CreateAsync(Guid businessId, CreateContactRequest request);
    Task<ContactDto> UpdateAsync(Guid businessId, Guid contactId, CreateContactRequest request);
    Task DeleteAsync(Guid businessId, Guid contactId);

    /// <summary>
    /// Sum of all contacts' receivables and payables. Honors the same search/type/balance
    /// filters as <see cref="GetAllAsync"/> so the dashboard can show headline totals
    /// matching the visible (filtered) set without paging the full list client-side.
    /// </summary>
    Task<ContactTotalsDto> GetTotalsAsync(Guid businessId, string? search, string? type, string? balance);
}

public class ContactTotalsDto
{
    public int TotalContacts { get; set; }
    public decimal TotalReceivable { get; set; }
    public decimal TotalPayable { get; set; }
}
