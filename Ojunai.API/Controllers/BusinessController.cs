using Ojunai.API.Common;
using Ojunai.API.Data;
using Ojunai.API.DTOs.Auth;
using Ojunai.API.DTOs.Business;
using Ojunai.API.Models;
using Ojunai.API.Services;
using Ojunai.API.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Controllers;

[Route("api/business")]
public class BusinessController : OjunaiBaseController
{
    private readonly IBusinessService _business;
    private readonly PlanGuard _planGuard;
    private readonly AppDbContext _db;
    private readonly PaystackService _paystack;
    private readonly ILogger<BusinessController> _logger;
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpFactory;
    private readonly Services.Interfaces.IBackgroundImageService _backgroundImage;
    private readonly IReceiptService _receipts;

    public BusinessController(
        IBusinessService business,
        PlanGuard planGuard,
        AppDbContext db,
        PaystackService paystack,
        ILogger<BusinessController> logger,
        IConfiguration config,
        IHttpClientFactory httpFactory,
        Services.Interfaces.IBackgroundImageService backgroundImage,
        IReceiptService receipts)
    {
        _business = business;
        _planGuard = planGuard;
        _db = db;
        _paystack = paystack;
        _logger = logger;
        _config = config;
        _httpFactory = httpFactory;
        _backgroundImage = backgroundImage;
        _receipts = receipts;
    }

    /// <summary>
    /// Render a SAMPLE receipt PDF using the supplied (possibly unsaved) receipt settings, so the
    /// owner can preview appearance before saving. Combines the business's real identity/address/
    /// currency with the draft receipt fields from the request. Persists nothing.
    /// </summary>
    [HttpPost("receipt-preview")]
    [RequirePermission(Permission.ManageSettings)]
    public async Task<IActionResult> ReceiptPreview([FromBody] ReceiptPreviewRequest request)
    {
        var biz = await _db.Businesses.FindAsync(BusinessId);
        if (biz == null) return NotFound();

        var draft = new Business
        {
            Name = biz.Name,
            Currency = biz.Currency,
            Address = biz.Address,
            City = biz.City,
            State = biz.State,
            Country = biz.Country,
            ReceiptPrefix = biz.ReceiptPrefix,
            ReceiptHeaderText = request.ReceiptHeaderText,
            ReceiptFooterText = request.ReceiptFooterText,
            ReceiptAccentColor = request.ReceiptAccentColor,
            TaxId = request.TaxId,
            VatEnabled = request.VatEnabled,
            VatRate = request.VatRate,
        };

        var pdf = _receipts.GeneratePreview(draft);
        return File(pdf, "application/pdf", "receipt-preview.pdf");
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
            HasCustomBranding = config.HasCustomBranding,
            IsBillable = biz.IsBillable,
            HasActiveSubscription = !string.IsNullOrEmpty(biz.PaystackSubscriptionCode)
                || !string.IsNullOrEmpty(biz.FlutterwaveSubscriptionId)
                || (biz.SubscriptionEndsAt.HasValue && biz.SubscriptionEndsAt > DateTime.UtcNow),
            SubscriptionEndsAt = biz.SubscriptionEndsAt,
            PendingPlanChange = biz.PendingPlanChange,
            IsAutoRenew = biz.IsAutoRenew,
            PaymentMethod = biz.PaymentMethod,
            SubscriptionStatus = PlanGuard.GetSubscriptionStatus(biz),
            // OjunaiVoice (standalone, two-tier)
            VoiceAIFeatureVisible = _config.GetValue<bool>("VoiceAI:FeatureEnabled"),
            VoiceAIEnabled = VoiceAIGuard.HasAccess(biz),
            VoiceAIPlanStatus = biz.VoiceAIPlanStatus,
            VoiceAITier = biz.VoiceAITier,
            VoiceAITierMinutesIncluded = !string.IsNullOrEmpty(biz.VoiceAITier)
                && BillingConfig.VoiceAITierMinutes.TryGetValue(biz.VoiceAITier, out var inc) ? inc : null,
            VoiceAICycleMinutesUsed = biz.VoiceAICycleMinutesUsed,
            VoiceAICycleMinutesRemaining = VoiceAIGuard.GetVoiceAICycleMinutesRemaining(biz),
            VoiceAITrialMinutesUsed = biz.VoiceAITrialMinutesUsed,
            VoiceAITrialMinutesRemaining = VoiceAIGuard.GetVoiceAITrialMinutesRemaining(biz),
            VoiceAISubscriptionEndsAt = biz.VoiceAISubscriptionEndsAt,
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
    /// Upload a custom dashboard background image. Pro + Business plans only.
    ///
    /// Security pipeline lives in BackgroundImageService — see that file for the full
    /// list of validation layers (MIME whitelist, magic-byte sniff, dimension preflight,
    /// full decode, EXIF strip, re-encode, UUID filename). Returns the updated business
    /// with the new BackgroundImageUrl populated.
    /// </summary>
    [HttpPost("background-image")]
    [RequirePermission(Permission.ManageSettings)]
    [RequestSizeLimit(5 * 1024 * 1024)] // 5MB — caps wire bytes before model binding
    public async Task<ActionResult<ApiResponse<BusinessDto>>> UploadBackgroundImage(IFormFile file)
    {
        var (allowed, planErr) = await _planGuard.CheckFeatureAsync(BusinessId, "custom_branding");
        if (!allowed) return BadRequest(ApiResponse<BusinessDto>.Fail(planErr!));

        await _backgroundImage.SaveAsync(BusinessId, file);
        var result = await _business.GetByIdAsync(BusinessId);
        return Ok(ApiResponse<BusinessDto>.Ok(result, "Background image updated."));
    }

    /// <summary>Removes the custom dashboard background image and reverts to the default.</summary>
    [HttpDelete("background-image")]
    [RequirePermission(Permission.ManageSettings)]
    public async Task<ActionResult<ApiResponse<BusinessDto>>> RemoveBackgroundImage()
    {
        await _backgroundImage.RemoveAsync(BusinessId);
        var result = await _business.GetByIdAsync(BusinessId);
        return Ok(ApiResponse<BusinessDto>.Ok(result, "Background image removed."));
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

        // Sanitize the business name before placing it in the Content-Disposition header. The name is
        // user-controlled (registration / chat onboarding / AI correction) and is not character-limited,
        // so a stray quote would break out of the quoted filename token and a CR/LF would make Kestrel
        // reject the header. Keep only a safe filename alphabet; collapse everything else to '-'.
        var safeName = System.Text.RegularExpressions.Regex.Replace(
            business.Name.Replace(" ", "-").ToLowerInvariant(), "[^a-z0-9._-]", "-").Trim('-');
        if (string.IsNullOrEmpty(safeName)) safeName = "business";
        if (safeName.Length > 60) safeName = safeName[..60];
        var filename = $"ojunai-export-{safeName}-{DateTime.UtcNow:yyyyMMdd}.json";
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
        return Ok(ApiResponse<object>.Ok(null!, "Account closed. Thank you for using Ojunai."));
    }

    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    [HttpGet("voice-ai-check/{accountNumber}")]
    public async Task<IActionResult> CheckVoiceAIAccess(
        [FromRoute] string accountNumber,
        [FromHeader(Name = "X-VoiceAI-Key")] string? apiKey)
    {
        var secret = _config["VoiceAI:InternalApiKey"];
        if (string.IsNullOrEmpty(secret) || secret.Length < 16)
            return StatusCode(503);
        if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(apiKey ?? ""),
            System.Text.Encoding.UTF8.GetBytes(secret)))
            return Unauthorized();

        if (!_config.GetValue<bool>("VoiceAI:FeatureEnabled"))
            return Ok(new { allowed = false, error = "Voice AI is not available." });

        var business = await _db.Businesses.AsNoTracking()
            .FirstOrDefaultAsync(b => b.AccountNumber == accountNumber && b.IsActive);
        if (business == null)
            return NotFound(new { allowed = false, error = "Business not found." });

        var allowed = VoiceAIGuard.HasAccess(business);
        // Tier-aware response. The Voice AI service uses these fields to enforce concurrent-line
        // and per-cycle minute caps in its telephony layer. When the merchant is on trial, tier
        // is null and the service should enforce the 10-minute trial cap using trialMinutesRemaining.
        int? tierMinutes = !string.IsNullOrEmpty(business.VoiceAITier)
            && BillingConfig.VoiceAITierMinutes.TryGetValue(business.VoiceAITier, out var inc) ? inc : null;
        int? tierLines = !string.IsNullOrEmpty(business.VoiceAITier)
            && BillingConfig.VoiceAITierConcurrentLines.TryGetValue(business.VoiceAITier, out var lines) ? lines : null;
        return Ok(new
        {
            allowed,
            businessId = business.Id,
            businessName = business.Name,
            status = business.VoiceAIPlanStatus,
            tier = business.VoiceAITier,
            tierLabel = !string.IsNullOrEmpty(business.VoiceAITier)
                && BillingConfig.VoiceAITierLabels.TryGetValue(business.VoiceAITier, out var lbl) ? lbl : null,
            minutesIncluded = tierMinutes,
            cycleMinutesUsed = business.VoiceAICycleMinutesUsed,
            cycleMinutesRemaining = VoiceAIGuard.GetVoiceAICycleMinutesRemaining(business),
            concurrentLines = tierLines,
            priorityQueueing = business.VoiceAITier == "pro",
            trialMinutesIncluded = BillingConfig.VoiceAITrialMinutes,
            trialMinutesUsed = business.VoiceAITrialMinutesUsed,
            trialMinutesRemaining = VoiceAIGuard.GetVoiceAITrialMinutesRemaining(business),
            subscriptionEndsAt = business.VoiceAISubscriptionEndsAt,
            error = allowed ? (string?)null : "Voice AI is not enabled for this business."
        });
    }

    /// <summary>
    /// Voice AI service → main API: report inbound minutes consumed by a business. Increments the
    /// trial counter (while status=trial) or the paid-cycle counter (while status=active). Returns the
    /// updated counters so the caller can re-check whether the merchant still has remaining minutes
    /// without a separate round-trip. The Voice AI service should call this after each call ends with
    /// the minutes (rounded up to whole minutes is fine; we store ints).
    /// </summary>
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    [HttpPost("voice-ai-minutes/{accountNumber}")]
    public async Task<IActionResult> ReportVoiceAIMinutes(
        [FromRoute] string accountNumber,
        [FromHeader(Name = "X-VoiceAI-Key")] string? apiKey,
        [FromBody] VoiceAIMinutesReport body)
    {
        var secret = _config["VoiceAI:InternalApiKey"];
        if (string.IsNullOrEmpty(secret) || secret.Length < 16)
            return StatusCode(503);
        if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(apiKey ?? ""),
            System.Text.Encoding.UTF8.GetBytes(secret)))
            return Unauthorized();

        if (body == null || body.Minutes <= 0)
            return BadRequest(new { error = "Minutes must be a positive integer." });
        // Cap a single report at 24 hours of audio to keep a buggy caller from running the counter
        // to int.MaxValue in a single request. Anything longer is almost certainly a malformed call log.
        if (body.Minutes > 1440)
            return BadRequest(new { error = "A single report cannot exceed 1440 minutes." });

        var business = await _db.Businesses.FirstOrDefaultAsync(b => b.AccountNumber == accountNumber && b.IsActive);
        if (business == null) return NotFound(new { error = "Business not found." });

        // Increment the matching counter. On trial we count against the 10-minute taste; on a paid
        // tier we count against the per-cycle cap. Active businesses without a tier (legacy override)
        // still get their cycle counter ticked so the admin overview shows real usage.
        if (business.VoiceAIPlanStatus == "trial")
            business.VoiceAITrialMinutesUsed += body.Minutes;
        else
            business.VoiceAICycleMinutesUsed += body.Minutes;

        // If trial minutes were just consumed past the cap, flip status to suspended right away so the
        // next call check from the Voice AI service short-circuits without waiting for the daily sweep.
        if (business.VoiceAIPlanStatus == "trial"
            && business.VoiceAITrialMinutesUsed >= BillingConfig.VoiceAITrialMinutes
            && !business.VoiceAIInternalOverride)
        {
            business.VoiceAIPlanStatus = "suspended";
            business.VoiceAIEnabled = false;
            _db.BillingEvents.Add(new Models.BillingEvent
            {
                BusinessId = business.Id,
                EventType = "voiceai.trial.minutes_exhausted",
                Provider = "system",
                Plan = "voice_ai",
                Status = "suspended"
            });
        }

        await _db.SaveChangesAsync();

        return Ok(new
        {
            status = business.VoiceAIPlanStatus,
            tier = business.VoiceAITier,
            cycleMinutesUsed = business.VoiceAICycleMinutesUsed,
            cycleMinutesRemaining = VoiceAIGuard.GetVoiceAICycleMinutesRemaining(business),
            trialMinutesUsed = business.VoiceAITrialMinutesUsed,
            trialMinutesRemaining = VoiceAIGuard.GetVoiceAITrialMinutesRemaining(business),
        });
    }

    // ── Voice AI Inventory Endpoints ─────────────────────────────────────────

    private IActionResult? ValidateVoiceAIKey(string? apiKey)
    {
        var secret = _config["VoiceAI:InternalApiKey"];
        if (string.IsNullOrEmpty(secret) || secret.Length < 16) return StatusCode(503);
        if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(apiKey ?? ""),
            System.Text.Encoding.UTF8.GetBytes(secret)))
            return Unauthorized();
        return null;
    }

    private async Task<(Business? Business, IActionResult? Error)> GetVoiceAIBusiness(string accountNumber, string? apiKey)
    {
        var auth = ValidateVoiceAIKey(apiKey);
        if (auth != null) return (null, auth);

        var business = await _db.Businesses.AsNoTracking()
            .FirstOrDefaultAsync(b => b.AccountNumber == accountNumber && b.IsActive);
        if (business == null) return (null, NotFound(new { reason = "not-found", error = "Business not found." }));

        if (!VoiceAIGuard.HasAccess(business))
            return (null, StatusCode(403, new { reason = "voice-ai-disabled", error = "Voice AI is not enabled for this business." }));

        return (business, null);
    }

    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    [HttpGet("voice-ai-products/{accountNumber}")]
    public async Task<IActionResult> VoiceAIProducts(
        [FromRoute] string accountNumber,
        [FromHeader(Name = "X-VoiceAI-Key")] string? apiKey,
        [FromQuery] string? search = null,
        [FromQuery] int limit = 50,
        [FromQuery] bool includeInactive = false)
    {
        var (business, error) = await GetVoiceAIBusiness(accountNumber, apiKey);
        if (error != null) return error;

        limit = Math.Clamp(limit, 1, 200);
        var currency = business!.Currency;

        var query = _db.Products.AsNoTracking()
            .Where(p => p.BusinessId == business.Id);

        if (!includeInactive) query = query.Where(p => p.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var startPattern = $"{search}%";
            var wordPattern = $"% {search}%";
            query = query.Where(p =>
                EF.Functions.ILike(p.Name, startPattern) || EF.Functions.ILike(p.Name, wordPattern));
        }

        var raw = await query
            .OrderBy(p => p.Name)
            .Take(limit)
            .ToListAsync();

        var products = raw.Select(p => MapVoiceProduct(p, currency)).ToList();
        return Ok(new { products });
    }

    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    [HttpGet("voice-ai-products/{accountNumber}/find")]
    public async Task<IActionResult> VoiceAIFindProduct(
        [FromRoute] string accountNumber,
        [FromHeader(Name = "X-VoiceAI-Key")] string? apiKey,
        [FromQuery] string? q = null)
    {
        var (business, error) = await GetVoiceAIBusiness(accountNumber, apiKey);
        if (error != null) return error;

        if (string.IsNullOrWhiteSpace(q))
            return Ok(new { products = Array.Empty<object>() });

        var currency = business!.Currency;
        var allActive = _db.Products.AsNoTracking()
            .Where(p => p.BusinessId == business.Id && p.IsActive);

        // Exact match first (case-insensitive)
        var exactRaw = await allActive
            .Where(p => EF.Functions.ILike(p.Name, q))
            .Take(5)
            .ToListAsync();

        if (exactRaw.Count >= 5)
            return Ok(new { products = exactRaw.Select(p => MapVoiceProduct(p, currency)) });

        // Then starts-with
        var foundIds = exactRaw.Select(p => p.Id).ToList();
        var startsRaw = await allActive
            .Where(p => !foundIds.Contains(p.Id) && EF.Functions.ILike(p.Name, $"{q}%"))
            .Take(5 - exactRaw.Count)
            .ToListAsync();

        var combined = exactRaw.Concat(startsRaw).ToList();
        if (combined.Count >= 5)
            return Ok(new { products = combined.Select(p => MapVoiceProduct(p, currency)) });

        // Then fuzzy (contains)
        foundIds = combined.Select(p => p.Id).ToList();
        var fuzzyRaw = await allActive
            .Where(p => !foundIds.Contains(p.Id) && EF.Functions.ILike(p.Name, $"%{q}%"))
            .Take(5 - combined.Count)
            .ToListAsync();

        return Ok(new { products = combined.Concat(fuzzyRaw).Select(p => MapVoiceProduct(p, currency)) });
    }

    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    [HttpGet("voice-ai-inventory/{productId:guid}")]
    public async Task<IActionResult> VoiceAIInventory(
        [FromRoute] Guid productId,
        [FromHeader(Name = "X-VoiceAI-Key")] string? apiKey)
    {
        var auth = ValidateVoiceAIKey(apiKey);
        if (auth != null) return auth;

        var product = await _db.Products.AsNoTracking()
            .Include(p => p.Business)
            .FirstOrDefaultAsync(p => p.Id == productId);

        if (product == null) return NotFound(new { error = "Product not found." });
        if (!VoiceAIGuard.HasAccess(product.Business))
            return StatusCode(403, new { reason = "voice-ai-disabled" });

        // Calculate reserved quantity from active stock holds
        var reserved = await _db.Set<Models.StockHold>()
            .Where(h => h.ProductId == productId && h.Status == Models.HoldStatus.Active)
            .SumAsync(h => h.Quantity);

        return Ok(new
        {
            productId = product.Id.ToString(),
            quantityOnHand = product.CurrentStock,
            quantityReserved = reserved,
            quantityAvailable = Math.Max(0, product.CurrentStock - reserved),
            reorderLevel = product.LowStockThreshold
        });
    }

    // ── Voice AI Settings Proxy ────────────────────────────────────────────

    [HttpGet("voice-ai-settings")]
    [RequirePermission(Permission.ManageSettings)]
    public async Task<IActionResult> GetVoiceAISettings()
    {
        var business = await _db.Businesses.AsNoTracking().FirstOrDefaultAsync(b => b.Id == BusinessId);
        if (business == null) return NotFound(ApiResponse<object>.Fail("Business not found."));

        if (!VoiceAIGuard.HasAccess(business))
            return StatusCode(403, ApiResponse<object>.Fail("Voice AI is not enabled for this business."));

        if (!business.VoiceAIBusinessId.HasValue)
            return NotFound(ApiResponse<object>.Fail("Voice AI not configured for this business. Contact support."));

        var adminKey = _config["VoiceAI:VoiceAdminKey"];
        if (string.IsNullOrEmpty(adminKey))
            return StatusCode(503, ApiResponse<object>.Fail("Voice AI settings not configured."));

        try
        {
            var client = _httpFactory.CreateClient("VoiceAI");
            var request = new HttpRequestMessage(HttpMethod.Get,
                $"/api/admin/businesses/{business.VoiceAIBusinessId}/settings");
            request.Headers.Add("X-Admin-Key", adminKey);

            var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return NotFound(ApiResponse<object>.Fail("Voice AI not configured for this business."));
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _logger.LogError("Voice AI admin key rejected — check VoiceAI:VoiceAdminKey config");
                return StatusCode(503, ApiResponse<object>.Fail("Voice AI temporarily unavailable. Please contact support."));
            }
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Voice AI settings GET failed: {Status} {Body}", response.StatusCode, body);
                return StatusCode(502, ApiResponse<object>.Fail("Voice AI temporarily unavailable. Try again."));
            }

            return Content(body, "application/json");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Voice AI settings proxy failed for business {BusinessId}", BusinessId);
            return StatusCode(502, ApiResponse<object>.Fail("Voice AI temporarily unavailable. Try again."));
        }
    }

    [HttpPatch("voice-ai-settings")]
    [RequirePermission(Permission.ManageSettings)]
    public async Task<IActionResult> UpdateVoiceAISettings()
    {
        var business = await _db.Businesses.AsNoTracking().FirstOrDefaultAsync(b => b.Id == BusinessId);
        if (business == null) return NotFound(ApiResponse<object>.Fail("Business not found."));

        if (!VoiceAIGuard.HasAccess(business))
            return StatusCode(403, ApiResponse<object>.Fail("Voice AI is not enabled for this business."));

        if (!business.VoiceAIBusinessId.HasValue)
            return NotFound(ApiResponse<object>.Fail("Voice AI not configured for this business. Contact support."));

        var adminKey = _config["VoiceAI:VoiceAdminKey"];
        if (string.IsNullOrEmpty(adminKey))
            return StatusCode(503, ApiResponse<object>.Fail("Voice AI settings not configured."));

        try
        {
            using var reader = new StreamReader(Request.Body);
            var requestBody = await reader.ReadToEndAsync();

            var client = _httpFactory.CreateClient("VoiceAI");
            var request = new HttpRequestMessage(HttpMethod.Patch,
                $"/api/admin/businesses/{business.VoiceAIBusinessId}/settings");
            request.Headers.Add("X-Admin-Key", adminKey);
            request.Content = new StringContent(requestBody, System.Text.Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
                return BadRequest(ApiResponse<object>.Fail(body));
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _logger.LogError("Voice AI admin key rejected on PATCH — check VoiceAI:VoiceAdminKey config");
                return StatusCode(503, ApiResponse<object>.Fail("Voice AI temporarily unavailable. Please contact support."));
            }
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Voice AI settings PATCH failed: {Status} {Body}", response.StatusCode, body);
                return StatusCode(502, ApiResponse<object>.Fail("Voice AI temporarily unavailable. Try again."));
            }

            return Content(body, "application/json");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Voice AI settings PATCH proxy failed for business {BusinessId}", BusinessId);
            return StatusCode(502, ApiResponse<object>.Fail("Voice AI temporarily unavailable. Try again."));
        }
    }

    // ── Voice AI Reservations Proxy ─────────────────────────────────────────

    [HttpGet("voice-ai-reservations")]
    [RequirePermission(Permission.ViewStock)]
    public async Task<IActionResult> GetVoiceAIReservations(
        [FromQuery] string? status = "all",
        [FromQuery] int limit = 50,
        [FromQuery] string? since = null)
    {
        var business = await _db.Businesses.AsNoTracking().FirstOrDefaultAsync(b => b.Id == BusinessId);
        if (business == null) return NotFound(ApiResponse<object>.Fail("Business not found."));

        if (!VoiceAIGuard.HasAccess(business))
            return StatusCode(403, ApiResponse<object>.Fail("Voice AI is not enabled for this business."));

        if (!business.VoiceAIBusinessId.HasValue)
            return Ok(new { reservations = Array.Empty<object>(), total = 0 });

        var adminKey = _config["VoiceAI:VoiceAdminKey"];
        if (string.IsNullOrEmpty(adminKey))
            return StatusCode(503, ApiResponse<object>.Fail("Voice AI not configured."));

        try
        {
            var client = _httpFactory.CreateClient("VoiceAI");
            // URL-encode status (parity with `since` below) so a value like "all%26limit%3D999999" can't
            // inject extra query parameters into the internal admin request carrying the X-Admin-Key.
            var qs = $"?status={Uri.EscapeDataString(status ?? "all")}&limit={Math.Clamp(limit, 1, 200)}";
            if (!string.IsNullOrEmpty(since)) qs += $"&since={Uri.EscapeDataString(since)}";

            var request = new HttpRequestMessage(HttpMethod.Get,
                $"/api/admin/businesses/{business.VoiceAIBusinessId}/reservations{qs}");
            request.Headers.Add("X-Admin-Key", adminKey);

            var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _logger.LogError("Voice AI admin key rejected on reservations GET");
                return StatusCode(503, ApiResponse<object>.Fail("Voice AI temporarily unavailable. Please contact support."));
            }
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return Ok(new { reservations = Array.Empty<object>(), total = 0 });
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Voice AI reservations GET failed: {Status} {Body}", response.StatusCode, body);
                return StatusCode(502, ApiResponse<object>.Fail("Voice AI temporarily unavailable. Try again."));
            }

            // Enrich with contact names from Ojunai's Contacts table
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("reservations", out var reservationsEl))
            {
                var phones = new HashSet<string>();
                foreach (var r in reservationsEl.EnumerateArray())
                {
                    if (r.TryGetProperty("customerPhone", out var ph) && ph.GetString() is string p)
                    {
                        phones.Add(p);
                        phones.Add(p.TrimStart('+'));
                        if (!p.StartsWith("+")) phones.Add("+" + p);
                    }
                }

                var contactsByPhone = new Dictionary<string, string>();
                if (phones.Count > 0)
                {
                    var contacts = await _db.Contacts.AsNoTracking()
                        .Where(c => c.BusinessId == BusinessId && c.PhoneNumber != null)
                        .Select(c => new { c.PhoneNumber, c.Name })
                        .ToListAsync();
                    foreach (var c in contacts)
                    {
                        if (c.PhoneNumber == null) continue;
                        var normalized = c.PhoneNumber.TrimStart('+');
                        contactsByPhone.TryAdd(normalized, c.Name);
                    }
                }

                var enriched = new List<object>();
                foreach (var r in reservationsEl.EnumerateArray())
                {
                    var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(r.GetRawText())!;
                    var phone = r.TryGetProperty("customerPhone", out var phEl) ? phEl.GetString() : null;
                    if (phone != null && contactsByPhone.TryGetValue(phone.TrimStart('+'), out var name))
                        dict["customerName"] = name;
                    enriched.Add(dict);
                }

                return Ok(new { reservations = enriched, total = enriched.Count });
            }

            return Content(body, "application/json");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Voice AI reservations proxy failed for business {BusinessId}", BusinessId);
            return StatusCode(502, ApiResponse<object>.Fail("Voice AI temporarily unavailable. Try again."));
        }
    }

    /// <summary>
    /// Confirms a reservation belongs to the caller's Voice-AI business before a mutation is relayed to
    /// the GLOBAL, non-business-scoped admin mutation route. Returns true when the id is found in the
    /// caller's own (business-scoped) reservation list; false when the caller's COMPLETE list definitively
    /// does not contain it; and null when we can't tell — the list may be truncated at the 200 cap, or the
    /// lookup errored — in which case callers must NOT block (fail open) to avoid breaking legitimate
    /// mutations. This makes the common case (a leaked foreign GUID) reject while never blocking a real one.
    /// </summary>
    private async Task<bool?> VoiceReservationOwnedAsync(string adminKey, Guid voiceBizId, Guid reservationId)
    {
        try
        {
            var client = _httpFactory.CreateClient("VoiceAI");
            var req = new HttpRequestMessage(HttpMethod.Get,
                $"/api/admin/businesses/{voiceBizId}/reservations?status=all&limit=200");
            req.Headers.Add("X-Admin-Key", adminKey);
            var resp = await client.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;

            var body = await resp.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("reservations", out var arr)
                || arr.ValueKind != System.Text.Json.JsonValueKind.Array)
                return null;

            var count = 0;
            foreach (var r in arr.EnumerateArray())
            {
                count++;
                if (r.TryGetProperty("id", out var idEl)
                    && idEl.ValueKind == System.Text.Json.JsonValueKind.String
                    && Guid.TryParse(idEl.GetString(), out var g) && g == reservationId)
                    return true;
            }
            // Not found: only trust a NEGATIVE when the list is definitely complete (below the cap).
            return count >= 200 ? (bool?)null : false;
        }
        catch
        {
            return null;
        }
    }

    [HttpPatch("voice-ai-reservations/{reservationId:guid}/status")]
    [RequirePermission(Permission.ManageStock)]
    public async Task<IActionResult> UpdateVoiceAIReservationStatus(
        [FromRoute] Guid reservationId,
        [FromBody] UpdateVoiceReservationStatusRequest request)
    {
        var business = await _db.Businesses.AsNoTracking().FirstOrDefaultAsync(b => b.Id == BusinessId);
        if (business == null) return NotFound(ApiResponse<object>.Fail("Business not found."));

        if (!VoiceAIGuard.HasAccess(business))
            return StatusCode(403, ApiResponse<object>.Fail("Voice AI is not enabled for this business."));

        if (!business.VoiceAIBusinessId.HasValue)
            return NotFound(ApiResponse<object>.Fail("Voice AI not configured for this business."));

        var validStatuses = new[] { "cancelled", "fulfilled", "expired" };
        if (string.IsNullOrEmpty(request.Status) || !validStatuses.Contains(request.Status))
            return BadRequest(ApiResponse<object>.Fail("Status must be: cancelled, fulfilled, or expired."));

        var adminKey = _config["VoiceAI:VoiceAdminKey"];
        if (string.IsNullOrEmpty(adminKey))
            return StatusCode(503, ApiResponse<object>.Fail("Voice AI not configured."));

        // Ownership check: the mutation route below (/api/admin/reservations/{id}) is NOT business-scoped,
        // so verify this reservation actually belongs to the caller's Voice-AI business first — otherwise
        // a merchant with a leaked foreign reservation GUID could cancel/fulfil/expire another tenant's
        // reservation. Null (indeterminate) does not block, so legitimate mutations are never broken.
        if (await VoiceReservationOwnedAsync(adminKey, business.VoiceAIBusinessId.Value, reservationId) == false)
            return NotFound(ApiResponse<object>.Fail("Reservation not found."));

        try
        {
            var client = _httpFactory.CreateClient("VoiceAI");
            var body = System.Text.Json.JsonSerializer.Serialize(new { status = request.Status, releaseReason = request.ReleaseReason, note = request.Note });

            var httpRequest = new HttpRequestMessage(HttpMethod.Patch,
                $"/api/admin/reservations/{reservationId}/status");
            httpRequest.Headers.Add("X-Admin-Key", adminKey);
            httpRequest.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

            var response = await client.SendAsync(httpRequest);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
                return Conflict(ApiResponse<object>.Fail("This reservation is already in a terminal state."));
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return NotFound(ApiResponse<object>.Fail("Reservation not found."));
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _logger.LogError("Voice AI admin key rejected on reservation status PATCH");
                return StatusCode(503, ApiResponse<object>.Fail("Voice AI temporarily unavailable."));
            }
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Voice AI reservation status PATCH failed: {Status} {Body}", response.StatusCode, responseBody);
                return StatusCode(502, ApiResponse<object>.Fail("Voice AI temporarily unavailable. Try again."));
            }

            return Content(responseBody, "application/json");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Voice AI reservation status PATCH failed for {ReservationId}", reservationId);
            return StatusCode(502, ApiResponse<object>.Fail("Voice AI temporarily unavailable. Try again."));
        }
    }

    [HttpPost("voice-ai-reservations/{reservationId:guid}/sell")]
    [RequirePermission(Permission.RecordSales)]
    public async Task<IActionResult> SellVoiceAIReservation(
        [FromRoute] Guid reservationId,
        [FromBody] SellVoiceReservationRequest request)
    {
        var business = await _db.Businesses.AsNoTracking().FirstOrDefaultAsync(b => b.Id == BusinessId);
        if (business == null) return NotFound(ApiResponse<object>.Fail("Business not found."));

        if (!VoiceAIGuard.HasAccess(business))
            return StatusCode(403, ApiResponse<object>.Fail("Voice AI is not enabled."));

        // Look up the product to get selling price
        var product = await _db.Products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.ProductId && p.BusinessId == BusinessId && p.IsActive);
        if (product == null)
            return NotFound(ApiResponse<object>.Fail("Product not found."));

        var unitPrice = product.SellingPrice ?? 0;

        // 1. Mark fulfilled on Voice AI
        var adminKey = _config["VoiceAI:VoiceAdminKey"];
        if (!string.IsNullOrEmpty(adminKey))
        {
            // Cross-tenant guard: don't fulfil (and then sell against) a reservation that isn't the
            // caller's. The downstream mutation route is not business-scoped. Indeterminate does not block.
            if (business.VoiceAIBusinessId.HasValue
                && await VoiceReservationOwnedAsync(adminKey, business.VoiceAIBusinessId.Value, reservationId) == false)
                return NotFound(ApiResponse<object>.Fail("Reservation not found."));

            try
            {
                var client = _httpFactory.CreateClient("VoiceAI");
                var voiceBody = System.Text.Json.JsonSerializer.Serialize(new { status = "fulfilled", releaseReason = "picked_up" });
                var httpReq = new HttpRequestMessage(HttpMethod.Patch, $"/api/admin/reservations/{reservationId}/status");
                httpReq.Headers.Add("X-Admin-Key", adminKey);
                httpReq.Content = new StringContent(voiceBody, System.Text.Encoding.UTF8, "application/json");
                await client.SendAsync(httpReq);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Voice AI fulfill failed for reservation {Id}, continuing with Ojunai sale", reservationId);
            }
        }

        // 2. Create sale in Ojunai
        var salesService = HttpContext.RequestServices.GetRequiredService<Services.Interfaces.ISalesService>();
        var user = await _db.Users.FindAsync(UserId);

        // Find or create contact from phone
        Guid? contactId = null;
        if (!string.IsNullOrEmpty(request.CustomerPhone))
        {
            var phone = request.CustomerPhone.TrimStart('+');
            var contact = await _db.Contacts.FirstOrDefaultAsync(c =>
                c.BusinessId == BusinessId && c.PhoneNumber != null && c.PhoneNumber.TrimStart('+') == phone);
            contactId = contact?.Id;
        }

        var sale = await salesService.CreateAsync(BusinessId, new DTOs.Sales.CreateSaleRequest
        {
            Items = new List<DTOs.Sales.SaleItemRequest>
            {
                new() { ProductId = request.ProductId, Quantity = request.Quantity, UnitPrice = unitPrice }
            },
            ContactId = contactId,
            PaymentStatus = Models.PaymentStatus.Paid
        }, "VoiceAI", user?.Id, user?.FullName);

        return Ok(ApiResponse<DTOs.Sales.SaleDto>.Ok(sale, "Reservation fulfilled and sale recorded."));
    }

    private static object MapVoiceProduct(Models.Product p, string currency) => new
    {
        id = p.Id.ToString(),
        businessId = p.BusinessId.ToString(),
        name = p.Name,
        sku = p.SKU ?? $"BP-{p.Id.ToString("N").Substring(0, 8).ToUpper()}",
        aliases = string.IsNullOrEmpty(p.Aliases) ? Array.Empty<string>() : System.Text.Json.JsonSerializer.Deserialize<string[]>(p.Aliases) ?? Array.Empty<string>(),
        description = !string.IsNullOrEmpty(p.VoiceDescription) ? p.VoiceDescription : (string?)null,
        unitPriceMinor = (long)((p.SellingPrice ?? 0) * 100),
        currency,
        category = p.Category,
        quantityOnHand = p.CurrentStock,
        active = p.IsActive
    };
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
    public bool HasCustomBranding { get; set; }
    public bool IsBillable { get; set; }
    public bool HasActiveSubscription { get; set; }
    public DateTime? SubscriptionEndsAt { get; set; }
    public string? PendingPlanChange { get; set; }
    public bool IsAutoRenew { get; set; }
    public string? PaymentMethod { get; set; }
    public string SubscriptionStatus { get; set; } = "none";
    // OjunaiVoice (standalone, two-tier)
    public bool VoiceAIFeatureVisible { get; set; }
    public bool VoiceAIEnabled { get; set; }
    public string VoiceAIPlanStatus { get; set; } = "inactive";
    /// <summary>"starter" or "pro", null on trial or before tier picked.</summary>
    public string? VoiceAITier { get; set; }
    /// <summary>Inbound minutes the tier includes per cycle (300 for starter, 1000 for pro). Null on trial.</summary>
    public int? VoiceAITierMinutesIncluded { get; set; }
    /// <summary>Inbound minutes consumed in the current paid cycle.</summary>
    public int VoiceAICycleMinutesUsed { get; set; }
    /// <summary>Inbound minutes still available in the current paid cycle. Null on trial.</summary>
    public int? VoiceAICycleMinutesRemaining { get; set; }
    /// <summary>Inbound minutes still available on the trial (0 once consumed). Null when not on trial.</summary>
    public int? VoiceAITrialMinutesRemaining { get; set; }
    public int VoiceAITrialMinutesUsed { get; set; }
    public DateTime? VoiceAISubscriptionEndsAt { get; set; }
}

public class StartTrialRequest
{
    public string Plan { get; set; } = string.Empty;
}

/// <summary>
/// Sent by the Voice AI service to /business/voice-ai-minutes/{accountNumber} after each call
/// ends. The Voice AI service is the source of truth for "how many minutes did this merchant
/// just use" — we just persist and gate. CallSid is logged for audit reconciliation between
/// the two systems.
/// </summary>
public class VoiceAIMinutesReport
{
    public int Minutes { get; set; }
    public string? CallSid { get; set; }
}

public class CloseAccountRequest
{
    public string ConfirmationPassword { get; set; } = string.Empty;
    public string Confirm { get; set; } = string.Empty;
}

public class SellVoiceReservationRequest
{
    public Guid ProductId { get; set; }
    public decimal Quantity { get; set; }
    public string? CustomerPhone { get; set; }
}

public class UpdateVoiceReservationStatusRequest
{
    public string Status { get; set; } = string.Empty;
    public string? ReleaseReason { get; set; }
    public string? Note { get; set; }
}
