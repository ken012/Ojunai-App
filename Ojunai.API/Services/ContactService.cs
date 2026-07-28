using Ojunai.API.Common;
using Ojunai.API.Data;
using Ojunai.API.DTOs.Contacts;
using Ojunai.API.Models;
using Ojunai.API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Services;

public class ContactService : IContactService
{
    private readonly AppDbContext _db;
    private readonly IActivityLogger _activity;
    private readonly LocationStockService _locStock;

    public ContactService(AppDbContext db, IActivityLogger activity, LocationStockService locStock)
    {
        _db = db;
        _activity = activity;
        _locStock = locStock;
    }

    /// <summary>
    /// Per-branch balance labels. The contact LIST stays business-wide (contacts are master data visible at
    /// every branch); this only resolves the origin-branch NAME to show per contact. Returns null for
    /// single-location businesses (nothing to label). Falls back a null contact-location to the default branch
    /// (e.g. contacts created under "All locations").
    /// </summary>
    private async Task<(Dictionary<Guid, string> Names, Guid? DefaultId)?> BranchLabelsAsync(Guid businessId)
    {
        var locs = await _db.Locations
            .Where(l => l.BusinessId == businessId && l.IsActive)
            .Select(l => new { l.Id, l.Name, l.IsDefault })
            .ToListAsync();
        if (locs.Count <= 1) return null; // single-location → no branch label
        return (locs.ToDictionary(l => l.Id, l => l.Name), locs.FirstOrDefault(l => l.IsDefault)?.Id);
    }

    private static string? LabelFor(Guid? contactLocationId, (Dictionary<Guid, string> Names, Guid? DefaultId)? labels)
    {
        if (labels is null) return null; // single-location
        var loc = contactLocationId ?? labels.Value.DefaultId;
        return loc is { } l && labels.Value.Names.TryGetValue(l, out var name) ? name : null;
    }

    public async Task<PaginatedResult<ContactDto>> GetAllAsync(
        Guid businessId, int page, int pageSize, string? search, string? type, string? balance)
    {
        // ── Step 1: filtered IDs (handles search + type + balance uniformly) ──
        // Balance filter requires aggregating LedgerEntries per contact, so we can't push
        // it down into a simple WHERE clause. Strategy: compute the per-contact balances
        // for the search+type-narrowed set, filter by the balance condition, paginate,
        // then hydrate ContactDto rows.
        var filteredIds = await GetFilteredContactIdsAsync(businessId, search, type, balance);

        var total = filteredIds.Count;
        var pageIds = filteredIds
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToHashSet();

        var contacts = await _db.Contacts
            .Where(c => c.BusinessId == businessId && pageIds.Contains(c.Id))
            .OrderBy(c => c.Name)
            .ToListAsync();

        // Balances scope to the selected branch (locId null = single-location / All → unchanged, business-wide).
        var locId = await _locStock.SelectedLocationForAsync(businessId);
        var labels = await BranchLabelsAsync(businessId);
        var balances = await _db.LedgerEntries
            .Where(e => e.BusinessId == businessId && pageIds.Contains(e.ContactId) && (locId == null || e.LocationId == locId))
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
                Email = c.Email,
                Type = c.Type.ToString(),
                OutstandingReceivable = bal?.Receivable ?? 0,
                OutstandingPayable = bal?.Payable ?? 0,
                CreatedAtUtc = c.CreatedAtUtc,
                CreatedAtBranch = LabelFor(c.LocationId, labels)
            };
        }).ToList();

        return new PaginatedResult<ContactDto> { Items = items, TotalCount = total, Page = page, PageSize = pageSize };
    }

    public async Task<ContactTotalsDto> GetTotalsAsync(Guid businessId, string? search, string? type, string? balance)
    {
        var filteredIds = await GetFilteredContactIdsAsync(businessId, search, type, balance);
        if (filteredIds.Count == 0)
            return new ContactTotalsDto { TotalContacts = 0, TotalReceivable = 0, TotalPayable = 0 };

        var idSet = filteredIds.ToHashSet();
        var totalsLocId = await _locStock.SelectedLocationForAsync(businessId);
        var balances = await _db.LedgerEntries
            .Where(e => e.BusinessId == businessId && idSet.Contains(e.ContactId) && (totalsLocId == null || e.LocationId == totalsLocId))
            .GroupBy(e => e.ContactId)
            .Select(g => new
            {
                Receivable = g.Where(e => e.EntryType == LedgerEntryType.Receivable).Sum(e => e.Amount)
                           - g.Where(e => e.EntryType == LedgerEntryType.ReceivablePayment).Sum(e => e.Amount),
                Payable = g.Where(e => e.EntryType == LedgerEntryType.Payable).Sum(e => e.Amount)
                        - g.Where(e => e.EntryType == LedgerEntryType.PayablePayment).Sum(e => e.Amount),
            })
            .ToListAsync();

        return new ContactTotalsDto
        {
            TotalContacts = filteredIds.Count,
            TotalReceivable = balances.Sum(b => Math.Max(0m, b.Receivable)),
            TotalPayable = balances.Sum(b => Math.Max(0m, b.Payable)),
        };
    }

    /// <summary>
    /// Resolves the set of contact IDs matching the search + type + balance filters, ordered
    /// by name. Shared by <see cref="GetAllAsync"/> (for pagination) and <see cref="GetTotalsAsync"/>
    /// (for aggregates). Balance filtering joins on LedgerEntries so it can't be pushed into
    /// the simple WHERE; we materialize the narrowed set first and filter in-memory.
    /// </summary>
    private async Task<List<Guid>> GetFilteredContactIdsAsync(Guid businessId, string? search, string? type, string? balance)
    {
        var query = _db.Contacts.Where(c => c.BusinessId == businessId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            // Prefix match via PostgreSQL's ILIKE — users expect "ada" to find "Ada Beauty"
            // without minding capitalization. Uses IX_Contacts_BusinessId_NameLower functional
            // index for fast lookups even at large contact counts.
            query = query.Where(c => EF.Functions.ILike(c.Name, $"{search}%")
                                  || (c.PhoneNumber != null && c.PhoneNumber.Contains(search)));
        }

        if (!string.IsNullOrWhiteSpace(type) && Enum.TryParse<ContactType>(type, true, out var ct))
            query = query.Where(c => c.Type == ct);

        // Without a balance filter we can paginate purely from the index — fast path.
        if (string.IsNullOrWhiteSpace(balance))
            return await query.OrderBy(c => c.Name).Select(c => c.Id).ToListAsync();

        // Balance filter: pull candidate IDs + their balances, then narrow.
        var candidateIds = await query.OrderBy(c => c.Name).Select(c => c.Id).ToListAsync();
        if (candidateIds.Count == 0) return candidateIds;

        // The balance FILTER ("who owes me" etc.) respects the selected branch too, so filtering by balance on
        // Branch B matches on B's debts. The base list (no balance filter) above stays the full business-wide set.
        var filterLocId = await _locStock.SelectedLocationForAsync(businessId);
        var balances = await _db.LedgerEntries
            .Where(e => e.BusinessId == businessId && candidateIds.Contains(e.ContactId) && (filterLocId == null || e.LocationId == filterLocId))
            .GroupBy(e => e.ContactId)
            .Select(g => new
            {
                ContactId = g.Key,
                Receivable = g.Where(e => e.EntryType == LedgerEntryType.Receivable).Sum(e => e.Amount)
                           - g.Where(e => e.EntryType == LedgerEntryType.ReceivablePayment).Sum(e => e.Amount),
                Payable = g.Where(e => e.EntryType == LedgerEntryType.Payable).Sum(e => e.Amount)
                        - g.Where(e => e.EntryType == LedgerEntryType.PayablePayment).Sum(e => e.Amount),
            })
            .ToDictionaryAsync(x => x.ContactId);

        bool Matches(Guid id) => balance.ToLowerInvariant() switch
        {
            "receivable" => balances.TryGetValue(id, out var b) && (b.Receivable > 0 || b.Payable < 0),
            "payable" => balances.TryGetValue(id, out var b) && (b.Payable > 0 || b.Receivable < 0),
            "settled" => !balances.TryGetValue(id, out var b) || (b.Receivable == 0 && b.Payable == 0),
            _ => true,
        };

        // Preserve name ordering from the candidate query while applying the balance filter.
        return candidateIds.Where(Matches).ToList();
    }

    public async Task<ContactDto> GetByIdAsync(Guid businessId, Guid contactId)
    {
        var contact = await _db.Contacts
            .FirstOrDefaultAsync(c => c.Id == contactId && c.BusinessId == businessId)
            ?? throw new KeyNotFoundException("Contact not found.");

        var locId = await _locStock.SelectedLocationForAsync(businessId);
        var ledger = await _db.LedgerEntries
            .Where(e => e.ContactId == contactId && e.BusinessId == businessId && (locId == null || e.LocationId == locId))
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
            Email = contact.Email,
            Type = contact.Type.ToString(),
            OutstandingReceivable = receivable,
            OutstandingPayable = payable,
            CreatedAtUtc = contact.CreatedAtUtc,
            CreatedAtBranch = LabelFor(contact.LocationId, await BranchLabelsAsync(businessId))
        };
    }

    public async Task<ContactDto> CreateAsync(Guid businessId, CreateContactRequest request)
    {
        var contact = new Contact
        {
            BusinessId = businessId,
            Name = request.Name,
            PhoneNumber = request.PhoneNumber,
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim().ToLowerInvariant(),
            Type = request.Type
        };
        _db.Contacts.Add(contact);
        await _activity.LogAsync(businessId, "contact.created", "Contact", contact.Id, contact.Name,
            $"added contact “{contact.Name}”");
        await _db.SaveChangesAsync(); // central stamp sets contact.LocationId from the ambient branch (if multi-location)

        return new ContactDto
        {
            Id = contact.Id,
            Name = contact.Name,
            PhoneNumber = contact.PhoneNumber,
            Email = contact.Email,
            Type = contact.Type.ToString(),
            OutstandingReceivable = 0,
            OutstandingPayable = 0,
            CreatedAtUtc = contact.CreatedAtUtc,
            CreatedAtBranch = LabelFor(contact.LocationId, await BranchLabelsAsync(businessId))
        };
    }

    public async Task<ContactDto> UpdateAsync(Guid businessId, Guid contactId, CreateContactRequest request)
    {
        var contact = await _db.Contacts
            .FirstOrDefaultAsync(c => c.Id == contactId && c.BusinessId == businessId)
            ?? throw new KeyNotFoundException("Contact not found.");

        contact.Name = request.Name;
        contact.PhoneNumber = request.PhoneNumber;
        contact.Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim().ToLowerInvariant();
        contact.Type = request.Type;

        await _activity.LogAsync(businessId, "contact.updated", "Contact", contact.Id, contact.Name,
            $"edited contact “{contact.Name}”");
        await _db.SaveChangesAsync();
        return await GetByIdAsync(businessId, contactId);
    }

    public async Task DeleteAsync(Guid businessId, Guid contactId)
    {
        var contact = await _db.Contacts
            .FirstOrDefaultAsync(c => c.Id == contactId && c.BusinessId == businessId)
            ?? throw new KeyNotFoundException("Contact not found.");

        // Nullify FK references on sales so the delete doesn't cascade or fail.
        // The sale records stay — they just lose the contact link.
        var linkedSales = await _db.Sales
            .Where(s => s.ContactId == contactId && s.BusinessId == businessId)
            .ToListAsync();
        foreach (var sale in linkedSales) sale.ContactId = null;

        // Remove ledger entries for this contact (debts are meaningless without the contact).
        var ledgerEntries = await _db.LedgerEntries
            .Where(e => e.ContactId == contactId && e.BusinessId == businessId)
            .ToListAsync();
        _db.LedgerEntries.RemoveRange(ledgerEntries);

        var contactName = contact.Name;
        _db.Contacts.Remove(contact);
        await _activity.LogAsync(businessId, "contact.deleted", "Contact", contact.Id, contactName,
            $"deleted contact “{contactName}”");
        await _db.SaveChangesAsync();
    }
}
