using BizPilot.API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Security.Claims;

namespace BizPilot.API.Common;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public class RequirePermissionAttribute : Attribute, IAuthorizationFilter
{
    private readonly string _permission;

    public RequirePermissionAttribute(string permission)
    {
        _permission = permission;
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var roleClaim = context.HttpContext.User.FindFirst(ClaimTypes.Role)?.Value;
        if (string.IsNullOrEmpty(roleClaim))
        {
            context.Result = new UnauthorizedObjectResult(
                ApiResponse<object>.Fail("Authentication required."));
            return;
        }

        if (!Enum.TryParse<UserRole>(roleClaim, true, out var role))
        {
            context.Result = new ForbidResult();
            return;
        }

        if (!RolePermissions.HasPermission(role, _permission))
        {
            context.Result = new ObjectResult(
                ApiResponse<object>.Fail($"Permission denied. Your role ({roleClaim}) does not have '{_permission}' access."))
            {
                StatusCode = 403
            };
        }
    }
}
