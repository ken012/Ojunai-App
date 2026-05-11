using Ojunai.API.Common;
using Ojunai.API.Data;
using Ojunai.API.DTOs.Ledger;
using Ojunai.API.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Ojunai.API.Controllers;

[Route("api/ledger")]
public class LedgerController : OjunaiBaseController
{
    private readonly ILedgerService _ledger;
    private readonly PlanGuard _planGuard;
    private readonly AppDbContext _db;

    public LedgerController(ILedgerService ledger, PlanGuard planGuard, AppDbContext db) { _ledger = ledger; _planGuard = planGuard; _db = db; }

    [HttpPost("receivables")]
    [RequirePermission(Permission.ManageDebts)]
    public async Task<ActionResult<ApiResponse<LedgerEntryDto>>> CreateReceivable([FromBody] CreateReceivableRequest request)
    {
        var (allowed, err) = await _planGuard.CheckFeatureAsync(BusinessId, "ledger");
        if (!allowed) return BadRequest(ApiResponse<LedgerEntryDto>.Fail(err!));
        var user = await _db.Users.FindAsync(UserId);
        var result = await _ledger.CreateReceivableAsync(BusinessId, request, "Manual", user?.Id, user?.FullName);
        return Ok(ApiResponse<LedgerEntryDto>.Ok(result, "Receivable recorded."));
    }

    [HttpPost("payables")]
    [RequirePermission(Permission.ManageDebts)]
    public async Task<ActionResult<ApiResponse<LedgerEntryDto>>> CreatePayable([FromBody] CreatePayableRequest request)
    {
        var (allowed, err) = await _planGuard.CheckFeatureAsync(BusinessId, "ledger");
        if (!allowed) return BadRequest(ApiResponse<LedgerEntryDto>.Fail(err!));
        var user = await _db.Users.FindAsync(UserId);
        var result = await _ledger.CreatePayableAsync(BusinessId, request, "Manual", user?.Id, user?.FullName);
        return Ok(ApiResponse<LedgerEntryDto>.Ok(result, "Payable recorded."));
    }

    [HttpPost("payments")]
    [RequirePermission(Permission.ManageDebts)]
    public async Task<ActionResult<ApiResponse<LedgerEntryDto>>> RecordPayment([FromBody] RecordPaymentRequest request)
    {
        var user = await _db.Users.FindAsync(UserId);
        var result = await _ledger.RecordPaymentAsync(BusinessId, request, Common.EntrySource.Dashboard, user?.Id, user?.FullName);
        return Ok(ApiResponse<LedgerEntryDto>.Ok(result, "Payment recorded."));
    }

    [HttpPut("entries/{id:guid}")]
    [RequirePermission(Permission.ManageDebts)]
    public async Task<ActionResult<ApiResponse<LedgerEntryDto>>> UpdateEntry(Guid id, [FromBody] UpdateLedgerEntryRequest request)
    {
        var result = await _ledger.UpdateEntryAsync(BusinessId, id, request);
        return Ok(ApiResponse<LedgerEntryDto>.Ok(result, "Entry updated."));
    }

    [HttpDelete("entries/{id:guid}")]
    [RequirePermission(Permission.ManageDebts)]
    public async Task<ActionResult<ApiResponse<object>>> DeleteEntry(Guid id)
    {
        await _ledger.DeleteEntryAsync(BusinessId, id);
        return Ok(ApiResponse<object>.Ok(null!, "Entry deleted."));
    }

    [HttpGet("balances")]
    [RequirePermission(Permission.ViewOwnReports)]
    public async Task<ActionResult<ApiResponse<List<OutstandingBalanceDto>>>> GetBalances([FromQuery] string? type = null)
    {
        var result = await _ledger.GetOutstandingBalancesAsync(BusinessId, type);
        return Ok(ApiResponse<List<OutstandingBalanceDto>>.Ok(result));
    }
}
