using Ojunai.API.Common;
using Ojunai.API.DTOs.Alerts;
using Ojunai.API.Models;
using Ojunai.API.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Ojunai.API.Controllers;

[Route("api/alerts")]
public class AlertsController : OjunaiBaseController
{
    private readonly IAlertService _alerts;

    public AlertsController(IAlertService alerts) { _alerts = alerts; }

    private UserRole CurrentRole =>
        Enum.TryParse<UserRole>(User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value, out var r)
            ? r
            : UserRole.Viewer;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<AlertDto>>>> List(
        [FromQuery] bool unreadOnly = false,
        [FromQuery] int limit = 20)
    {
        if (limit < 1 || limit > 100) limit = 20;
        var rows = await _alerts.ListAsync(BusinessId, UserId, CurrentRole, unreadOnly, limit);
        var dto = rows.Select(ToDto).ToList();
        return Ok(ApiResponse<List<AlertDto>>.Ok(dto));
    }

    [HttpGet("unread-count")]
    public async Task<ActionResult<ApiResponse<UnreadCountResponse>>> UnreadCount()
    {
        var count = await _alerts.UnreadCountAsync(BusinessId, UserId, CurrentRole);
        return Ok(ApiResponse<UnreadCountResponse>.Ok(new UnreadCountResponse { Count = count }));
    }

    [HttpPost("{id:guid}/read")]
    public async Task<ActionResult<ApiResponse<object>>> Read(Guid id)
    {
        await _alerts.MarkReadAsync(BusinessId, UserId, CurrentRole, id);
        return Ok(ApiResponse<object>.Ok(null!));
    }

    [HttpPost("{id:guid}/dismiss")]
    public async Task<ActionResult<ApiResponse<object>>> Dismiss(Guid id)
    {
        await _alerts.DismissAsync(BusinessId, UserId, CurrentRole, id);
        return Ok(ApiResponse<object>.Ok(null!));
    }

    [HttpPost("mark-all-read")]
    public async Task<ActionResult<ApiResponse<object>>> MarkAllRead()
    {
        await _alerts.MarkAllReadAsync(BusinessId, UserId, CurrentRole);
        return Ok(ApiResponse<object>.Ok(null!));
    }

    private static AlertDto ToDto(Alert a) => new()
    {
        Id = a.Id,
        Type = a.Type.ToString(),
        Severity = a.Severity.ToString(),
        // UserId == null = business-wide (operational); UserId set = personal (security/privacy).
        Scope = a.UserId == null ? "Business" : "Personal",
        Title = a.Title,
        Body = a.Body,
        LinkUrl = a.LinkUrl,
        CreatedAtUtc = a.CreatedAtUtc,
        ReadAtUtc = a.ReadAtUtc,
    };
}
