using Ojunai.API.Common;
using Ojunai.API.DTOs.Contacts;
using Ojunai.API.DTOs.Ledger;
using Ojunai.API.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Ojunai.API.Controllers;

[Route("api/contacts")]
public class ContactsController : OjunaiBaseController
{
    private readonly IContactService _contacts;
    private readonly ILedgerService _ledger;

    public ContactsController(IContactService contacts, ILedgerService ledger)
    {
        _contacts = contacts;
        _ledger = ledger;
    }

    [HttpGet]
    [RequirePermission(Permission.ViewOwnReports)]
    public async Task<ActionResult<ApiResponse<PaginatedResult<ContactDto>>>> GetAll(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null, [FromQuery] string? type = null,
        [FromQuery] string? balance = null)
    {
        var result = await _contacts.GetAllAsync(BusinessId, page, pageSize, search, type, balance);
        return Ok(ApiResponse<PaginatedResult<ContactDto>>.Ok(result));
    }

    /// <summary>
    /// Headline totals across all contacts matching the same filters as <see cref="GetAll"/>.
    /// Lets the Contacts page show "Total receivables: ₦X" without paging the entire list
    /// client-side.
    /// </summary>
    [HttpGet("totals")]
    [RequirePermission(Permission.ViewOwnReports)]
    public async Task<ActionResult<ApiResponse<ContactTotalsDto>>> GetTotals(
        [FromQuery] string? search = null, [FromQuery] string? type = null,
        [FromQuery] string? balance = null)
    {
        var result = await _contacts.GetTotalsAsync(BusinessId, search, type, balance);
        return Ok(ApiResponse<ContactTotalsDto>.Ok(result));
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(Permission.ViewOwnReports)]
    public async Task<ActionResult<ApiResponse<ContactDto>>> GetById(Guid id)
    {
        var result = await _contacts.GetByIdAsync(BusinessId, id);
        return Ok(ApiResponse<ContactDto>.Ok(result));
    }

    [HttpPost]
    [RequirePermission(Permission.ManageDebts)]
    public async Task<ActionResult<ApiResponse<ContactDto>>> Create([FromBody] CreateContactRequest request)
    {
        var result = await _contacts.CreateAsync(BusinessId, request);
        return CreatedAtAction(nameof(GetById), new { id = result.Id },
            ApiResponse<ContactDto>.Ok(result, "Contact created."));
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(Permission.ManageDebts)]
    public async Task<ActionResult<ApiResponse<ContactDto>>> Update(Guid id, [FromBody] CreateContactRequest request)
    {
        var result = await _contacts.UpdateAsync(BusinessId, id, request);
        return Ok(ApiResponse<ContactDto>.Ok(result));
    }

    [HttpGet("{id:guid}/ledger")]
    [RequirePermission(Permission.ManageDebts)]
    public async Task<ActionResult<ApiResponse<List<LedgerEntryDto>>>> GetLedger(Guid id)
    {
        var result = await _ledger.GetContactLedgerAsync(BusinessId, id);
        return Ok(ApiResponse<List<LedgerEntryDto>>.Ok(result));
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(Permission.ManageDebts)]
    public async Task<ActionResult<ApiResponse<object>>> Delete(Guid id)
    {
        await _contacts.DeleteAsync(BusinessId, id);
        return Ok(ApiResponse<object>.Ok(null!, "Contact deleted."));
    }
}
