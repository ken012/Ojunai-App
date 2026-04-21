namespace BizPilot.API.Models;

/// <summary>
/// Tracks an async CSV import job running in the background via Hangfire.
/// Raw CSV text is persisted on the row so the Hangfire worker is self-contained — it doesn't need the
/// original HTTP request context or a file on disk. RawCsvText is cleared once the job completes to
/// keep the table small.
/// </summary>
public class ImportJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BusinessId { get; set; }
    public Guid UserId { get; set; }
    public ImportJobType Type { get; set; }
    public ImportJobStatus Status { get; set; } = ImportJobStatus.Queued;

    public string? RawCsvText { get; set; }
    public string FileName { get; set; } = string.Empty;

    public int TotalRows { get; set; }
    public int ProcessedRows { get; set; }
    public int SuccessCount { get; set; }
    public int ErrorCount { get; set; }

    public string? ErrorsJson { get; set; }
    public string? FailureReason { get; set; }

    public bool SkipExpenses { get; set; }
    public string ImportMode { get; set; } = "default";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    public Business Business { get; set; } = null!;
}

public enum ImportJobType
{
    Inventory = 1,
    Sales = 2,
    Expenses = 3,
    Contacts = 4,
    ContactsWithLedger = 5
}

public enum ImportJobStatus
{
    Queued = 1,
    Running = 2,
    Completed = 3,
    Failed = 4,
    RolledBack = 5
}
