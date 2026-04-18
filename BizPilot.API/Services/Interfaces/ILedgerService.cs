using BizPilot.API.Common;
using BizPilot.API.DTOs.Ledger;

namespace BizPilot.API.Services.Interfaces;

public interface ILedgerService
{
    Task<LedgerEntryDto> CreateReceivableAsync(Guid businessId, CreateReceivableRequest request, string source = "Manual", Guid? recordedByUserId = null, string? recordedByName = null);
    Task<LedgerEntryDto> CreatePayableAsync(Guid businessId, CreatePayableRequest request, string source = "Manual", Guid? recordedByUserId = null, string? recordedByName = null);
    Task<LedgerEntryDto> RecordPaymentAsync(Guid businessId, RecordPaymentRequest request, string source = "Manual", Guid? recordedByUserId = null, string? recordedByName = null);
    Task<List<OutstandingBalanceDto>> GetOutstandingBalancesAsync(Guid businessId, string? type);
    Task<List<LedgerEntryDto>> GetContactLedgerAsync(Guid businessId, Guid contactId);
    Task<LedgerEntryDto> UpdateEntryAsync(Guid businessId, Guid entryId, UpdateLedgerEntryRequest request);
    Task DeleteEntryAsync(Guid businessId, Guid entryId);
}
