using BizPilot.API.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BizPilot.API.Controllers;

[Authorize]
[ApiController]
public abstract class BizPilotBaseController : ControllerBase
{
    protected Guid BusinessId => User.GetBusinessId();
    protected Guid UserId => User.GetUserId();
}
