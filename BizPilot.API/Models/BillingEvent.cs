namespace BizPilot.API.Models;

public class BillingEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BusinessId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string? Plan { get; set; }
    public string? BillingCycle { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public string? TransactionRef { get; set; }
    public string? SubscriptionId { get; set; }
    public string? PaymentMethod { get; set; }
    public string? Status { get; set; }
    public string? ErrorDetails { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
