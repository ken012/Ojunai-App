namespace Ojunai.API.DTOs.Auth;

public class AuthResponse
{
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public bool MustChangePassword { get; set; }
    public UserDto User { get; set; } = null!;
    public BusinessDto Business { get; set; } = null!;
}

public class UserDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string? Email { get; set; }
    public bool EmailVerified { get; set; }
    public string Role { get; set; } = string.Empty;
    public DateOnly? DateOfBirth { get; set; }
    /// <summary>"none" (default — alerts off until a channel is picked), "whatsapp", "telegram",
    /// or "messenger". Drives where business alerts/summaries go.</summary>
    public string AlertChannel { get; set; } = Ojunai.API.Common.AlertChannels.None;
}

public class BusinessDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? BusinessType { get; set; }
    public string Currency { get; set; } = "NGN";
    public string? State { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public string Timezone { get; set; } = "Africa/Lagos";
    public string Plan { get; set; } = "starter";
    public string? SubscribedPlan { get; set; }
    public DateTime? TrialEndsAt { get; set; }
    public decimal LargeSaleThreshold { get; set; } = 100000;
    public List<string>? CustomCategories { get; set; }
    public bool AlertLowStock { get; set; } = true;
    public bool AlertDailySummary { get; set; } = true;
    public bool AlertLargeSale { get; set; } = true;
    public bool LargeSaleAlertWhatsApp { get; set; } = true;
    public bool LargeSaleAlertTelegram { get; set; } = true;
    public bool LargeSaleAlertMessenger { get; set; } = true;
    public bool LargeSaleAlertDashboard { get; set; } = true;
    public bool AlertDashboardLowStock { get; set; } = true;
    public bool AlertDashboardDailySummary { get; set; } = true;
    public bool AlertDashboardLargeSale { get; set; } = true;
    public bool AlertDashboardAgedReceivable { get; set; } = true;
    public bool AlertDashboardStaffChanges { get; set; } = true;
    public decimal? DailySalesGoal { get; set; }
    public bool ConfirmLargeSales { get; set; }

    /// <summary>Public URL to the business's custom dashboard background image, or null. Pro/Business plans only.</summary>
    public string? BackgroundImageUrl { get; set; }
    public decimal BackgroundImageOpacity { get; set; } = 0.85m;
    public decimal ConfirmLargeSaleThreshold { get; set; }
    public bool ConfirmLargeSalesTelegram { get; set; }
    public decimal ConfirmLargeSaleThresholdTelegram { get; set; }
    public bool ConfirmLargeSalesMessenger { get; set; }
    public decimal ConfirmLargeSaleThresholdMessenger { get; set; }
    public bool IsActive { get; set; }
    public string AccountNumber { get; set; } = string.Empty;

    // Receipts
    public string? Address { get; set; }
    public bool VatEnabled { get; set; } = false;
    public decimal VatRate { get; set; } = 7.5m;
    public string? TaxId { get; set; }
    public string? ReceiptHeaderText { get; set; }
    public string? ReceiptFooterText { get; set; }
    public string? ReceiptAccentColor { get; set; }
}
