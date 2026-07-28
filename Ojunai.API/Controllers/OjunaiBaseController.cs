using Ojunai.API.Common;
using Ojunai.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Ojunai.API.Controllers;

[Authorize]
[ApiController]
public abstract class OjunaiBaseController : ControllerBase, IAsyncActionFilter
{
    protected Guid BusinessId => User.GetBusinessId();
    protected Guid UserId => User.GetUserId();

    /// <summary>
    /// Multi-location: capture the request's location into the ambient <see cref="LocationScope"/> so the
    /// AppDbContext dual-write mirror and the per-location read/write gates route to the right location.
    ///
    /// Access control (multi-location user scoping):
    ///  • Owner/Admin — all-access: the X-Location-Id header passes through verbatim (null = "All locations").
    ///    No DB round-trip.
    ///  • Restricted roles (Sales/Bookkeeper/Viewer) at a MULTI-location business — PINNED to one of their
    ///    accessible locations (their assignments, or the default location if unassigned). A missing or
    ///    foreign header can't widen them to business-wide. Single-location businesses are unaffected (the
    ///    resolver returns the raw request, and the >1-active gate ignores it) — byte-for-byte unchanged.
    ///
    /// Scope is set per action and cleared in a finally, so no value can leak across requests.
    /// </summary>
    /// <remarks>
    /// [NonAction] is REQUIRED: this is the <see cref="IAsyncActionFilter"/> hook, not a routable endpoint.
    /// Without it, MVC treats this public controller method as an action whose two complex parameters both
    /// infer [FromBody], and startup aborts at MapControllers with "more than one parameter bound from body"
    /// — crashing EVERY controller that inherits this base. The attribute only removes it from action
    /// discovery; the filter pipeline still invokes it on every request via the interface.
    /// </remarks>
    [NonAction]
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        try
        {
            // Anonymous actions (e.g. login/register on AuthController, which also inherits this base) have no
            // businessId/role claims — reading them would throw. Skip scoping entirely for them.
            if (User.Identity?.IsAuthenticated == true)
            {
                var header = Request.Headers["X-Location-Id"].FirstOrDefault();
                var requested = Guid.TryParse(header, out var locId) ? (Guid?)locId : null;

                var role = User.GetRole();
                if (role is Ojunai.API.Models.UserRole.Owner or Ojunai.API.Models.UserRole.Admin)
                {
                    LocationScope.Current = requested; // all-access — no DB round-trip
                }
                else
                {
                    var access = context.HttpContext.RequestServices.GetRequiredService<LocationAccessService>();
                    LocationScope.Current = await access.ResolveEffectiveLocationAsync(BusinessId, UserId, role, requested);
                }
            }
            else
            {
                LocationScope.Current = null;
            }

            await next();
        }
        finally
        {
            LocationScope.Current = null; // never leak scope across requests (pooled threads)
        }
    }
}
