using BizPilot.API.DTOs.Auth;

namespace BizPilot.API.Services.Interfaces;

public interface IAuthService
{
    Task<AuthResponse> RegisterOwnerAsync(RegisterOwnerRequest request);
    Task<AuthResponse> LoginAsync(LoginRequest request);
    Task<UserDto> GetMeAsync(Guid userId);
    Task ChangePasswordAsync(Guid userId, string currentPassword, string newPassword);
    Task<string> RequestPasswordResetAsync(string phoneNumber);
    Task VerifyResetAndChangePasswordAsync(string phoneNumber, string code, string newPassword);
}
