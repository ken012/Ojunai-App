namespace Ojunai.API.Common;

/// <summary>
/// Single source of truth for password rules. Enforced at registration, password change,
/// and password reset. Keep this in sync with dashboard/src/lib/password-policy.ts —
/// the client mirrors these checks for live UI feedback, the server is authoritative.
/// </summary>
public static class PasswordPolicy
{
    public const int MinLength = 10;

    // Top common passwords + a few app-specific ones. Small embedded list — no external
    // dependency. Compared case-insensitively against the raw input.
    private static readonly HashSet<string> Blocklist = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "password1", "password12", "password123", "password1234",
        "12345678", "123456789", "1234567890", "qwerty", "qwerty123", "qwerty1234",
        "abc12345", "abcdefgh", "iloveyou", "letmein", "welcome1", "welcome123",
        "passw0rd", "p@ssword", "p@ssw0rd", "admin1234", "administrator",
        "ojunai", "ojunai123", "shopowner", "starter12", "businessowner",
    };

    public static (bool Ok, string? Reason) Validate(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return (false, $"Password is required (min {MinLength} characters).");

        if (raw.Length < MinLength)
            return (false, $"Password must be at least {MinLength} characters.");

        // Reject obvious runs / repeats — "aaaaaaaaaa", "1234567890", "abcdefghij" all
        // pass the length check but offer no real entropy.
        if (raw.Distinct().Count() < 4)
            return (false, "Password is too repetitive — mix in different characters.");

        if (Blocklist.Contains(raw))
            return (false, "That's a commonly used password — please choose something stronger.");

        var hasLower = raw.Any(char.IsLower);
        var hasUpper = raw.Any(char.IsUpper);
        var hasDigit = raw.Any(char.IsDigit);
        var hasSymbol = raw.Any(c => !char.IsLetterOrDigit(c));
        var classes = new[] { hasLower, hasUpper, hasDigit, hasSymbol }.Count(x => x);

        if (classes < 3)
            return (false, "Password must include at least 3 of: lowercase, uppercase, digits, symbols.");

        return (true, null);
    }

    /// <summary>
    /// Human-readable requirements list — surfaced in the dashboard strength hint and in
    /// the WhatsApp onboarding "you'll change this on first login" message.
    /// </summary>
    public static IReadOnlyList<string> RequirementsHint() => new[]
    {
        $"At least {MinLength} characters",
        "Mix of letters, numbers, and symbols (3 of 4)",
        "Avoid common passwords like 'password123'",
    };
}
