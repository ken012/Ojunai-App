namespace BizPilot.API.Models;

public class MessageLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? BusinessId { get; set; }
    public Guid? UserId { get; set; }
    public string? WhatsAppMessageId { get; set; }
    public MessageDirection Direction { get; set; }
    public string Channel { get; set; } = "WhatsApp";
    public string RawMessage { get; set; } = string.Empty;
    public string? ParsedIntent { get; set; }
    public string? ParsedPayloadJson { get; set; }
    public decimal? ConfidenceScore { get; set; }
    public MessageProcessingStatus ProcessingStatus { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public enum MessageDirection { Inbound = 1, Outbound = 2 }

public enum MessageProcessingStatus
{
    Received = 1,
    Parsed = 2,
    Executed = 3,
    NeedsClarification = 4,
    Failed = 5
}
