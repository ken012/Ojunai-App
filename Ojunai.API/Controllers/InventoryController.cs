using Ojunai.API.Common;
using Ojunai.API.Data;
using Ojunai.API.DTOs.Inventory;
using Ojunai.API.Models;
using Ojunai.API.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Controllers;

[Route("api/inventory")]
public class InventoryController : OjunaiBaseController
{
    private readonly IInventoryService _inventory;
    private readonly AppDbContext _db;
    private readonly IWhatsAppService _whatsApp;
    private readonly ILogger<InventoryController> _logger;

    public InventoryController(IInventoryService inventory, AppDbContext db, IWhatsAppService whatsApp, ILogger<InventoryController> logger)
    {
        _inventory = inventory;
        _db = db;
        _whatsApp = whatsApp;
        _logger = logger;
    }

    [HttpPost("stock-in")]
    [RequirePermission(Permission.ManageStock)]
    public async Task<ActionResult<ApiResponse<InventoryTransactionDto>>> StockIn([FromBody] StockInRequest request)
    {
        var user = await _db.Users.FindAsync(UserId);
        var result = await _inventory.StockInAsync(BusinessId, request, user?.Id, user?.FullName);
        return Ok(ApiResponse<InventoryTransactionDto>.Ok(result, "Stock added."));
    }

    [HttpPost("stock-out")]
    [RequirePermission(Permission.ManageStock)]
    public async Task<ActionResult<ApiResponse<InventoryTransactionDto>>> StockOut([FromBody] StockOutRequest request)
    {
        var user = await _db.Users.FindAsync(UserId);
        var result = await _inventory.StockOutAsync(BusinessId, request, user?.Id, user?.FullName);
        _ = Task.Run(async () => { try { await FireStockAlertsAsync(); } catch (Exception ex) { _logger.LogError(ex, "Failed to fire stock alerts"); } });
        return Ok(ApiResponse<InventoryTransactionDto>.Ok(result, "Stock removed."));
    }

    [HttpPost("adjust")]
    [RequirePermission(Permission.ManageStock)]
    public async Task<ActionResult<ApiResponse<InventoryTransactionDto>>> Adjust([FromBody] AdjustmentRequest request)
    {
        var user = await _db.Users.FindAsync(UserId);
        var result = await _inventory.AdjustAsync(BusinessId, request, user?.Id, user?.FullName);
        return Ok(ApiResponse<InventoryTransactionDto>.Ok(result, "Stock adjusted."));
    }

    [HttpPost("damaged")]
    [RequirePermission(Permission.ManageStock)]
    public async Task<ActionResult<ApiResponse<InventoryTransactionDto>>> Damaged([FromBody] DamagedRequest request)
    {
        var user = await _db.Users.FindAsync(UserId);
        var result = await _inventory.MarkDamagedAsync(BusinessId, request, user?.Id, user?.FullName);
        _ = Task.Run(async () => { try { await FireStockAlertsAsync(); } catch (Exception ex) { _logger.LogError(ex, "Failed to fire stock alerts"); } });
        return Ok(ApiResponse<InventoryTransactionDto>.Ok(result, "Damaged stock recorded."));
    }

    [HttpPost("wastage")]
    [RequirePermission(Permission.ManageStock)]
    public async Task<ActionResult<ApiResponse<InventoryTransactionDto>>> Wastage([FromBody] DamagedRequest request)
    {
        var user = await _db.Users.FindAsync(UserId);
        var result = await _inventory.MarkWastageAsync(BusinessId, request, user?.Id, user?.FullName);
        _ = Task.Run(async () => { try { await FireStockAlertsAsync(); } catch (Exception ex) { _logger.LogError(ex, "Failed to fire stock alerts"); } });
        return Ok(ApiResponse<InventoryTransactionDto>.Ok(result, "Wastage recorded."));
    }

    [HttpPost("return")]
    [RequirePermission(Permission.ManageStock)]
    public async Task<ActionResult<ApiResponse<InventoryTransactionDto>>> Return([FromBody] ReturnRequest request)
    {
        var user = await _db.Users.FindAsync(UserId);
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == request.ProductId && p.BusinessId == BusinessId)
            ?? throw new KeyNotFoundException("Product not found.");

        var result = await _inventory.StockInAsync(BusinessId, new StockInRequest
        {
            ProductId = request.ProductId,
            Quantity = request.Quantity,
            Notes = $"Return{(!string.IsNullOrEmpty(request.CustomerName) ? $" from {request.CustomerName}" : "")}{(!string.IsNullOrEmpty(request.Notes) ? $" — {request.Notes}" : "")}"
        }, user?.Id, user?.FullName);

        return Ok(ApiResponse<InventoryTransactionDto>.Ok(result, $"{request.Quantity} {product.Unit} of {product.Name} returned to stock."));
    }

    [HttpGet("transactions")]
    [RequirePermission(Permission.ViewStock)]
    public async Task<ActionResult<ApiResponse<PaginatedResult<InventoryTransactionDto>>>> GetTransactions(
        [FromQuery] Guid? productId = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var result = await _inventory.GetTransactionsAsync(BusinessId, productId, page, pageSize);
        return Ok(ApiResponse<PaginatedResult<InventoryTransactionDto>>.Ok(result));
    }

    private async Task FireStockAlertsAsync()
    {
        var business = await _db.Businesses.Include(b => b.Users).FirstOrDefaultAsync(b => b.Id == BusinessId);
        if (business == null || !business.AlertLowStock) return;

        var owner = business.Users.FirstOrDefault(u => u.Role == UserRole.Owner && u.IsActive);
        if (owner == null) return;

        var lowStock = await _db.Products
            .Where(p => p.BusinessId == BusinessId && p.IsActive && p.CurrentStock <= p.LowStockThreshold)
            .OrderBy(p => p.CurrentStock).Take(5).ToListAsync();

        if (lowStock.Count == 0) return;

        var alerts = lowStock.Select(p =>
            p.CurrentStock <= 0
                ? $"🚫 *{p.Name}* is out of stock — reorder now!"
                : $"⚠️ *{p.Name}* is running low — {p.CurrentStock:0.##} {p.Unit} left").ToList();

        await _whatsApp.SendMessageAsync($"whatsapp:{owner.PhoneNumber}",
            $"🔔 *Alerts*\n{string.Join("\n", alerts)}", BusinessId, owner.Id);
    }
}
