using System.Net.Http.Headers;
using System.Text.Json;

namespace BizPilot.API.Services;

/// <summary>
/// DelegatingHandler that transparently adds a Flutterwave OAuth2 access token
/// to every outgoing request on the "Flutterwave" HttpClient.
/// Uses the centralized Flutterwave IDP (same URL for sandbox and production).
/// Token is cached until expiry.
/// </summary>
public class FlutterwaveAuthHandler : DelegatingHandler
{
    private const string DefaultTokenUrl =
        "https://idp.flutterwave.com/realms/flutterwave/protocol/openid-connect/token";

    private readonly IConfiguration _config;
    private readonly ILogger<FlutterwaveAuthHandler> _logger;

    private string? _cachedToken;
    private DateTime _tokenExpiresAt = DateTime.MinValue;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public FlutterwaveAuthHandler(IConfiguration config, ILogger<FlutterwaveAuthHandler> logger)
    {
        _config = config;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await GetTokenAsync(cancellationToken);
        if (token != null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await base.SendAsync(request, cancellationToken);
    }

    private async Task<string?> GetTokenAsync(CancellationToken ct)
    {
        if (_cachedToken != null && DateTime.UtcNow < _tokenExpiresAt)
            return _cachedToken;

        await _lock.WaitAsync(ct);
        try
        {
            if (_cachedToken != null && DateTime.UtcNow < _tokenExpiresAt)
                return _cachedToken;

            var clientId = _config["Flutterwave:ClientId"];
            var clientSecret = _config["Flutterwave:ClientSecret"];

            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            {
                _logger.LogError("Flutterwave ClientId or ClientSecret not configured");
                return null;
            }

            var tokenUrl = _config["Flutterwave:TokenUrl"] ?? DefaultTokenUrl;

            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["grant_type"] = "client_credentials"
            });

            using var httpClient = new HttpClient();
            var response = await httpClient.PostAsync(tokenUrl, form, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Flutterwave OAuth token failed: {Status} {Body}", response.StatusCode, body);
                return null;
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            _cachedToken = root.GetProperty("access_token").GetString();
            var expiresIn = root.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3600;
            _tokenExpiresAt = DateTime.UtcNow.AddSeconds(expiresIn - 60);

            _logger.LogInformation("Flutterwave OAuth token acquired, expires in {ExpiresIn}s", expiresIn);
            return _cachedToken;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to acquire Flutterwave OAuth token");
            return null;
        }
        finally
        {
            _lock.Release();
        }
    }
}
