using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Ojunai.API.Controllers;
using Xunit;

namespace Ojunai.Tests.Security;

/// <summary>
/// Guards against a class of startup-only crash that the unit suite (InMemory, never boots the web host)
/// can't otherwise see. On 2026-07-28 a deploy crash-looped prod: <c>OjunaiBaseController</c> implements
/// <see cref="Microsoft.AspNetCore.Mvc.Filters.IAsyncActionFilter"/>, whose public
/// <c>OnActionExecutionAsync(ActionExecutingContext, ActionExecutionDelegate)</c> hook — without
/// <c>[NonAction]</c> — is treated by MVC as a routable action with two complex parameters that both infer
/// <c>[FromBody]</c>. <c>MapControllers()</c> then throws "more than one parameter bound from body" at
/// startup, taking down EVERY controller that inherits the base. This test fails fast if any controller
/// exposes a filter hook as an action.
/// </summary>
public class StartupSafetyTests
{
    // Public method names of the MVC filter interfaces a controller might implement on itself.
    private static readonly HashSet<string> FilterHookMethodNames = new()
    {
        "OnActionExecutionAsync", "OnActionExecuting", "OnActionExecuted",
        "OnResultExecutionAsync", "OnResultExecuting", "OnResultExecuted",
        "OnException", "OnExceptionAsync",
    };

    [Fact]
    public void ControllerFilterHooks_AreMarkedNonAction()
    {
        var asm = typeof(OjunaiBaseController).Assembly;
        var offenders = new List<string>();
        foreach (var t in asm.GetTypes().Where(t => typeof(ControllerBase).IsAssignableFrom(t)))
        {
            // DeclaredOnly: a hook defined on a base (e.g. OjunaiBaseController) is checked once, on that base.
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (!FilterHookMethodNames.Contains(m.Name)) continue;
                if (m.GetCustomAttribute<NonActionAttribute>() == null)
                    offenders.Add($"{t.Name}.{m.Name}");
            }
        }

        Assert.True(offenders.Count == 0,
            "These controller filter hooks are NOT marked [NonAction] — MVC will treat them as actions and " +
            "crash MapControllers at startup: " + string.Join(", ", offenders));
    }
}
