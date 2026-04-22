using BizPilot.API.Common;
using BizPilot.API.DTOs.Sales;
using BizPilot.API.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BizPilot.API.Controllers;

[Route("api/sales")]
public class SalesController : BizPilotBaseController
{
    private readonly ISalesService _sales;
    private readonly Data.AppDbContext _db;
    private readonly IWhatsAppService _whatsApp;
    private readonly ILogger<SalesController> _logger;

    public SalesController(ISalesService sales, Data.AppDbContext db, IWhatsAppService whatsApp, ILogger<SalesController> logger)
    {
        _sales = sales;
        _db = db;
        _whatsApp = whatsApp;
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
