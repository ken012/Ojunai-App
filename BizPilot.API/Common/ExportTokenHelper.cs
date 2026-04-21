using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BizPilot.API.Common;

public record ExportTokenPayload(Guid BusinessId, string ReportType, DateOnly From, DateOnly To, long ExpiresAtUnix);

public static class ExportTokenHelper
{
    public static string GenerateToken(Guid businessId, string reportType, DateOnly from, DateOnly to, string secret, TimeSpan? expiry = null)
    {
        var exp = DateTimeOffset.UtcNow.Add(expiry ?? TimeSpan.FromHours(24)).ToUnixTimeSeconds();
        var payload = JsonSerializer.Serialize(new
        {
            bid = businessId.ToString(),
            type = reportType,
            from = from.ToString("yyyy-MM-dd"),
            to = to.ToString("yyyy-MM-dd"),
            exp
        });

        var payloadBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
        var signature = ComputeHmac(payloadBase64, secret);
        return $"{payloadBase64}.{signature}";
    }

    public static ExportTokenPayload? ValidateToken(string token, string secret)
    {
        var parts = token.Split('.', 2);
        if (parts.Length != 2) return null;

        var expectedSig = ComputeHmac(parts[0], secret);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(parts[1]),
                Encoding.UTF8.GetBytes(expectedSig)))
            return null;

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(parts[0]));
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var bid = Guid.Parse(root.GetProperty("bid").GetString()!);
            var type = root.GetProperty("type").GetString()!;
            var from = DateOnly.Parse(root.GetProperty("from").GetString()!);
            var to = DateOnly.Parse(root.GetProperty("to").GetString()!);
            var exp = root.GetProperty("exp").GetInt64();

            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > exp) return null;

            return new ExportTokenPayload(bid, type, from, to, exp);
        }
        catch
        {
            return null;
        }
    }

    private static string ComputeHmac(string data, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToBase64String(hash);
    }
}
