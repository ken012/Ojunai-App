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
