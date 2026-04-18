using BizPilot.API.Common;
using BizPilot.API.Data;
using BizPilot.API.DTOs.Auth;
using BizPilot.API.DTOs.Business;
using BizPilot.API.Models;
using BizPilot.API.Services;
using BizPilot.API.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BizPilot.API.Controllers;

[Route("api/business")]
public class BusinessController : BizPilotBaseController
{
    private readonly IBusinessService _business;
    private readonly PlanGuard _planGuard;
    private readonly AppDbContext _db;
    private readonly PaystackService _paystack;
    private readonly ILogger<BusinessController> _logger;

    public BusinessController(
        IBusinessService business,
        PlanGuard planGuard,
        AppDbContext db,
        PaystackService paystack,
        ILogger<BusinessController> logger)
    {
        _business = business;
        _planGuard = planGuard;
        _db = db;
        _paystack = paystack;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<BusinessDto>>> Get()
    {
        var result = await _business.GetByIdAsync(BusinessId);
        return Ok(ApiResponse<BusinessDto>.Ok(result));
    }

    [HttpGet("plan-status")]
    public async Task<ActionResult<ApiResponse<PlanStatusDto>>> GetPlanStatus()
    {
        var biz = await _planGuard.GetBusinessAsync(BusinessId);
        if (biz == null) return NotFound(ApiResponse<PlanStatusDto>.Fail("Business not found."));

        var trial = PlanGuard.GetTrialStatus(biz);
        var config = PlanLimits.Get(biz.Plan);

        int? daysLeft = null;
        if (biz.TrialEndsAt.HasValue && trial == TrialStatus.Active)
            daysLeft = Math.Max(0, (int)Math.Ceiling((biz.TrialEndsAt.Value - DateTime.UtcNow).TotalDays));
        else if (biz.TrialEndsAt.HasValue && trial == TrialStatus.GracePeriod)
            daysLeft = 0;

        return Ok(ApiResponse<PlanStatusDto>.Ok(new PlanStatusDto
        {
            Plan = biz.Plan,
            SubscribedPlan = biz.SubscribedPlan,
            IsSubscriber = PlanGuard.IsSubscriber(biz),
            TrialStatus = trial.ToString(),
            TrialDaysLeft = daysLeft,
            TrialEndsAt = biz.TrialEndsAt,
            PricePerMonth = config.PricePerMonth,
            MaxProducts = config.MaxProducts,
            MaxMessages = config.MaxMessagesPerMonth,
            MaxStaff = config.MaxStaff,
            HasLedger = config.HasLedger,
            HasCsvImport = config.HasCsvImport,
            HasAdvancedReports = config.HasAdvancedReports,
            HasMonthlyCharts = config.HasMonthlyCharts,
            HasStockHolds = config.HasStockHolds,
            IsBillable = biz.IsBillable,
            HasActiveSubscription = !string.IsNullOrEmpty(biz.PaystackSubscriptionCode)
                || !string.IsNullOrEmpty(biz.FlutterwaveSubscriptionId)
                || (biz.SubscriptionEndsAt.HasValue && biz.SubscriptionEndsAt > DateTime.UtcNow),
            SubscriptionEndsAt = biz.SubscriptionEndsAt,
            PendingPlanChange = biz.PendingPlanChange,
            IsAutoRenew = biz.IsAutoRenew,
            PaymentMethod = biz.PaymentMethod,
        }));
    }

    [HttpPost("start-trial")]
    [RequirePermission(Permission.ManageSettings)]
    public async Task<ActionResult<ApiResponse<object>>> StartTrial([FromBody] StartTrialRequest request)
    {
        var error = await _planGuard.StartTrialAsync(BusinessId, request.Plan);
        if (error != null) return BadRequest(ApiResponse<object>.Fail(error));

        return Ok(ApiResponse<object>.Ok(null!, $"Your {request.Plan} free trial has started! You have {PlanGuard.TrialDurationDays} days to try it out."));
    }

    [HttpPut]
    [RequirePermission(Permission.ManageSettings)]
    public async Task<ActionResult<ApiResponse<BusinessDto>>> Update([FromBody] UpdateBusinessRequest request)
    {
        var result = await _business.UpdateAsync(BusinessId, request);
        return Ok(ApiResponse<BusinessDto>.Ok(result, "Business updated."));
    }

    /// <summary>
    /// GDPR Article 20 (right to data portability). Returns a JSON blob with every piece of business data the
    /// owner can claim ownership over — profile, staff (without credentials), products, sales, expenses,
    /// contacts, ledger, inventory movements, stock holds, and message logs. Restricted to Owner
    /// (ManageSettings permission) because the payload contains PII across all staff and customers.
    /// </summary>
    [HttpGet("export")]
    [RequirePermission(Permission.ManageSettings)]
    public async Task<IActionResult> ExportData(CancellationToken ct)
    {
        var businessId = BusinessId;

        var business = await _db.Businesses.AsNoTracking().FirstOrDefaultAsync(b => b.Id == businessId, ct);
        if (business == null) return NotFound(ApiResponse<object>.Fail("Business not found."));

        // Project each entity set to explicit anonymous objects so we never accidentally leak sensitive fields
        // (e.g., password hashes, Paystack subscription codes) into the export.
        var users = await _db.Users.AsNoTracking()
            .Where(u => u.BusinessId == businessId)
            .Select(u => new
            {
                u.Id, u.FullName, u.PhoneNumber, u.Email, u.Role, u.IsActive, u.CreatedAtUtc
            })
            .ToListAsync(ct);

        var products = await _db.Products.AsNoTracking()
            .Where(p => p.BusinessId == businessId)
            .ToListAsync(ct);

        // Include sale items so the export is a complete transaction record; IgnoreQueryFilters so soft-deleted
        // sales (which the user may have voided but are still part of their history) are included too.
        var sales = await _db.Sales.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(s => s.BusinessId == businessId)
            .Select(s => new
            {
                s.Id, s.ContactId, s.TotalAmount, s.PaymentStatus, s.PaymentMethod, s.Notes,
                s.Source, s.RecordedByUserId, s.RecordedByName, s.IsDeleted, s.DeletedAtUtc, s.CreatedAtUtc,
                Items = s.Items.Select(i => new { i.Id, i.ProductId, i.Quantity, i.UnitPrice, i.TotalPrice })
            })
            .ToListAsync(ct);

        var expenses = await _db.Expenses.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(e => e.BusinessId == businessId)
            .ToListAsync(ct);

        var contacts = await _db.Contacts.AsNoTracking()
            .Where(c => c.BusinessId == businessId)
            .ToListAsync(ct);

        var ledger = await _db.LedgerEntries.AsNoTracking()
            .Where(l => l.BusinessId == businessId)
            .ToListAsync(ct);

        var inventoryTxns = await _db.InventoryTransactions.AsNoTracking()
            .Where(t => t.BusinessId == businessId)
            .ToListAsync(ct);

        var stockHolds = await _db.StockHolds.AsNoTracking()
            .Where(h => h.BusinessId == businessId)
            .ToListAsync(ct);

        // Message logs can be large; we include them because they contain user-entered PII the user has a right
        // to see. Skip the parsed payload JSON to keep export size reasonable — raw message is enough for audit.
        var messageLogs = await _db.MessageLogs.AsNoTracking()
            .Where(m => m.BusinessId == businessId)
            .Select(m => new
            {
                m.Id, m.UserId, m.WhatsAppMessageId, m.Direction, m.Channel, m.RawMessage,
                m.ParsedIntent, m.ConfidenceScore, m.ProcessingStatus, m.CreatedAtUtc
            })
            .ToListAsync(ct);

        var export = new
        {
            ExportedAtUtc = DateTime.UtcNow,
            Business = new
            {
                business.Id, business.Name, business.BusinessType, business.Currency, business.Country,
                business.State, business.City, business.Plan, business.SubscribedPlan, business.TrialEndsAt,
                business.SubscriptionEndsAt, business.PendingPlanChange, business.LargeSaleThreshold,
                business.CustomCategories, business.AlertLowStock, business.AlertDailySummary,
                business.AlertLargeSale, business.IsActive, business.CreatedAtUtc
            },
            Users = users,
            Products = products,
            Sales = sales,
            Expenses = expenses,
            Contacts = contacts,
            LedgerEntries = ledger,
            InventoryTransactions = inventoryTxns,
            StockHolds = stockHolds,
            MessageLogs = messageLogs
        };

        var filename = $"bizpilot-export-{business.Name.Replace(" ", "-").ToLowerInvariant()}-{DateTime.UtcNow:yyyyMMdd}.json";
        Response.Headers["Content-Disposition"] = $"attachment; filename=\"{filename}\"";
        return Ok(export);
    }

    /// <summary>
    /// GDPR Article 17 (right to erasure) / account closure. Irreversibly closes the business:
    ///   - Cancels the Paystack subscription if one exists (best-effort — failure doesn't block closure).
    ///   - Anonymizes PII on the owner and all staff (names, phones, emails).
    ///   - Anonymizes customer/supplier PII.
    ///   - Marks the business inactive so the ActiveUserMiddleware rejects all future requests for it.
    ///   - Bumps TokenVersion on every user so any outstanding JWTs are invalidated immediately.
    ///
    /// Financial transaction records (sales, expenses, ledger, inventory) are retained for tax/accounting
    /// legal retention obligations but decoupled from identifying PII. Requires Owner confirmation
    /// via the current password to prevent accidental or compromised-session deletions.
    /// </summary>
    [HttpDelete]
    [RequirePermission(Permission.ManageSettings)]
    public async Task<ActionResult<ApiResponse<object>>> CloseAccount([FromBody] CloseAccountRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ConfirmationPassword))
            return BadRequest(ApiResponse<object>.Fail("Password confirmation is required to close the account."));
        if (request.Confirm != "DELETE MY ACCOUNT")
            return BadRequest(ApiResponse<object>.Fail("You must type 'DELETE MY ACCOUNT' exactly to confirm."));

        var owner = await _db.Users.FirstOrDefaultAsync(u =>
            u.Id == UserId && u.BusinessId == BusinessId && u.Role == UserRole.Owner && u.IsActive);
        if (owner == null) return Unauthorized(ApiResponse<object>.Fail("Only the business owner can close the account."));

        if (!BCrypt.Net.BCrypt.Verify(request.ConfirmationPassword, owner.PasswordHash))
            return Unauthorized(ApiResponse<object>.Fail("Password does not match."));

        var business = await _db.Businesses.FindAsync(BusinessId);
        if (business == null) return NotFound(ApiResponse<object>.Fail("Business not found."));

        // Best-effort Paystack cancellation — a Paystack outage must not block account closure.
        if (!string.IsNullOrEmpty(business.PaystackSubscriptionCode))
        {
            try { await _paystack.CancelSubscriptionAsync(BusinessId); }
            catch (Exception ex) { _logger.LogError(ex, "Paystack cancel failed during account closure for {BusinessId}", BusinessId); }
        }

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var suffix = BusinessId.ToString("N")[..8];

            var users = await _db.Users.Where(u => u.BusinessId == BusinessId).ToListAsync();
            foreach (var u in users)
            {
                // Unique phone suffix avoids collision with the PhoneNumber unique index when another business
                // later signs up with the same real phone number.
                u.FullName = "Deleted User";
                u.Email = null;
                u.PhoneNumber = $"x{u.Id.ToString("N")[..18]}";
                u.PasswordHash = string.Empty;
                u.PasswordResetCode = null;
                u.PasswordResetCodeExpiresAtUtc = null;
                u.IsActive = false;
                u.TokenVersion++;
            }

            var contacts = await _db.Contacts.Where(c => c.BusinessId == BusinessId).ToListAsync();
            foreach (var c in contacts)
            {
                c.Name = "Deleted Contact";
                c.PhoneNumber = null;
            }

            business.Name = $"Closed Account {suffix}";
            business.City = null;
            business.State = null;
            business.BusinessType = null;
            business.CustomCategories = null;
            business.PaystackCustomerCode = null;
            business.PaystackSubscriptionCode = null;
            business.PaystackPlanCode = null;
            business.IsActive = false;

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

        _logger.LogWarning("Business {BusinessId} closed and PII anonymized by owner {UserId}", BusinessId, UserId);
        return Ok(ApiResponse<object>.Ok(null!, "Account closed. Thank you for using BizPilot."));
    }
}

public class PlanStatusDto
{
    public string Plan { get; set; } = "starter";
    public string? SubscribedPlan { get; set; }
    public bool IsSubscriber { get; set; }
    public string TrialStatus { get; set; } = "None";
    public int? TrialDaysLeft { get; set; }
    public DateTime? TrialEndsAt { get; set; }
    public decimal PricePerMonth { get; set; }
    public int MaxProducts { get; set; }
    public int MaxMessages { get; set; }
    public int MaxStaff { get; set; }
    public bool HasLedger { get; set; }
    public bool HasCsvImport { get; set; }
    public bool HasAdvancedReports { get; set; }
    public bool HasMonthlyCharts { get; set; }
    public bool HasStockHolds { get; set; }
    public bool IsBillable { get; set; }
    public bool HasActiveSubscription { get; set; }
    public DateTime? SubscriptionEndsAt { get; set; }
    public string? PendingPlanChange { get; set; }
    public bool IsAutoRenew { get; set; }
    public string? PaymentMethod { get; set; }
}

public class StartTrialRequest
{
    public string Plan { get; set; } = string.Empty;
}

public class CloseAccountRequest
{
    public string ConfirmationPassword { get; set; } = string.Empty;
    public string Confirm { get; set; } = string.Empty;
}
