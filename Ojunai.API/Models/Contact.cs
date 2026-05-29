namespace Ojunai.API.Models;

public class Contact
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

    public Business Business { get; set; } = null!;
    public ICollection<LedgerEntry> LedgerEntries { get; set; } = new List<LedgerEntry>();
    public ICollection<Sale> Sales { get; set; } = new List<Sale>();
}

public enum ContactType { Customer = 1, Supplier = 2, Both = 3 }
