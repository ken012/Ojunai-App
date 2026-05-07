namespace Ojunai.API.DTOs.Alerts;

public class AlertDto
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    /// <summary>
    /// "Business" — about the business overall, shown to Owner/Admin.
    /// "Personal" — about your own account (security/privacy), shown only to you.
    /// </summary>
    public string Scope { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? LinkUrl { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ReadAtUtc { get; set; }
}

public class UnreadCountResponse
{
    public int Count { get; set; }
}
