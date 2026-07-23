using System.Text;
using System.Text.Json;
using Ojunai.API.Data;
using Ojunai.API.Models;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Services;

/// <summary>
/// Provisions a business in the Voice AI system when Voice AI is first enabled.
/// Called from payment webhooks and admin endpoints. Idempotent: skips if already linked.
/// </summary>
public class VoiceAIProvisioningService
{
    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<VoiceAIProvisioningService> _logger;

    public VoiceAIProvisioningService(
        AppDbContext db, IHttpClientFactory httpFactory,
        IConfiguration config, ILogger<VoiceAIProvisioningService> logger)
    {
        _db = db;
        _httpFactory = httpFactory;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Ensures a Voice AI business record exists for the given Ojunai business.
    /// If VoiceAIBusinessId is already set, does nothing. Otherwise creates the
    /// record via Voice AI's admin API and stores the returned ID.
    /// </summary>
    public async Task EnsureProvisionedAsync(Business business)
    {
        if (business.VoiceAIBusinessId.HasValue)
            return; // Already linked

        var adminKey = _config["VoiceAI:VoiceAdminKey"];
        if (string.IsNullOrEmpty(adminKey))
        {
            _logger.LogWarning("VoiceAI:VoiceAdminKey not configured — cannot provision business {BusinessId}", business.Id);
            return;
        }

        // Get owner's phone number for the Voice AI business
        var owner = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.BusinessId == business.Id && u.Role == UserRole.Owner && u.IsActive);
        var phone = owner?.PhoneNumber ?? "";
        if (!phone.StartsWith("+")) phone = $"+{phone}";

        try
        {
            var client = _httpFactory.CreateClient("VoiceAI");
            var body = new
            {
                name = business.Name,
                ojunaiAccountNumber = business.AccountNumber,
                phoneNumberExternal = phone,
                defaultLanguage = "en",
                timezone = business.Timezone ?? "Africa/Lagos",
                reservationHoldHours = 4,
                voiceTransport = "record"
            };

            var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/businesses");
            request.Headers.Add("X-Admin-Key", adminKey);
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                // 409: a voice business already exists for this account (or one of its
                // phone numbers). Recover the id and back-fill the link instead of
                // leaving the business permanently unlinked.
                var conflictField = TryParseConflictField(responseBody);
                _logger.LogInformation(
                    "Voice AI business already exists (conflict on {Field}) for account {AccountNumber}; attempting to back-fill the link",
                    conflictField, business.AccountNumber);
                await LookupExistingAsync(business, conflictField, adminKey);
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Voice AI provisioning failed for {BusinessId}: {Status} {Body}",
                    business.Id, response.StatusCode, responseBody);
                return;
            }

            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("id", out var idEl))
            {
                var voiceBusinessId = Guid.Parse(idEl.GetString()!);
                business.VoiceAIBusinessId = voiceBusinessId;
                await _db.SaveChangesAsync();
                _logger.LogInformation("Voice AI provisioned: {BusinessName} → {VoiceBusinessId}", business.Name, voiceBusinessId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Voice AI provisioning exception for {BusinessId}", business.Id);
        }
    }

    /// <summary>
    /// Recovery path for a 409 on create: a voice business already exists for this
    /// Ojunai account but VoiceAIBusinessId wasn't stored — e.g. a prior provision
    /// created the voice row but crashed before SaveChangesAsync, or the link was
    /// cleared by a restore/migration. Resolves the existing voice business id by
    /// account number and back-fills the link so the dashboard's voice settings work.
    ///
    /// Only heals when the conflict is on ojunaiAccountNumber. A phone/voiceNumber
    /// collision means a DIFFERENT account already owns that number — linking would be
    /// wrong, so we log loudly and leave it for a human.
    /// </summary>
    private async Task LookupExistingAsync(Business business, string conflictField, string adminKey)
    {
        if (conflictField != "ojunaiAccountNumber")
        {
            _logger.LogError(
                "Voice AI provisioning conflict on {Field} for business {BusinessId} (account {AccountNumber}): " +
                "a different voice business already owns this {Field}. NOT auto-linking — investigate the collision.",
                conflictField, business.Id, business.AccountNumber, conflictField);
            return;
        }

        try
        {
            var client = _httpFactory.CreateClient("VoiceAI");
            var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"/api/admin/businesses/by-ojunai/{Uri.EscapeDataString(business.AccountNumber)}");
            request.Headers.Add("X-Admin-Key", adminKey);

            var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                // 404 = business not found OR the lookup endpoint isn't deployed on the
                // voice backend yet. Either way, degrade to an actionable warning rather
                // than failing — the link stays null and can be fixed manually.
                _logger.LogWarning(
                    "Voice AI link back-fill for account {AccountNumber} returned {Status}. Link left unset — " +
                    "fix manually: UPDATE \"Businesses\" SET \"VoiceAIBusinessId\" = '<voice id>' WHERE \"AccountNumber\" = '{AccountNumber}'.",
                    business.AccountNumber, (int)response.StatusCode, business.AccountNumber);
                return;
            }

            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("id", out var idEl)
                && Guid.TryParse(idEl.GetString(), out var voiceBusinessId))
            {
                business.VoiceAIBusinessId = voiceBusinessId;
                await _db.SaveChangesAsync();
                _logger.LogInformation(
                    "Voice AI link self-healed: {BusinessName} (account {AccountNumber}) → {VoiceBusinessId}",
                    business.Name, business.AccountNumber, voiceBusinessId);
            }
            else
            {
                _logger.LogWarning(
                    "Voice AI link back-fill for account {AccountNumber} returned no usable id; link left unset.",
                    business.AccountNumber);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Voice AI link back-fill failed for account {AccountNumber}", business.AccountNumber);
        }
    }

    /// <summary>Extracts the conflicting field from the voice backend's 409 body ({ field, message }).</summary>
    private static string TryParseConflictField(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("field", out var f)
                ? (f.GetString() ?? "unknown")
                : "unknown";
        }
        catch
        {
            return "unknown";
        }
    }
}
