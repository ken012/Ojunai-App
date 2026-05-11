using System.Text;
using Ojunai.API.Common;
using Ojunai.API.Data;
using Ojunai.API.Models;
using Ojunai.API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Services.Channels;

/// <summary>
/// Default implementation of <see cref="IChatQueryService"/>. Reuses the same IReportService /
/// ILedgerService / AppDbContext queries that <see cref="Ojunai.API.Services.WhatsAppService"/>
/// uses inline, and formats output with the same WhatsApp-style markdown so the user experience
/// is consistent across channels.
///
/// Currency symbol and timezone are derived per-call from the business record — we don't cache
/// them on the service because it's scoped and could serve multiple businesses across a request
/// scope's lifetime in pathological cases (mostly defensive).
/// </summary>
public sealed class ChatQueryService : IChatQueryService
{
    private readonly AppDbContext _db;
    private readonly IReportService _reports;
    private readonly ILedgerService _ledger;

    public ChatQueryService(AppDbContext db, IReportService reports, ILedgerService ledger)
    {
        _db = db;
        _reports = reports;
        _ledger = ledger;
    }

    public async Task<string> GetTodaySalesAsync(Guid businessId, CancellationToken ct = default)
    {
        var cs = await CurrencySymbolAsync(businessId, ct);
        var s = await _reports.GetDailySummaryAsync(businessId, null);
        return $"📊 *Today's Sales*\nRevenue: {cs}{s.TotalSales:N0} ({s.SaleCount} transactions)\nExpenses: {cs}{s.TotalExpenses:N0}\nNet: {cs}{s.NetCashIn:N0}";
    }

    public async Task<string> GetWeekSalesAsync(Guid businessId, CancellationToken ct = default)
    {
        var cs = await CurrencySymbolAsync(businessId, ct);
        var s = await _reports.GetWeeklySummaryAsync(businessId, null);
        var top = s.TopProducts.Count > 0
            ? $"\n🏆 Top: {s.TopProducts[0].ProductName} ({cs}{s.TopProducts[0].TotalRevenue:N0})"
            : "";
        return $"📊 *This Week ({s.WeekStart} – {s.WeekEnd})*\nSales: {cs}{s.TotalSales:N0}\nExpenses: {cs}{s.TotalExpenses:N0}\nEst. Profit: {cs}{s.EstimatedProfit:N0}" + top + "\n\n📥 Download full report: app.ojunai.com/export";
    }

    public async Task<string> GetAllStockAsync(Guid businessId, bool showPrices, CancellationToken ct = default)
    {
        var cs = await CurrencySymbolAsync(businessId, ct);

        var items = await _db.Products
            .Where(p => p.BusinessId == businessId && p.IsActive)
            .OrderBy(p => p.Name)
            .ToListAsync(ct);

        if (items.Count == 0) return "You have no products set up yet.";

        // One query for all active stock holds so the per-product map below is cheap.
        var activeHolds = await _db.StockHolds
            .Where(h => h.BusinessId == businessId && h.Status == HoldStatus.Active)
            .GroupBy(h => h.ProductId)
            .Select(g => new { ProductId = g.Key, HeldQty = g.Sum(h => h.Quantity) })
            .ToDictionaryAsync(x => x.ProductId, x => x.HeldQty, ct);

        var lines = items.Select(p =>
        {
            var held = activeHolds.GetValueOrDefault(p.Id, 0);
            var flag = p.CurrentStock <= p.LowStockThreshold ? " ⚠️" : "";
            var holdStr = held > 0 ? $" ({held:0.##} on hold, {(p.CurrentStock - held):0.##} avail)" : "";
            var priceStr = "";
            if (showPrices)
            {
                var prices = new List<string>();
                if (p.SellingPrice.HasValue) prices.Add($"Sell: {cs}{p.SellingPrice.Value:N0}");
                if (p.CostPrice.HasValue) prices.Add($"Cost: {cs}{p.CostPrice.Value:N0}");
                if (prices.Count > 0) priceStr = $" — {string.Join(" | ", prices)}";
            }
            return $"• {p.Name}: {p.CurrentStock:0.##} {p.Unit}{holdStr}{flag}{priceStr}";
        });
        return $"📦 *Stock Levels*\n{string.Join("\n", lines)}";
    }

    public async Task<string> GetLowStockAsync(Guid businessId, CancellationToken ct = default)
    {
        var cs = await CurrencySymbolAsync(businessId, ct);

        var items = await _db.Products
            .Where(p => p.BusinessId == businessId && p.IsActive && p.CurrentStock <= p.LowStockThreshold)
            .OrderBy(p => p.CurrentStock)
            .ToListAsync(ct);

        if (items.Count == 0) return "✅ All products have sufficient stock.";
        var lines = items.Select(p =>
        {
            var priceStr = p.SellingPrice.HasValue ? $" — {cs}{p.SellingPrice.Value:N0}" : "";
            return $"• {p.Name}: {p.CurrentStock:0.##} {p.Unit} (min: {p.LowStockThreshold:0.##}){priceStr}";
        });
        return $"⚠️ *Low Stock* ({items.Count} items)\n{string.Join("\n", lines)}";
    }

    public async Task<string> GetTodayExpensesAsync(Guid businessId, CancellationToken ct = default)
    {
        var cs = await CurrencySymbolAsync(businessId, ct);
        var todayUtc = DateTime.UtcNow.Date;

        var expenses = await _db.Expenses
            .Where(e => e.BusinessId == businessId && e.CreatedAtUtc >= todayUtc)
            .OrderByDescending(e => e.CreatedAtUtc)
            .Take(10)
            .ToListAsync(ct);

        if (expenses.Count == 0) return "No expenses recorded today.";

        var lines = expenses.Select(e =>
            $"• {e.Category} — {cs}{e.Amount:N0}" + (e.Notes != null ? $" ({e.Notes})" : ""));
        var total = expenses.Sum(e => e.Amount);
        return $"💸 *Today's Expenses* ({expenses.Count} items)\n{string.Join("\n", lines)}\n\n*Total: {cs}{total:N0}*";
    }

    public async Task<string> GetRecentExpensesAsync(Guid businessId, CancellationToken ct = default)
    {
        var (cs, tz) = await CurrencyAndTzAsync(businessId, ct);
        var sevenDaysAgo = DateTime.UtcNow.Date.AddDays(-6);

        var expenses = await _db.Expenses
            .Where(e => e.BusinessId == businessId && e.CreatedAtUtc >= sevenDaysAgo)
            .OrderByDescending(e => e.CreatedAtUtc)
            .Take(10)
            .ToListAsync(ct);

        if (expenses.Count == 0) return "No expenses in the last 7 days.";

        var lines = expenses.Select(e =>
        {
            var date = TimeZoneInfo.ConvertTimeFromUtc(e.CreatedAtUtc, tz).ToString("MMM d");
            return $"• {date} — {e.Category} — {cs}{e.Amount:N0}" + (e.PaidTo != null ? $" (to {e.PaidTo})" : "");
        });
        var total = expenses.Sum(e => e.Amount);
        return $"💸 *Recent Expenses* (last 7 days)\n{string.Join("\n", lines)}\n\n*Total: {cs}{total:N0}*";
    }

    public async Task<string> GetOutstandingAsync(Guid businessId, string type, CancellationToken ct = default)
    {
        var cs = await CurrencySymbolAsync(businessId, ct);
        var balances = await _ledger.GetOutstandingBalancesAsync(businessId, type);

        if (balances.Count == 0)
            return type == "receivable" ? "No outstanding receivables." : "No outstanding payables.";

        var title = type == "receivable" ? "💰 Outstanding Receivables" : "💸 Outstanding Payables";
        var total = type == "receivable" ? balances.Sum(b => b.TotalReceivable) : balances.Sum(b => b.TotalPayable);

        var lines = balances
            .OrderByDescending(b => type == "receivable" ? b.TotalReceivable : b.TotalPayable)
            .Select(b => type == "receivable"
                ? $"• {b.ContactName}: {cs}{b.TotalReceivable:N0}"
                : $"• {b.ContactName}: {cs}{b.TotalPayable:N0}");

        var countNote = balances.Count > 1 ? $" ({balances.Count} contacts)" : "";
        return $"{title}{countNote}\n{string.Join("\n", lines)}\n\n*Total: {cs}{total:N0}*\n\n📥 Download full report: app.ojunai.com/export";
    }

    public async Task<string> GetDailySummaryAsync(Guid businessId, CancellationToken ct = default)
    {
        var (cs, tz) = await CurrencyAndTzAsync(businessId, ct);
        var summary = await _reports.GetDailySummaryAsync(businessId, null);
        var net = summary.TotalSales - summary.TotalExpenses;
        var netEmoji = net >= 0 ? "📈" : "📉";

        var sb = new StringBuilder();
        sb.AppendLine($"📊 *Daily Summary — {TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz):MMM d, yyyy}*");
        sb.AppendLine($"🛒 Sales: {cs}{summary.TotalSales:N0} ({summary.SaleCount} transactions)");
        sb.AppendLine($"💸 Expenses: {cs}{summary.TotalExpenses:N0}");
        sb.AppendLine($"{netEmoji} Net: {cs}{net:N0}");

        if (summary.LowStockCount > 0)
            sb.AppendLine($"⚠️ {summary.LowStockCount} product{(summary.LowStockCount != 1 ? "s" : "")} running low");

        if (summary.OutstandingReceivables > 0)
            sb.AppendLine($"💰 Outstanding receivables: {cs}{summary.OutstandingReceivables:N0}");

        if (summary.OutstandingPayables > 0)
            sb.AppendLine($"💳 Outstanding payables: {cs}{summary.OutstandingPayables:N0}");

        if (summary.SaleCount == 0)
            sb.AppendLine("🔕 No sales recorded today.");

        var todayUtc = DateTime.UtcNow.Date;
        var topProduct = await _db.SaleItems
            .Include(i => i.Sale).Include(i => i.Product)
            .Where(i => i.Sale.BusinessId == businessId && i.Sale.CreatedAtUtc >= todayUtc)
            .GroupBy(i => i.Product.Name)
            .Select(g => new { Name = g.Key, Rev = g.Sum(i => i.TotalPrice) })
            .OrderByDescending(p => p.Rev)
            .FirstOrDefaultAsync(ct);

        if (topProduct != null)
            sb.AppendLine($"🏆 Top today: {topProduct.Name} ({cs}{topProduct.Rev:N0})");

        sb.AppendLine();
        sb.AppendLine("📥 Download full report: app.ojunai.com/export");

        return sb.ToString().TrimEnd();
    }

    public async Task<string> GetContactBalanceAsync(Guid businessId, string? contactName, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(contactName))
            return await GetOutstandingAsync(businessId, "receivable", ct);

        var cs = await CurrencySymbolAsync(businessId, ct);

        // Exact-match first, then prefix-match as a forgiving fallback. Matches WhatsApp's lookup.
        var contact = await _db.Contacts.FirstOrDefaultAsync(c =>
            c.BusinessId == businessId && EF.Functions.ILike(c.Name, contactName), ct);
        if (contact == null)
        {
            contact = await _db.Contacts.FirstOrDefaultAsync(c =>
                c.BusinessId == businessId && EF.Functions.ILike(c.Name, $"{contactName} %"), ct);
        }
        if (contact == null) return $"Contact '{contactName}' not found.";

        var entries = await _db.LedgerEntries
            .Where(e => e.ContactId == contact.Id && e.BusinessId == businessId)
            .ToListAsync(ct);

        var receivable = entries.Where(e => e.EntryType == LedgerEntryType.Receivable).Sum(e => e.Amount)
                       - entries.Where(e => e.EntryType == LedgerEntryType.ReceivablePayment).Sum(e => e.Amount);
        var payable = entries.Where(e => e.EntryType == LedgerEntryType.Payable).Sum(e => e.Amount)
                   - entries.Where(e => e.EntryType == LedgerEntryType.PayablePayment).Sum(e => e.Amount);

        if (receivable <= 0 && payable <= 0) return $"{contactName} has no outstanding balance.";

        var parts = new List<string>();
        if (receivable > 0) parts.Add($"owes you {cs}{receivable:N0}");
        if (payable > 0) parts.Add($"you owe {cs}{payable:N0}");
        return $"💼 {contactName}: {string.Join(", ", parts)}";
    }

    public async Task<string> GetCashPositionAsync(Guid businessId, CancellationToken ct = default)
    {
        var cs = await CurrencySymbolAsync(businessId, ct);
        var pos = await _reports.GetCashPositionAsync(businessId);
        return $"📈 *This Month*\nSales: {cs}{pos.TotalSalesThisMonth:N0}\nExpenses: {cs}{pos.TotalExpensesThisMonth:N0}\nEst. Cash In: {cs}{pos.EstimatedCashIn:N0}\nReceivables: {cs}{pos.OutstandingReceivables:N0}\nPayables: {cs}{pos.OutstandingPayables:N0}\n*Net: {cs}{pos.NetPosition:N0}*\n\n📥 Download full report: app.ojunai.com/export";
    }

    public string GetHelpText() =>
        "*What I can do*\n\n" +
        "_Record:_\n" +
        "• \"sold 2 X for 5000\" — record a sale\n" +
        "• \"paid 3000 for printing\" — log an expense\n" +
        "• \"Mary paid 5000\" — record a customer payment\n\n" +
        "_Ask:_\n" +
        "• \"stock\" or \"inventory\" — current stock levels\n" +
        "• \"low stock\" — products running low\n" +
        "• \"today's sales\" — today's revenue\n" +
        "• \"this week\" — weekly summary\n" +
        "• \"today's expenses\" — today's expense list\n" +
        "• \"who owes me\" — outstanding receivables\n" +
        "• \"who do I owe\" — outstanding payables\n" +
        "• \"summary\" — today's full snapshot\n" +
        "• \"this month\" — month-to-date cash position";

    public string GetGreetText(string? businessName) =>
        string.IsNullOrEmpty(businessName)
            ? "👋 Hey! I can record sales, expenses, and payments — or answer questions like \"today's sales\" or \"low stock\". Say *help* to see everything."
            : $"👋 Hey! Welcome to {businessName}'s assistant. I can record sales, expenses, and payments — or answer questions like \"today's sales\" or \"low stock\". Say *help* to see everything.";

    // ── Helpers ────────────────────────────────────────────────────────────────

    private async Task<string> CurrencySymbolAsync(Guid businessId, CancellationToken ct)
    {
        var currency = await _db.Businesses
            .AsNoTracking()
            .Where(b => b.Id == businessId)
            .Select(b => b.Currency)
            .FirstOrDefaultAsync(ct) ?? "NGN";
        return BillingConfig.Symbol(currency);
    }

    private async Task<(string Symbol, TimeZoneInfo Tz)> CurrencyAndTzAsync(Guid businessId, CancellationToken ct)
    {
        var row = await _db.Businesses
            .AsNoTracking()
            .Where(b => b.Id == businessId)
            .Select(b => new { b.Currency, b.Timezone })
            .FirstOrDefaultAsync(ct);

        var cs = BillingConfig.Symbol(row?.Currency ?? "NGN");
        TimeZoneInfo tz;
        try
        {
            tz = TimeZoneInfo.FindSystemTimeZoneById(row?.Timezone ?? "Africa/Lagos");
        }
        catch
        {
            tz = TimeZoneInfo.Utc;
        }
        return (cs, tz);
    }
}
