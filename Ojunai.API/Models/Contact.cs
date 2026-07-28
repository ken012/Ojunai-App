namespace Ojunai.API.Models;

public class Contact : ILocationScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BusinessId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    /// <summary>Optional email — used to pre-fill the "Email receipt" dialog and any future email-based contact features.</summary>
    public string? Email { get; set; }
    public ContactType Type { get; set; } = ContactType.Customer;
    public string Source { get; set; } = "Manual";
    public Guid? ImportBatchId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Multi-location: the branch a contact was CREATED at (business-wide master data, but tagged with its
    // origin branch so the UI can show "created at X"). Stamped on insert from the ambient LocationScope via
    // ILocationScoped when the business is multi-location; null = business-wide / single-location. Existing
    // rows are backfilled to the default location. Contacts are NOT filtered by branch — the whole list stays
    // visible everywhere — this only drives the origin-branch label.
    public Guid? LocationId { get; set; }

    public Business Business { get; set; } = null!;
    public ICollection<LedgerEntry> LedgerEntries { get; set; } = new List<LedgerEntry>();
    public ICollection<Sale> Sales { get; set; } = new List<Sale>();
}

public enum ContactType { Customer = 1, Supplier = 2, Both = 3 }
