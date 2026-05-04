using System.ComponentModel.DataAnnotations;

namespace Ojunai.API.DTOs.Ledger;

public class CreateReceivableRequest
{
    [Required] public Guid ContactId { get; set; }
    [Range(0.01, 999999999)] public decimal Amount { get; set; }
    [MaxLength(500)] public string? Notes { get; set; }
    public DateTime? DueDate { get; set; }
}

public class CreatePayableRequest
{
    [Required] public Guid ContactId { get; set; }
    [Range(0.01, 999999999)] public decimal Amount { get; set; }
    [MaxLength(500)] public string? Notes { get; set; }
    public DateTime? DueDate { get; set; }
}

public class RecordPaymentRequest
{
    [Required] public Guid ContactId { get; set; }
    [Range(0.01, 999999999)] public decimal Amount { get; set; }
    [Required, MaxLength(20)] public string PaymentType { get; set; } = string.Empty;
    [MaxLength(500)] public string? Notes { get; set; }
}

public class LedgerEntryDto
{
    public Guid Id { get; set; }
    public string ContactName { get; set; } = string.Empty;
    public string EntryType { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Notes { get; set; }
    public DateTime? DueDate { get; set; }
    public string Source { get; set; } = "Manual";
    public DateTime CreatedAtUtc { get; set; }
}

public class UpdateLedgerEntryRequest
{
    [Range(0.01, 999999999)] public decimal Amount { get; set; }
    [MaxLength(500)] public string? Notes { get; set; }
}

public class OutstandingBalanceDto
{
    public Guid ContactId { get; set; }
    public string ContactName { get; set; } = string.Empty;
    public string ContactType { get; set; } = string.Empty;
    public decimal TotalReceivable { get; set; }
    public decimal TotalPayable { get; set; }
    public decimal NetBalance { get; set; }
    public List<string> RecentNotes { get; set; } = new();
}
