using System.Text;
using System.Text.Json;
using BizPilot.API.Common;
using BizPilot.API.Data;
using BizPilot.API.Jobs;
using BizPilot.API.Models;
using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BizPilot.API.Controllers;

/// <summary>
/// CSV imports are fully async. Each upload is parsed to validate format, persisted as an ImportJob row,
/// and queued for background processing via Hangfire. The endpoint returns the job id immediately so the
/// frontend can poll <c>GET /api/import/jobs/{id}</c> for progress. A WhatsApp message is sent to the
/// owner when the job finishes.
/// </summary>
[Route("api/import")]
public class ImportController : BizPilotBaseController
{
    private readonly AppDbContext _db;
    private readonly PlanGuard _planGuard;
    private readonly IBackgroundJobClient _jobs;
    private readonly ILogger<ImportController> _logger;

    // Raised from 5MB since processing runs in the background and no longer blocks the request.
    private const long MaxFileSize = 50 * 1024 * 1024; // 50MB

    // Async processing removes the HTTP timeout ceiling, so we can accept much larger files. The ceiling
    // here is about protecting memory during the initial parse, not HTTP latency.
    private const int MaxRows = 100_000;

    public ImportController(AppDbContext db, PlanGuard planGuard, IBackgroundJobClient jobs, ILogger<ImportController> logger)
    {
        _db = db;
        _planGuard = planGuard;
        _jobs = jobs;
        _logger = logger;
    }

    [HttpPost("inventory")]
    [RequirePermission(Permission.ManageStock)]
    [RequestSizeLimit(MaxFileSize)]
    public Task<ActionResult<ApiResponse<ImportJobDto>>> ImportInventory(IFormFile file, [FromQuery] string mode = "new_purchase")
        => EnqueueImportAsync(file, ImportJobType.Inventory, Permission.ManageStock, mode);

    [HttpPost("sales")]
    [RequirePermission(Permission.RecordSales)]
    [RequestSizeLimit(MaxFileSize)]
    public Task<ActionResult<ApiResponse<ImportJobDto>>> ImportSales(IFormFile file, [FromQuery] string mode = "new_sales")
        => EnqueueImportAsync(file, ImportJobType.Sales, Permission.RecordSales, mode);

    [HttpPost("expenses")]
    [RequirePermission(Permission.RecordExpenses)]
    [RequestSizeLimit(MaxFileSize)]
    public Task<ActionResult<ApiResponse<ImportJobDto>>> ImportExpenses(IFormFile file)
        => EnqueueImportAsync(file, ImportJobType.Expenses, Permission.RecordExpenses);

    [HttpPost("contacts")]
    [RequirePermission(Permission.ManageDebts)]
    [RequestSizeLimit(MaxFileSize)]
    public Task<ActionResult<ApiResponse<ImportJobDto>>> ImportContacts(IFormFile file)
        => EnqueueImportAsync(file, ImportJobType.Contacts, Permission.ManageDebts);

    [HttpPost("contacts-ledger")]
    [RequirePermission(Permission.ManageDebts)]
    [RequestSizeLimit(MaxFileSize)]
    public Task<ActionResult<ApiResponse<ImportJobDto>>> ImportContactsLedger(IFormFile file, [FromQuery] string mode = "new_debts")
        => EnqueueImportAsync(file, ImportJobType.ContactsWithLedger, Permission.ManageDebts, mode);

    /// <summary>
    /// Polling endpoint. Returns the current status, progress counters, and any errors for a job.
    /// Tenant-scoped: can't read jobs belonging to another business.
    /// </summary>
    [HttpGet("jobs/{id:guid}")]
    public async Task<ActionResult<ApiResponse<ImportJobDto>>> GetJob(Guid id)
    {
        var job = await _db.ImportJobs.AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == id && j.BusinessId == BusinessId);
        if (job == null) return NotFound(ApiResponse<ImportJobDto>.Fail("Import job not found."));

        return Ok(ApiResponse<ImportJobDto>.Ok(MapToDto(job)));
    }

    /// <summary>
    /// Rolls back a completed import by hard-deleting all records created by the import batch:
    /// inventory transactions, products, expenses, ledger entries, and contacts.
    /// </summary>
    [HttpPost("rollback/{jobId:guid}")]
    [RequirePermission(Permission.ManageSettings)]
    public async Task<ActionResult<ApiResponse<object>>> Rollback(Guid jobId)
    {
        var job = await _db.ImportJobs.FirstOrDefaultAsync(j => j.Id == jobId && j.BusinessId == BusinessId);
        if (job == null) return NotFound(ApiResponse<object>.Fail("Import job not found."));
        if (job.Status != ImportJobStatus.Completed)
            return BadRequest(ApiResponse<object>.Fail("Only completed imports can be rolled back."));

        // 1. Delete inventory transactions for products in this batch
        var batchProductIds = await _db.Products
            .Where(p => p.ImportBatchId == jobId)
            .Select(p => p.Id)
            .ToListAsync();
        if (batchProductIds.Count > 0)
        {
            var invTxns = await _db.InventoryTransactions
                .Where(t => batchProductIds.Contains(t.ProductId))
                .ToListAsync();
            _db.InventoryTransactions.RemoveRange(invTxns);
        }

        // 2. Products: hard-delete (inventory transactions removed above, no sales reference them)
        var products = await _db.Products.Where(p => p.ImportBatchId == jobId).ToListAsync();
        _db.Products.RemoveRange(products);

        // 3. Expenses: hard-delete
        var expenses = await _db.Expenses.Where(e => e.ImportBatchId == jobId).ToListAsync();
        _db.Expenses.RemoveRange(expenses);

        // 4. Ledger entries: hard-delete
        var ledgerEntries = await _db.LedgerEntries.Where(le => le.ImportBatchId == jobId).ToListAsync();
        _db.LedgerEntries.RemoveRange(ledgerEntries);

        // 5. Contacts: hard-delete (only those created by this batch with no other references)
        var contacts = await _db.Contacts.Where(c => c.ImportBatchId == jobId).ToListAsync();
        _db.Contacts.RemoveRange(contacts);

        job.Status = ImportJobStatus.RolledBack;
        await _db.SaveChangesAsync();

        var count = products.Count + expenses.Count + ledgerEntries.Count + contacts.Count;
        _logger.LogInformation("Import {JobId} rolled back: {Products} products, {Expenses} expenses, {Ledger} ledger entries, {Contacts} contacts deleted",
            jobId, products.Count, expenses.Count, ledgerEntries.Count, contacts.Count);

        return Ok(ApiResponse<object>.Ok(null!, $"Import rolled back. {count} records deleted."));
    }

    /// <summary>Lists recent import jobs for the current business. Useful for an import history page.</summary>
    [HttpGet("jobs")]
    public async Task<ActionResult<ApiResponse<List<ImportJobDto>>>> ListJobs([FromQuery] int limit = 20)
    {
        var cap = Math.Min(Math.Max(limit, 1), 100);
        var jobs = await _db.ImportJobs.AsNoTracking()
            .Where(j => j.BusinessId == BusinessId)
            .OrderByDescending(j => j.CreatedAtUtc)
            .Take(cap)
            .ToListAsync();

        return Ok(ApiResponse<List<ImportJobDto>>.Ok(jobs.Select(MapToDto).ToList()));
    }

    private async Task<ActionResult<ApiResponse<ImportJobDto>>> EnqueueImportAsync(IFormFile file, ImportJobType type, string permission, string mode = "default")
    {
        var (allowed, planErr) = await _planGuard.CheckFeatureAsync(BusinessId, "csv_import");
        if (!allowed) return BadRequest(ApiResponse<ImportJobDto>.Fail(planErr!));

        var (csvText, rowCount, parseError) = ReadAndValidateFile(file);
        if (parseError != null) return BadRequest(ApiResponse<ImportJobDto>.Fail(parseError));

        var job = new ImportJob
        {
            BusinessId = BusinessId,
            UserId = UserId,
            Type = type,
            Status = ImportJobStatus.Queued,
            RawCsvText = csvText,
            FileName = file.FileName,
            TotalRows = rowCount,
            ImportMode = mode,
            SkipExpenses = mode == "existing_stock" || mode == "price_update"
        };
        _db.ImportJobs.Add(job);
        await _db.SaveChangesAsync();

        // Queue the Hangfire job — processing starts as soon as a worker picks it up.
        _jobs.Enqueue<ImportJobService>(svc => svc.RunAsync(job.Id));

        _logger.LogInformation("Queued {Type} import {JobId} with {Rows} rows for business {BusinessId}",
            type, job.Id, rowCount, BusinessId);

        return Accepted(ApiResponse<ImportJobDto>.Ok(MapToDto(job),
            $"{rowCount} rows queued. You'll get a WhatsApp message when the import finishes."));
    }

    private static (string? CsvText, int RowCount, string? Error) ReadAndValidateFile(IFormFile? file)
    {
        if (file == null || file.Length == 0) return (null, 0, "No file uploaded.");
        if (file.Length > MaxFileSize) return (null, 0, $"File too large. Maximum size is {MaxFileSize / (1024 * 1024)}MB.");
        if (!file.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) return (null, 0, "Only CSV files are supported.");

        // Read the entire stream into memory once — we need to both count rows (for validation) and persist
        // the raw text onto the ImportJob row for the background worker. Streams can't be rewound after
        // CsvParser reads them, so we capture the text first and parse from it.
        using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var csvText = reader.ReadToEnd();

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csvText));
        var rows = CsvParser.Parse(stream);

        if (rows.Count == 0) return (null, 0, "No data rows found. Make sure the file has a header row and at least one data row.");
        if (rows.Count > MaxRows) return (null, 0, $"File has too many rows. Maximum is {MaxRows:N0} rows per import.");

        return (csvText, rows.Count, null);
    }

    private static ImportJobDto MapToDto(ImportJob job)
    {
        var errors = string.IsNullOrEmpty(job.ErrorsJson)
            ? new List<string>()
            : JsonSerializer.Deserialize<List<string>>(job.ErrorsJson) ?? new List<string>();

        return new ImportJobDto
        {
            Id = job.Id,
            Type = job.Type.ToString(),
            Status = job.Status.ToString(),
            FileName = job.FileName,
            TotalRows = job.TotalRows,
            ProcessedRows = job.ProcessedRows,
            SuccessCount = job.SuccessCount,
            ErrorCount = job.ErrorCount,
            Errors = errors,
            FailureReason = job.FailureReason,
            CreatedAtUtc = job.CreatedAtUtc,
            StartedAtUtc = job.StartedAtUtc,
            CompletedAtUtc = job.CompletedAtUtc,
            ProgressPercent = job.TotalRows == 0 ? 0 : (int)Math.Round(job.ProcessedRows * 100.0 / job.TotalRows)
        };
    }
}

public class ImportJobDto
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public int TotalRows { get; set; }
    public int ProcessedRows { get; set; }
    public int SuccessCount { get; set; }
    public int ErrorCount { get; set; }
    public List<string> Errors { get; set; } = new();
    public string? FailureReason { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public int ProgressPercent { get; set; }
}
