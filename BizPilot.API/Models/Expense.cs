namespace BizPilot.API.Models;

public class Expense
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BusinessId { get; set; }
    public string Category { get; set; } = "General";
    public string ExpenseType { get; set; } = "operating"; // "operating" or "cogs"
    public decimal Amount { get; set; }
    public string? Notes { get; set; }
    public string? PaidTo { get; set; }
    public string Source { get; set; } = "Manual";
    public Guid? RecordedByUserId { get; set; }
    public string? RecordedByName { get; set; }
    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Business Business { get; set; } = null!;
}
