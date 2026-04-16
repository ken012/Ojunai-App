using BizPilot.API.Common;
using BizPilot.API.DTOs.Contacts;
using BizPilot.API.DTOs.Ledger;
using BizPilot.API.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BizPilot.API.Controllers;

[Route("api/contacts")]
public class ContactsController : BizPilotBaseController
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
        [FromQuery] string? search = null, [FromQuery] string? type = null)
    {
        var result = await _contacts.GetAllAsync(BusinessId, page, pageSize, search, type);
        return Ok(ApiResponse<PaginatedResult<ContactDto>>.Ok(result));
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
}
