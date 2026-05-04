using Ojunai.API.Common;
using Ojunai.API.Data;
using Ojunai.API.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Controllers;

[Route("api/export")]
[ApiController]
public class ExportController : ControllerBase
{
    private readonly IPdfExportService _pdf;
    private readonly IConfiguration _config;
    private readonly ILogger<ExportController> _logger;
    private readonly AppDbContext _db;

    public ExportController(IPdfExportService pdf, IConfiguration config, ILogger<ExportController> logger, AppDbContext db)
    {
        _pdf = pdf;
        _config = config;
        _logger = logger;
        _db = db;
    }

    [HttpGet("download")]
    [AllowAnonymous]
    public IActionResult Download([FromQuery] string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return BadRequest("Missing token.");

        var secret = _config["Jwt:Secret"]!;
        var payload = ExportTokenHelper.ValidateToken(token, secret);
        if (payload == null)
            return Content(PinPage(token, "This download link is invalid or has expired."), "text/html");

        return Content(PinPage(token, null), "text/html");
    }

    [HttpPost("download")]
    [AllowAnonymous]
    public async Task<IActionResult> DownloadWithPin([FromForm] string? token, [FromForm] string? pin)
    {
        if (string.IsNullOrWhiteSpace(token))
            return BadRequest("Missing token.");

        var secret = _config["Jwt:Secret"]!;
        var payload = ExportTokenHelper.ValidateToken(token, secret);
        if (payload == null)
            return Content(PinPage(token!, "This download link is invalid or has expired."), "text/html");

        var user = await _db.Users
            .Include(u => u.Business)
            .FirstOrDefaultAsync(u => u.Id == payload.UserId && u.IsActive);

        if (user == null)
            return Content(PinPage(token!, "Account not found."), "text/html");

        var expectedPin = DerivePin(user.Business.AccountNumber, user.DateOfBirth);

        if (string.IsNullOrWhiteSpace(pin) || pin.Trim() != expectedPin)
            return Content(PinPage(token!, "Incorrect PIN. Please try again."), "text/html");

        return await ServePdf(payload);
    }

    private async Task<IActionResult> ServePdf(ExportTokenPayload payload)
    {
        try
        {
            var pdf = await _pdf.GenerateReportPdfAsync(payload.BusinessId, payload.ReportType, payload.From, payload.To);
            var filename = $"Ojunai-{Capitalize(payload.ReportType)}-{payload.From:yyyyMMdd}-{payload.To:yyyyMMdd}.pdf";
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

    internal static string DerivePin(string accountNumber, DateOnly? dob)
    {
        if (string.IsNullOrEmpty(accountNumber) || accountNumber.Length < 4) return "0000";
        if (dob == null) return accountNumber[^4..];
        var acctLast2 = accountNumber[^2..];
        var yearLast2 = (dob.Value.Year % 100).ToString("D2");
        return acctLast2 + yearLast2;
    }

    private static string Capitalize(string s) => s switch
    {
        "sales" => "Sales-Report",
        "expenses" => "Expenses-Report",
        "monthly-pnl" => "PnL-Statement",
        _ => s
    };

    private static string PinPage(string token, string? error)
    {
        var errorHtml = error != null
            ? $"<p style=\"color:#dc2626;font-size:14px;margin-bottom:16px\">{error}</p>"
            : "";
        var isExpired = error?.Contains("expired") == true || error?.Contains("invalid") == true;
        var formHtml = isExpired ? "" : $@"
            <form method=""post"" action=""/api/export/download"">
                <input type=""hidden"" name=""token"" value=""{System.Net.WebUtility.HtmlEncode(token)}"" />
                <label style=""display:block;font-size:14px;color:#475569;margin-bottom:8px"">Enter your 4-digit PIN to download</label>
                <p style=""font-size:12px;color:#94a3b8;margin-bottom:12px"">Your 4-digit security PIN</p>
                <input type=""text"" name=""pin"" maxlength=""4"" pattern=""\d{{4}}"" inputmode=""numeric""
                    style=""width:120px;height:48px;font-size:24px;text-align:center;letter-spacing:8px;border:2px solid #e2e8f0;border-radius:8px;outline:none;display:block;margin:0 auto 16px""
                    autofocus required />
                <button type=""submit""
                    style=""width:100%;padding:12px;background:#0ea5e9;color:white;border:none;border-radius:8px;font-size:16px;font-weight:600;cursor:pointer"">
                    Download Report
                </button>
            </form>";

        return $@"<!DOCTYPE html>
<html><head>
<meta charset=""utf-8"" /><meta name=""viewport"" content=""width=device-width,initial-scale=1"" />
<title>Ojunai — Download Report</title>
</head><body style=""margin:0;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;background:#f8fafc;display:flex;align-items:center;justify-content:center;min-height:100vh"">
<div style=""background:white;border-radius:16px;padding:32px;max-width:360px;width:100%;box-shadow:0 1px 3px rgba(0,0,0,0.1);text-align:center"">
    <div style=""font-size:20px;font-weight:700;color:#0f172a;margin-bottom:4px"">Ojunai</div>
    <p style=""color:#64748b;font-size:13px;margin-bottom:24px"">Secure Report Download</p>
    {errorHtml}
    {formHtml}
</div>
</body></html>";
    }
}
