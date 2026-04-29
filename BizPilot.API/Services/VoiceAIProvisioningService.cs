using System.Text;
using System.Text.Json;
using BizPilot.API.Data;
using BizPilot.API.Models;
using Microsoft.EntityFrameworkCore;

namespace BizPilot.API.Services;

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
    /// Ensures a Voice AI business record exists for the given BizPilot business.
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
                bizpilotAccountNumber = business.AccountNumber,
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
                // 409: already exists — look up via the entitlement check endpoint
                _logger.LogInformation("Voice AI business already exists for account {AccountNumber}, looking up ID", business.AccountNumber);
                await LookupExistingAsync(business);
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

    private Task LookupExistingAsync(Business business)
    {
        _logger.LogWarning("Voice AI business exists for account {AccountNumber} but VoiceAIBusinessId is not set. " +
            "Link manually via admin page or SQL: UPDATE \"Businesses\" SET \"VoiceAIBusinessId\" = '<id>' WHERE \"AccountNumber\" = '{Acct}'",
            business.AccountNumber, business.AccountNumber);
        return Task.CompletedTask;
    }
}
