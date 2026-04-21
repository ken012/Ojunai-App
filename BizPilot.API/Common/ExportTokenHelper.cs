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

        var payloadB64 = ToUrlSafeBase64(Encoding.UTF8.GetBytes(payload));
        var signature = ComputeHmac(payloadB64, secret);
        return $"{payloadB64}.{signature}";
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
            var json = Encoding.UTF8.GetString(FromUrlSafeBase64(parts[0]));
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
        return ToUrlSafeBase64(hash);
    }

    private static string ToUrlSafeBase64(byte[] bytes) =>
        Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static byte[] FromUrlSafeBase64(string s)
    {
        var b64 = s.Replace('-', '+').Replace('_', '/');
        switch (b64.Length % 4)
        {
            case 2: b64 += "=="; break;
            case 3: b64 += "="; break;
        }
        return Convert.FromBase64String(b64);
    }
}
