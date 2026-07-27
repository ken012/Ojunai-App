using Ojunai.API.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Ojunai.API.Controllers;

[Authorize]
[ApiController]
public abstract class OjunaiBaseController : ControllerBase, IActionFilter
{
    protected Guid BusinessId => User.GetBusinessId();
    protected Guid UserId => User.GetUserId();

    /// <summary>
    /// Multi-location (Phase 2b): capture the X-Location-Id header into the ambient <see cref="LocationScope"/>
    /// so the AppDbContext dual-write mirror routes a multi-location business's stock change to the selected
    /// location. Set explicitly each action (to the parsed value or null) so no stale value can leak. The id
    /// is validated against the business inside the mirror; absent/invalid ⇒ default location = today's behaviour.
    /// </summary>
    public void OnActionExecuting(ActionExecutingContext context)
    {
        var header = Request.Headers["X-Location-Id"].FirstOrDefault();
        LocationScope.Current = Guid.TryParse(header, out var locId) ? locId : null;
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}
