using Ojunai.API.Common;
using Ojunai.API.DTOs.Sales;
using Ojunai.API.Services;
using Ojunai.API.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Controllers;

[Route("api/sales")]
public class SalesController : OjunaiBaseController
{
    private readonly ISalesService _sales;
    private readonly Data.AppDbContext _db;
    private readonly IWhatsAppService _whatsApp;
    private readonly IReceiptService _receipts;
    private readonly IEmailService _email;
    private readonly ILogger<SalesController> _logger;

    public SalesController(
        ISalesService sales,
        Data.AppDbContext db,
        IWhatsAppService whatsApp,
        IReceiptService receipts,
        IEmailService email,
        ILogger<SalesController> logger)
    {
        _sales = sales;
        _db = db;
        _whatsApp = whatsApp;
        _receipts = receipts;
        _email = email;
        _logger = logger;
    }

    [HttpPost]
    [RequirePermission(Permission.RecordSales)]
    public async Task<ActionResult<ApiResponse<SaleDto>>> Create([FromBody] CreateSaleRequest request)
    {
        var user = await _db.Users.FindAsync(UserId);
        var result = await _sales.CreateAsync(BusinessId, request, "Manual", user?.Id, user?.FullName);

        // Fire alerts (low stock + large sale) — same as WhatsApp flow
        // Fire alerts in background — don't block the sale response if alerts fail
        _ = Task.Run(async () =>
        {
            try { await FireDashboardAlertsAsync(result.TotalAmount); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to fire dashboard alerts after sale"); }
        });

        return CreatedAtAction(nameof(GetById), new { id = result.Id },
            ApiResponse<SaleDto>.Ok(result, "Sale recorded."));
    }

    private async Task FireDashboardAlertsAsync(decimal saleAmount)
    {
        var business = await _db.Businesses.Include(b => b.Users).FirstOrDefaultAsync(b => b.Id == BusinessId);
        if (business == null) return;

        var owner = business.Users.FirstOrDefault(u => u.Role == Models.UserRole.Owner && u.IsActive);
        if (owner == null) return;

        var alerts = new List<string>();

        // Low stock
        if (business.AlertLowStock)
        {
            var lowStock = await _db.Products
                .Where(p => p.BusinessId == BusinessId && p.IsActive && p.CurrentStock <= p.LowStockThreshold)
                .OrderBy(p => p.CurrentStock).Take(5).ToListAsync();

            foreach (var p in lowStock)
            {
                if (p.CurrentStock <= 0)
                    alerts.Add($"🚫 *{p.Name}* is out of stock — reorder now!");
                else
                    alerts.Add($"⚠️ *{p.Name}* is running low — {p.CurrentStock:0.##} {p.Unit} left");
            }
        }

        // Large sale
        if (business.AlertLargeSale && business.LargeSaleThreshold > 0 && saleAmount >= business.LargeSaleThreshold)
        {
            var cs = BillingConfig.Symbol(business.Currency);
            alerts.Add($"💰 *Big sale!* {cs}{saleAmount:N0} just recorded from dashboard");
        }

        if (alerts.Count > 0)
        {
            var msg = $"🔔 *Alerts*\n{string.Join("\n", alerts)}";
            await _whatsApp.SendMessageAsync($"whatsapp:{owner.PhoneNumber}", msg, BusinessId, owner.Id);
        }
    }

    [HttpGet]
    [RequirePermission(Permission.ViewOwnReports)]
    public async Task<ActionResult<ApiResponse<PaginatedResult<SaleSummaryDto>>>> GetAll(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null,
        [FromQuery] string? paymentStatus = null, [FromQuery] string? paymentMethod = null,
        [FromQuery] string? source = null, [FromQuery] Guid? customerId = null, [FromQuery] string? search = null)
    {
        var result = await _sales.GetAllAsync(BusinessId, page, pageSize, from, to, paymentStatus, paymentMethod, source, customerId, search);
        return Ok(ApiResponse<PaginatedResult<SaleSummaryDto>>.Ok(result));
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(Permission.ViewOwnReports)]
    public async Task<ActionResult<ApiResponse<SaleDto>>> GetById(Guid id)
    {
        var result = await _sales.GetByIdAsync(BusinessId, id);
        return Ok(ApiResponse<SaleDto>.Ok(result));
    }

    /// <summary>
    /// Generate (or re-fetch) a PDF receipt for the sale. First call assigns a sequential
    /// receipt number; subsequent calls reuse it.
    /// </summary>
    [HttpGet("{id:guid}/receipt")]
    [RequirePermission(Permission.ViewOwnReports)]
    public async Task<IActionResult> Receipt(Guid id)
    {
        var (bytes, receiptNumber) = await _receipts.GenerateAsync(id, BusinessId);
        var safeName = receiptNumber.Replace("/", "_");
        return File(bytes, "application/pdf", $"{safeName}.pdf");
    }

    /// <summary>
    /// Email the receipt PDF to a recipient. Returns 503 if SMTP isn't configured.
    /// </summary>
    [HttpPost("{id:guid}/receipt/email")]
    [RequirePermission(Permission.ViewOwnReports)]
    public async Task<IActionResult> EmailReceipt(Guid id, [FromBody] EmailReceiptRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.To) || !request.To.Contains('@'))
            return BadRequest(ApiResponse<object>.Fail("Provide a valid recipient email address."));

        if (!_email.IsConfigured)
        {
            return StatusCode(503, ApiResponse<object>.Fail(
                "Email is not configured for this server. Ask your admin to set the Email__* environment variables."));
        }

        var (bytes, receiptNumber) = await _receipts.GenerateAsync(id, BusinessId);
        var sale = await _db.Sales.Include(s => s.Contact)
            .FirstOrDefaultAsync(s => s.Id == id && s.BusinessId == BusinessId)
            ?? throw new KeyNotFoundException("Sale not found.");
        var business = await _db.Businesses.FindAsync(BusinessId)
            ?? throw new KeyNotFoundException("Business not found.");

        var customerName = sale.Contact?.Name ?? "there";
        var subject = $"Your receipt from {business.Name} ({receiptNumber})";
        var html = $@"
<!DOCTYPE html>
<html>
<body style=""font-family: -apple-system, BlinkMacSystemFont, Segoe UI, Roboto, sans-serif; color: #1e293b; max-width: 560px; margin: 0 auto; padding: 24px;"">
  <h2 style=""color: #0f172a; margin: 0 0 16px;"">Thank you, {System.Net.WebUtility.HtmlEncode(customerName)}</h2>
  <p style=""color: #475569; line-height: 1.6;"">Here is your receipt from <strong>{System.Net.WebUtility.HtmlEncode(business.Name)}</strong>.</p>
  <div style=""background: #f1f5f9; border-radius: 8px; padding: 16px; margin: 16px 0;"">
    <div style=""font-size: 11px; font-weight: bold; color: #06b6d4; letter-spacing: 1.2px;"">RECEIPT</div>
    <div style=""font-size: 16px; font-weight: bold; margin-top: 4px;"">{receiptNumber}</div>
    <div style=""font-size: 12px; color: #94a3b8; margin-top: 4px;"">Total: {BillingConfig.Symbol(business.Currency)}{sale.TotalAmount:N2}</div>
  </div>
  <p style=""color: #475569; line-height: 1.6;"">The full receipt is attached as a PDF.</p>
  <p style=""color: #94a3b8; font-size: 12px; margin-top: 24px; border-top: 1px solid #e2e8f0; padding-top: 16px;"">
    Sent by {System.Net.WebUtility.HtmlEncode(business.Name)} via Ojunai.
  </p>
</body>
</html>";

        try
        {
            await _email.SendAsync(
                toAddress: request.To.Trim(),
                toName: customerName,
                subject: subject,
                htmlBody: html,
                attachments: new[] { new EmailAttachment($"{receiptNumber}.pdf", bytes, "application/pdf") }
            );
            return Ok(ApiResponse<object>.Ok(null!, $"Receipt sent to {request.To}."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to email receipt {ReceiptNumber}", receiptNumber);
            return StatusCode(500, ApiResponse<object>.Fail("Failed to send email. Check server logs."));
        }
    }

    public class EmailReceiptRequest
    {
        public string To { get; set; } = string.Empty;
    }

    [HttpPost("{id:guid}/void")]
    [RequirePermission(Permission.VoidSales)]
    public async Task<ActionResult<ApiResponse<object>>> Void(Guid id)
    {
        var user = await _db.Users.FindAsync(UserId);
        await _sales.VoidAsync(BusinessId, id, user?.Id, user?.FullName);
        return Ok(ApiResponse<object>.Ok(null!, "Sale voided. Stock restored."));
    }

    [HttpPost("{id:guid}/return")]
    [RequirePermission(Permission.VoidSales)]
    public async Task<ActionResult<ApiResponse<object>>> Return(Guid id)
    {
        var user = await _db.Users.FindAsync(UserId);
        await _sales.ReturnAsync(BusinessId, id, user?.Id, user?.FullName);
        return Ok(ApiResponse<object>.Ok(null!, "Sale returned. Stock restored."));
    }

    [HttpGet("voided")]
    [RequirePermission(Permission.VoidSales)]
    public async Task<ActionResult<ApiResponse<PaginatedResult<SaleSummaryDto>>>> GetVoided(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var result = await _sales.GetVoidedAsync(BusinessId, page, pageSize);
        return Ok(ApiResponse<PaginatedResult<SaleSummaryDto>>.Ok(result));
    }

    [HttpGet("returned")]
    [RequirePermission(Permission.VoidSales)]
    public async Task<ActionResult<ApiResponse<PaginatedResult<SaleSummaryDto>>>> GetReturned(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var result = await _sales.GetReturnedAsync(BusinessId, page, pageSize);
        return Ok(ApiResponse<PaginatedResult<SaleSummaryDto>>.Ok(result));
    }
}
