using Ojunai.API.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Common;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class RequireVoiceAIAttribute : Attribute, IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var config = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        if (!config.GetValue<bool>("VoiceAI:FeatureEnabled"))
        {
            context.Result = new NotFoundResult();
            return;
        }

        var businessIdClaim = context.HttpContext.User.FindFirst("businessId")?.Value;
        if (!Guid.TryParse(businessIdClaim, out var businessId))
        {
            context.Result = new UnauthorizedObjectResult(
                ApiResponse<object>.Fail("Authentication required."));
            return;
        }

        var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var business = await db.Businesses.AsNoTracking().FirstOrDefaultAsync(b => b.Id == businessId);
        if (business == null || !VoiceAIGuard.HasAccess(business))
        {
            context.Result = new ObjectResult(
                ApiResponse<object>.Fail("Voice AI is not enabled for your business. Enable it in Settings."))
            { StatusCode = 403 };
        }
    }
}
