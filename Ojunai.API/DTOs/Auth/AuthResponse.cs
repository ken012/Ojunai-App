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
    public string Role { get; set; } = string.Empty;
    public DateOnly? DateOfBirth { get; set; }
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
    public bool ConfirmLargeSales { get; set; }
    public decimal ConfirmLargeSaleThreshold { get; set; }
    public bool IsActive { get; set; }
    public string AccountNumber { get; set; } = string.Empty;
}
