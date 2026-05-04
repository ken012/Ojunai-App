using Ojunai.API.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ojunai.API.Controllers;

[Authorize]
[ApiController]
public abstract class OjunaiBaseController : ControllerBase
{
    protected Guid BusinessId => User.GetBusinessId();
    protected Guid UserId => User.GetUserId();
}
