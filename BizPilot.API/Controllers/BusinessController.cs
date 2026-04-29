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
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpFactory;

    public BusinessController(
        IBusinessService business,
        PlanGuard planGuard,
        AppDbContext db,
        PaystackService paystack,
        ILogger<BusinessController> logger,
        IConfiguration config,
        IHttpClientFactory httpFactory)
    {
        _business = business;
        _planGuard = planGuard;
        _db = db;
        _paystack = paystack;
        _logger = logger;
        _config = config;
        _httpFactory = httpFactory;
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
            SubscriptionStatus = PlanGuard.GetSubscriptionStatus(biz),
            // Voice AI add-on
            VoiceAIFeatureVisible = _config.GetValue<bool>("VoiceAI:FeatureEnabled"),
            VoiceAIEnabled = VoiceAIGuard.HasAccess(biz),
            VoiceAIPlanStatus = biz.VoiceAIPlanStatus,
            VoiceAITrialDaysLeft = VoiceAIGuard.GetVoiceAITrialDaysLeft(biz),
            VoiceAITrialEndsAt = biz.VoiceAITrialEndsAt,
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
        return Ok(new
        {
            allowed,
            businessId = business.Id,
            businessName = business.Name,
            status = business.VoiceAIPlanStatus,
            error = allowed ? (string?)null : "Voice AI is not enabled for this business."
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

    private static object MapVoiceProduct(Models.Product p, string currency) => new
    {
        id = p.Id.ToString(),
        businessId = p.BusinessId.ToString(),
        name = p.Name,
        sku = p.SKU ?? $"BP-{p.Id.ToString("N").Substring(0, 8).ToUpper()}",
        aliases = new string[0],
        description = p.Category != null ? $"{p.Unit} — {p.Category}" : p.Unit,
        unitPriceMinor = (long)((p.SellingPrice ?? 0) * 100),
        currency,
        category = p.Category,
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
    public bool IsBillable { get; set; }
    public bool HasActiveSubscription { get; set; }
    public DateTime? SubscriptionEndsAt { get; set; }
    public string? PendingPlanChange { get; set; }
    public bool IsAutoRenew { get; set; }
    public string? PaymentMethod { get; set; }
    public string SubscriptionStatus { get; set; } = "none";
    // Voice AI add-on
    public bool VoiceAIFeatureVisible { get; set; }
    public bool VoiceAIEnabled { get; set; }
    public string VoiceAIPlanStatus { get; set; } = "inactive";
    public int? VoiceAITrialDaysLeft { get; set; }
    public DateTime? VoiceAITrialEndsAt { get; set; }
    public DateTime? VoiceAISubscriptionEndsAt { get; set; }
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
