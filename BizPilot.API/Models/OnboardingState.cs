namespace BizPilot.API.Models;

public class OnboardingState
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string PhoneNumber { get; set; } = string.Empty;
    public OnboardingStep Step { get; set; } = OnboardingStep.Menu;
    public string? BusinessName { get; set; }
    public string? BusinessType { get; set; }
    public string? City { get; set; }
    public string? OwnerName { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastActivityUtc { get; set; } = DateTime.UtcNow;
}

public enum OnboardingStep
{
    Menu = 0,
    BusinessName = 1,
    BusinessType = 2,
    City = 3,
    OwnerName = 4,
    Confirmation = 5,
    Complete = 6
}
