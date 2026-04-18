using BizPilot.API.Common;
using BizPilot.API.Data;
using BizPilot.API.DTOs.Ledger;
using BizPilot.API.Models;
using BizPilot.API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BizPilot.API.Services;

public class LedgerService : ILedgerService
{
    private readonly AppDbContext _db;

    public LedgerService(AppDbContext db) => _db = db;

    public async Task<LedgerEntryDto> CreateReceivableAsync(Guid businessId, CreateReceivableRequest request, string source = "Manual", Guid? recordedByUserId = null, string? recordedByName = null)
    {
        await EnsureContactExistsAsync(businessId, request.ContactId);

        var entry = new LedgerEntry
        {
            BusinessId = businessId,
            ContactId = request.ContactId,
            EntryType = LedgerEntryType.Receivable,
            Amount = request.Amount,
            Notes = request.Notes,
            DueDate = request.DueDate,
            Source = source,
            RecordedByUserId = recordedByUserId,
            RecordedByName = recordedByName
        };
        _db.LedgerEntries.Add(entry);
        await _db.SaveChangesAsync();
        return await ToDtoAsync(entry);
    }

    public async Task<LedgerEntryDto> CreatePayableAsync(Guid businessId, CreatePayableRequest request, string source = "Manual", Guid? recordedByUserId = null, string? recordedByName = null)
    {
        await EnsureContactExistsAsync(businessId, request.ContactId);

        var entry = new LedgerEntry
        {
            BusinessId = businessId,
            ContactId = request.ContactId,
            EntryType = LedgerEntryType.Payable,
            Amount = request.Amount,
            Notes = request.Notes,
            DueDate = request.DueDate,
            Source = source,
            RecordedByUserId = recordedByUserId,
            RecordedByName = recordedByName
        };
        _db.LedgerEntries.Add(entry);
        await _db.SaveChangesAsync();
        return await ToDtoAsync(entry);
    }

    public async Task<LedgerEntryDto> RecordPaymentAsync(Guid businessId, RecordPaymentRequest request, string source = "Manual", Guid? recordedByUserId = null, string? recordedByName = null)
    {
        await EnsureContactExistsAsync(businessId, request.ContactId);

        var entryType = request.PaymentType.ToLowerInvariant() switch
        {
            "receivable" => LedgerEntryType.ReceivablePayment,
            "payable" => LedgerEntryType.PayablePayment,
            _ => throw new ArgumentException($"Invalid payment type '{request.PaymentType}'. Use 'receivable' or 'payable'.")
        };

        var entry = new LedgerEntry
        {
            BusinessId = businessId,
            ContactId = request.ContactId,
            EntryType = entryType,
            Amount = request.Amount,
            Notes = request.Notes,
            Source = source,
            RecordedByUserId = recordedByUserId,
            RecordedByName = recordedByName
        };
        _db.LedgerEntries.Add(entry);
        await _db.SaveChangesAsync();
        return await ToDtoAsync(entry);
    }

    public async Task<List<OutstandingBalanceDto>> GetOutstandingBalancesAsync(Guid businessId, string? type)
    {
        var query = _db.LedgerEntries
            .Include(e => e.Contact)
            .Where(e => e.BusinessId == businessId);

        var grouped = await query
            .GroupBy(e => new { e.ContactId, e.Contact.Name, e.Contact.Type })
            .Select(g => new OutstandingBalanceDto
            {
                ContactId = g.Key.ContactId,
                ContactName = g.Key.Name,
                ContactType = g.Key.Type.ToString(),
                TotalReceivable = g.Where(e => e.EntryType == LedgerEntryType.Receivable).Sum(e => e.Amount)
                                - g.Where(e => e.EntryType == LedgerEntryType.ReceivablePayment).Sum(e => e.Amount),
                TotalPayable = g.Where(e => e.EntryType == LedgerEntryType.Payable).Sum(e => e.Amount)
                             - g.Where(e => e.EntryType == LedgerEntryType.PayablePayment).Sum(e => e.Amount)
            })
            .ToListAsync();

        foreach (var b in grouped)
            b.NetBalance = b.TotalReceivable - b.TotalPayable;

        var contactIds = grouped.Select(g => g.ContactId).ToList();
        var recentNotes = await _db.LedgerEntries
            .Where(e => e.BusinessId == businessId && contactIds.Contains(e.ContactId)
                && e.Notes != null && (e.EntryType == LedgerEntryType.Receivable || e.EntryType == LedgerEntryType.Payable))
            .OrderByDescending(e => e.CreatedAtUtc)
            .ToListAsync();

        var notesByContact = recentNotes
            .GroupBy(e => e.ContactId)
            .ToDictionary(g => g.Key, g => g.Take(3).Select(e => e.Notes!).ToList());

        foreach (var b in grouped)
            b.RecentNotes = notesByContact.GetValueOrDefault(b.ContactId, new List<string>());

        if (!string.IsNullOrWhiteSpace(type))
        {
            grouped = type.ToLowerInvariant() switch
            {
                "receivable" => grouped.Where(b => b.TotalReceivable > 0).ToList(),
                "payable" => grouped.Where(b => b.TotalPayable > 0).ToList(),
                _ => grouped
            };
        }

        return grouped.Where(b => b.TotalReceivable > 0 || b.TotalPayable > 0).ToList();
    }

    public async Task<List<LedgerEntryDto>> GetContactLedgerAsync(Guid businessId, Guid contactId)
    {
        var entries = await _db.LedgerEntries
            .Include(e => e.Contact)
            .Where(e => e.BusinessId == businessId && e.ContactId == contactId)
            .OrderByDescending(e => e.CreatedAtUtc)
            .ToListAsync();

        return entries.Select(e => new LedgerEntryDto
        {
            Id = e.Id,
            ContactName = e.Contact.Name,
            EntryType = e.EntryType.ToString(),
            Amount = e.Amount,
            Notes = e.Notes,
            DueDate = e.DueDate,
            Source = e.Source,
            CreatedAtUtc = e.CreatedAtUtc
        }).ToList();
    }

    public async Task<LedgerEntryDto> UpdateEntryAsync(Guid businessId, Guid entryId, UpdateLedgerEntryRequest request)
    {
        var entry = await _db.LedgerEntries
            .Include(e => e.Contact)
            .FirstOrDefaultAsync(e => e.Id == entryId && e.BusinessId == businessId)
            ?? throw new KeyNotFoundException("Ledger entry not found.");

        // Reject edits on adjustment entries — they're system-generated corrections, not user data
        if (entry.Source == "Adjustment")
            throw new InvalidOperationException("Adjustment entries cannot be edited. Edit the original debt instead.");

        // Compute the CURRENT outstanding balance for this contact (not just this entry's amount),
        // so repeated adjustments stack correctly. Without this, adjusting 100k→200k→300k would
        // create deltas from the original 100k each time instead of from the running total.
        var isReceivable = entry.EntryType == LedgerEntryType.Receivable;
        var debtType = isReceivable ? LedgerEntryType.Receivable : LedgerEntryType.Payable;
        var payType = isReceivable ? LedgerEntryType.ReceivablePayment : LedgerEntryType.PayablePayment;

        var allEntries = await _db.LedgerEntries
            .Where(e => e.BusinessId == businessId && e.ContactId == entry.ContactId
                        && (e.EntryType == debtType || e.EntryType == payType))
            .ToListAsync();

        var currentBalance = allEntries.Where(e => e.EntryType == debtType).Sum(e => e.Amount)
                           - allEntries.Where(e => e.EntryType == payType).Sum(e => e.Amount);

        var delta = request.Amount - currentBalance;
        if (delta == 0)
        {
            if (request.Notes != null && request.Notes != entry.Notes) { entry.Notes = request.Notes; await _db.SaveChangesAsync(); }
            return await ToDtoAsync(entry);
        }

        var adjustmentType = delta > 0 ? debtType : payType;

        var adjustmentEntry = new LedgerEntry
        {
            BusinessId = businessId,
            ContactId = entry.ContactId,
            EntryType = adjustmentType,
            Amount = Math.Abs(delta),
            Notes = $"Adjusted {entry.Contact.Name}: ₦{currentBalance:N0} → ₦{request.Amount:N0}",
            Source = "Adjustment",
            RecordedByUserId = entry.RecordedByUserId,
            RecordedByName = entry.RecordedByName
        };
        _db.LedgerEntries.Add(adjustmentEntry);

        if (request.Notes != null) entry.Notes = request.Notes;
        await _db.SaveChangesAsync();
        return await ToDtoAsync(adjustmentEntry);
    }

    public async Task DeleteEntryAsync(Guid businessId, Guid entryId)
    {
        var entry = await _db.LedgerEntries
            .FirstOrDefaultAsync(e => e.Id == entryId && e.BusinessId == businessId)
            ?? throw new KeyNotFoundException("Ledger entry not found.");

        // Create a reversal entry so the deletion is visible in the activity feed and
        // the balance is properly zeroed. The reversal type cancels out the original:
        // Receivable → ReceivablePayment, Payable → PayablePayment, and vice versa.
        var reversalType = entry.EntryType switch
        {
            LedgerEntryType.Receivable => LedgerEntryType.ReceivablePayment,
            LedgerEntryType.Payable => LedgerEntryType.PayablePayment,
            LedgerEntryType.ReceivablePayment => LedgerEntryType.Receivable,
            LedgerEntryType.PayablePayment => LedgerEntryType.Payable,
            _ => entry.EntryType
        };

        _db.LedgerEntries.Add(new LedgerEntry
        {
            BusinessId = businessId,
            ContactId = entry.ContactId,
            EntryType = reversalType,
            Amount = entry.Amount,
            Notes = $"Deleted: {entry.Notes ?? entry.EntryType.ToString()} (₦{entry.Amount:N0})",
            Source = "Adjustment",
            RecordedByUserId = entry.RecordedByUserId,
            RecordedByName = entry.RecordedByName
        });

        _db.LedgerEntries.Remove(entry);
        await _db.SaveChangesAsync();
    }

    private async Task EnsureContactExistsAsync(Guid businessId, Guid contactId)
    {
        var exists = await _db.Contacts.AnyAsync(c => c.Id == contactId && c.BusinessId == businessId);
        if (!exists) throw new KeyNotFoundException("Contact not found.");
    }

    private async Task<LedgerEntryDto> ToDtoAsync(LedgerEntry entry)
    {
        var contact = await _db.Contacts.FindAsync(entry.ContactId);
        return new LedgerEntryDto
        {
            Id = entry.Id,
            ContactName = contact?.Name ?? string.Empty,
            EntryType = entry.EntryType.ToString(),
            Amount = entry.Amount,
            Notes = entry.Notes,
            DueDate = entry.DueDate,
            Source = entry.Source,
            CreatedAtUtc = entry.CreatedAtUtc
        };
    }
}
