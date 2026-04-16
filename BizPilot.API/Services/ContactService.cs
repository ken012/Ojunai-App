using BizPilot.API.Common;
using BizPilot.API.Data;
using BizPilot.API.DTOs.Contacts;
using BizPilot.API.Models;
using BizPilot.API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BizPilot.API.Services;

public class ContactService : IContactService
{
    private readonly AppDbContext _db;

    public ContactService(AppDbContext db) => _db = db;

    public async Task<PaginatedResult<ContactDto>> GetAllAsync(
        Guid businessId, int page, int pageSize, string? search, string? type)
    {
        var query = _db.Contacts.Where(c => c.BusinessId == businessId);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(c => c.Name.Contains(search) || (c.PhoneNumber != null && c.PhoneNumber.Contains(search)));

        if (!string.IsNullOrWhiteSpace(type) && Enum.TryParse<ContactType>(type, true, out var ct))
            query = query.Where(c => c.Type == ct);

        var total = await query.CountAsync();

        var contacts = await query
            .OrderBy(c => c.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var contactIds = contacts.Select(c => c.Id).ToList();

        var balances = await _db.LedgerEntries
            .Where(e => e.BusinessId == businessId && contactIds.Contains(e.ContactId))
            .GroupBy(e => e.ContactId)
            .Select(g => new
            {
                ContactId = g.Key,
                Receivable = g.Where(e => e.EntryType == LedgerEntryType.Receivable).Sum(e => e.Amount)
                           - g.Where(e => e.EntryType == LedgerEntryType.ReceivablePayment).Sum(e => e.Amount),
                Payable = g.Where(e => e.EntryType == LedgerEntryType.Payable).Sum(e => e.Amount)
                        - g.Where(e => e.EntryType == LedgerEntryType.PayablePayment).Sum(e => e.Amount)
            })
            .ToDictionaryAsync(x => x.ContactId);

        var items = contacts.Select(c =>
        {
            balances.TryGetValue(c.Id, out var bal);
            return new ContactDto
            {
                Id = c.Id,
                Name = c.Name,
                PhoneNumber = c.PhoneNumber,
                Type = c.Type.ToString(),
                OutstandingReceivable = bal?.Receivable ?? 0,
                OutstandingPayable = bal?.Payable ?? 0,
                CreatedAtUtc = c.CreatedAtUtc
            };
        }).ToList();

        return new PaginatedResult<ContactDto> { Items = items, TotalCount = total, Page = page, PageSize = pageSize };
    }

    public async Task<ContactDto> GetByIdAsync(Guid businessId, Guid contactId)
    {
        var contact = await _db.Contacts
            .FirstOrDefaultAsync(c => c.Id == contactId && c.BusinessId == businessId)
            ?? throw new KeyNotFoundException("Contact not found.");

        var ledger = await _db.LedgerEntries
            .Where(e => e.ContactId == contactId && e.BusinessId == businessId)
            .ToListAsync();

        var receivable = ledger.Where(e => e.EntryType == LedgerEntryType.Receivable).Sum(e => e.Amount)
                       - ledger.Where(e => e.EntryType == LedgerEntryType.ReceivablePayment).Sum(e => e.Amount);
        var payable = ledger.Where(e => e.EntryType == LedgerEntryType.Payable).Sum(e => e.Amount)
                   - ledger.Where(e => e.EntryType == LedgerEntryType.PayablePayment).Sum(e => e.Amount);

        return new ContactDto
        {
            Id = contact.Id,
            Name = contact.Name,
            PhoneNumber = contact.PhoneNumber,
            Type = contact.Type.ToString(),
            OutstandingReceivable = receivable,
            OutstandingPayable = payable,
            CreatedAtUtc = contact.CreatedAtUtc
        };
    }

    public async Task<ContactDto> CreateAsync(Guid businessId, CreateContactRequest request)
    {
        var contact = new Contact
        {
            BusinessId = businessId,
            Name = request.Name,
            PhoneNumber = request.PhoneNumber,
            Type = request.Type
        };
        _db.Contacts.Add(contact);
        await _db.SaveChangesAsync();

        return new ContactDto
        {
            Id = contact.Id,
            Name = contact.Name,
            PhoneNumber = contact.PhoneNumber,
            Type = contact.Type.ToString(),
            OutstandingReceivable = 0,
            OutstandingPayable = 0,
            CreatedAtUtc = contact.CreatedAtUtc
        };
    }

    public async Task<ContactDto> UpdateAsync(Guid businessId, Guid contactId, CreateContactRequest request)
    {
        var contact = await _db.Contacts
            .FirstOrDefaultAsync(c => c.Id == contactId && c.BusinessId == businessId)
            ?? throw new KeyNotFoundException("Contact not found.");

        contact.Name = request.Name;
        contact.PhoneNumber = request.PhoneNumber;
        contact.Type = request.Type;

        await _db.SaveChangesAsync();
        return await GetByIdAsync(businessId, contactId);
    }
}
