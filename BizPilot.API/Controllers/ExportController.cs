using BizPilot.API.Common;
using BizPilot.API.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BizPilot.API.Controllers;

[Route("api/export")]
[ApiController]
public class ExportController : ControllerBase
{
    private readonly IPdfExportService _pdf;
    private readonly IConfiguration _config;
    private readonly ILogger<ExportController> _logger;

    public ExportController(IPdfExportService pdf, IConfiguration config, ILogger<ExportController> logger)
    {
        _pdf = pdf;
        _config = config;
        _logger = logger;
    }

    [HttpGet("download")]
    [AllowAnonymous]
    public async Task<IActionResult> Download([FromQuery] string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return BadRequest("Missing token.");

        var secret = _config["Jwt:Secret"]!;
        var payload = ExportTokenHelper.ValidateToken(token, secret);
        if (payload == null)
            return Unauthorized("Invalid or expired download link.");

        try
        {
            var pdf = await _pdf.GenerateReportPdfAsync(payload.BusinessId, payload.ReportType, payload.From, payload.To);
            var filename = $"BizPilot-{Capitalize(payload.ReportType)}-{payload.From:yyyyMMdd}-{payload.To:yyyyMMdd}.pdf";
            return File(pdf, "application/pdf", filename);
        }
        catch (KeyNotFoundException)
        {
            return NotFound("Business not found.");
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PDF generation failed for business {BusinessId}, report {ReportType}", payload.BusinessId, payload.ReportType);
            return StatusCode(500, "Failed to generate report. Please try again.");
        }
    }

    private static string Capitalize(string s) => s switch
    {
        "sales" => "Sales-Report",
        "expenses" => "Expenses-Report",
        "monthly-pnl" => "PnL-Statement",
        _ => s
    };
}
