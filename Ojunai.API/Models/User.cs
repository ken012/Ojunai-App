namespace Ojunai.API.Models;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BusinessId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Owner;
    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; } = false;
    public string? PasswordResetCode { get; set; }
    public DateTime? PasswordResetCodeExpiresAtUtc { get; set; }
    public int TokenVersion { get; set; } = 0;
    public int FailedLoginAttempts { get; set; } = 0;
    public DateTime? LockoutEndsAtUtc { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public bool EmailVerified { get; set; } = false;
    public DateTime? EmailVerifiedAtUtc { get; set; }

    // ── Pricing v2 onboarding fields ─────────────────────────────────────────
    /// <summary>
    /// Tier the user clicked through marketing to land on (?plan=lite, ?plan=operator, etc.).
    /// Captured at registration; drives the dashboard-home upgrade prompt card. Null means
    /// the user came in cold or registered via WhatsApp.
    /// </summary>
    public string? IntendedPlan { get; set; }

    /// <summary>
    /// Billing cadence the user picked on the marketing site (?period=monthly|yearly). Pre-fills
    /// the upgrade card's billing-period toggle. Defaults to "monthly" if the param is missing.
    /// </summary>
    public string? IntendedBillingPeriod { get; set; }

    /// <summary>
    /// One-time-per-account counter for the WhatsApp Starter onboarding allowance. Starter
    /// plan users get 2 free inventory adds via WhatsApp before the bot locks them out and
    /// prompts upgrade. Capped at 2; never resets, even if the user downgrades from a paid
    /// plan back to Starter.
    /// </summary>
    public int OnboardingInventoryCount { get; set; } = 0;

    /// <summary>
    /// Where outbound alerts and summaries (daily summary, low stock, large sale, trial reminder,
    /// renewal warning, etc.) get delivered. <c>"whatsapp"</c> (default, backward-compat) or
    /// <c>"telegram"</c>. Opt-in via Settings → Alert delivery. Doesn't affect OTPs (always
    /// WhatsApp) or in-app dashboard bell alerts.
    /// </summary>
    public string AlertChannel { get; set; } = "whatsapp";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Business Business { get; set; } = null!;
}

public enum UserRole
{
    Owner = 1,
    Admin = 2,
    Sales = 3,
    Bookkeeper = 4,
    Viewer = 5
}
