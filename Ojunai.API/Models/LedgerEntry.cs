namespace Ojunai.API.Models;

public class LedgerEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BusinessId { get; set; }
    /// <summary>Location of the transaction this entry came from (multi-location; null = default/legacy). Additive Phase 0.</summary>
    public Guid? LocationId { get; set; }
    public Guid ContactId { get; set; }
    public LedgerEntryType EntryType { get; set; }
    public decimal Amount { get; set; }
    public string? Notes { get; set; }
    public DateTime? DueDate { get; set; }
    public string Source { get; set; } = "Manual";
    public Guid? ImportBatchId { get; set; }
    public Guid? RecordedByUserId { get; set; }
    public string? RecordedByName { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Business Business { get; set; } = null!;
    public Contact Contact { get; set; } = null!;
}

public enum LedgerEntryType
{
    Receivable = 1,
    ReceivablePayment = 2,
    Payable = 3,
    PayablePayment = 4
}
