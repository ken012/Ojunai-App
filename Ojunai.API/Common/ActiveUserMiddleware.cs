using Ojunai.API.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Common;

/// <summary>
/// Runs on every authenticated request and performs defense-in-depth checks that JWT signing alone cannot guarantee:
///   1. User still exists and is active (not deactivated since the token was issued)
///   2. User's business is still active
///   3. The businessId claim in the JWT matches the user's actual BusinessId in the database
///      (prevents an attacker who forged a token with a swapped businessId from accessing another business)
///   4. The token's "tokenVersion" claim matches the user's current TokenVersion
///      (tokens are invalidated when the user changes their password or completes a password reset)
///
/// Endpoints decorated with [AllowAnonymous] are skipped entirely — login, register, password
/// reset, and account recovery flows are designed to handle bad/stale auth state, and blocking
/// them would prevent the user from recovering once their cookie's tokenVersion goes stale.
/// </summary>
public class ActiveUserMiddleware
{
    private readonly RequestDelegate _next;

    public ActiveUserMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, AppDbContext db)
    {
        // Skip for [AllowAnonymous] endpoints. Without this, a user with a stale-but-signature-valid
        // JWT cookie (typical after a password change on another device) cannot log in — the middleware
        // would 401 their /auth/login request before the controller ever runs.
        var endpoint = context.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<IAllowAnonymous>() != null)
        {
            await _next(context);
            return;
        }

        // Only authenticated requests need these checks — anonymous endpoints (login, webhooks) flow through.
        if (context.User.Identity?.IsAuthenticated == true)
        {
            // AuthService puts the user ID in the "sub" claim, which ASP.NET Core maps to NameIdentifier by default.
            // We check both names plus a legacy "userId" fallback to be resilient to claim-mapping configuration differences.
            var userIdClaim = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? context.User.FindFirst("sub")?.Value
                ?? context.User.FindFirst("userId")?.Value;

            if (Guid.TryParse(userIdClaim, out var userId))
            {
                // Fetch the current state of the user and their business. This adds one DB query per authenticated request,
                // which is acceptable for the security benefit (blocks stolen tokens from deactivated accounts).
                var user = await db.Users
                    .Include(u => u.Business)
                    .FirstOrDefaultAsync(u => u.Id == userId);

                // Block the request if:
                //   - The user was deleted (shouldn't happen with soft-delete, but defensive)
                //   - The user was deactivated (staff removed, owner deactivated themselves, etc.)
                //   - The business was deactivated
                if (user == null || !user.IsActive || !user.Business.IsActive)
                {
                    context.Response.StatusCode = 401;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync("{\"success\":false,\"errors\":[\"Account is inactive.\"]}");
                    return;
                }

                // Prevent token forgery attacks: if someone signed a token with a businessId that doesn't match
                // the user's actual BusinessId in the database, reject it. This ensures JWT signing compromise
                // cannot be used to cross-tenant data.
                if (user.BusinessId.ToString() != context.User.FindFirst("businessId")?.Value)
                {
                    context.Response.StatusCode = 401;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync("{\"success\":false,\"errors\":[\"Invalid session.\"]}");
                    return;
                }

                // Token version check — whenever a user changes their password or completes a reset, we increment
                // User.TokenVersion. Old tokens still have the old version and are rejected here.
                // This gives us a way to invalidate all sessions for a user after a credential change.
                // Fail CLOSED: reject when the claim is missing/unparseable OR does not match. Every
                // legitimately-minted token carries tokenVersion (AuthService.GenerateJwt always adds
                // it), so a token without a parseable claim is malformed/forged and must not slip past
                // revocation — the old `TryParse && !=` form silently allowed it.
                var tokenVersionClaim = context.User.FindFirst("tokenVersion")?.Value;
                if (!int.TryParse(tokenVersionClaim, out var tokenVersion) || tokenVersion != user.TokenVersion)
                {
                    // Proactively clear the stale cookie. Without this, every subsequent request
                    // (including the user's next login attempt if they don't manually clear cookies)
                    // would re-send the same bad cookie and re-trigger this 401 — which is exactly
                    // what "I always have to clear cache" reports were about.
                    context.Response.Cookies.Delete("oj_auth", new CookieOptions
                    {
                        HttpOnly = true,
                        Secure = true,
                        SameSite = SameSiteMode.Strict,
                        Path = "/",
                    });
                    context.Response.StatusCode = 401;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync("{\"success\":false,\"errors\":[\"Session expired. Please log in again.\"]}");
                    return;
                }
            }
        }

        await _next(context);
    }
}
