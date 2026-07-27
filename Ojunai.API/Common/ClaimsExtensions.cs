using System.Security.Claims;
using Ojunai.API.Models;

namespace Ojunai.API.Common;

public static class ClaimsExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(ClaimTypes.NameIdentifier)
                 ?? user.FindFirstValue("sub")
                 ?? throw new UnauthorizedAccessException("User ID claim missing.");
        return Guid.Parse(value);
    }

    public static Guid GetBusinessId(this ClaimsPrincipal user)
    {
        var value = user.FindFirstValue("businessId")
            ?? throw new UnauthorizedAccessException("Business ID claim missing.");
        return Guid.Parse(value);
    }

    /// <summary>The caller's role from the JWT (no DB round-trip). Falls back to the LEAST-privileged role
    /// (<see cref="UserRole.Viewer"/>) if the claim is missing or unparseable, so a malformed token can never
    /// be mistaken for an all-access Owner/Admin.</summary>
    public static UserRole GetRole(this ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(ClaimTypes.Role);
        return Enum.TryParse<UserRole>(value, ignoreCase: true, out var role) ? role : UserRole.Viewer;
    }
}
