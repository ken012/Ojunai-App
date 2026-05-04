using System.Security.Claims;

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
}
