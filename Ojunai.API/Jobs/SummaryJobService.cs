using Ojunai.API.Common;
using Ojunai.API.Data;
using Ojunai.API.Models;
using Ojunai.API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Jobs;

public class SummaryJobService
{
    private readonly AppDbContext _db;
    private readonly IReportService _reports;
    private readonly IWhatsAppService _whatsApp;
    private readonly ILogger<SummaryJobService> _logger;

    public SummaryJobService(
        AppDbContext db,
        IReportService reports,
        IWhatsAppService whatsApp,
        ILogger<SummaryJobService> logger)
    {
        _db = db;
        _reports = reports;
        _whatsApp = whatsApp;
        _logger = logger;
    }

    public async Task RunDailySummaryAsync()
    {
        await ComputeDailySummariesAsync();
        await SendDailySummariesAsync();
    }

    public async Task RunWeeklySummaryAsync() => await SendWeeklySummariesAsync();

    public async Task ComputeDailySummariesAsync()
    {
        _logger.LogInformation("Starting daily summary computation.");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var businesses = await _db.Businesses
            .Where(b => b.IsActive)
            .ToListAsync();

        foreach (var business in businesses)
        {
            try
            {
                await ComputeDailySummaryForBusinessAsync(business.Id, today);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Daily summary failed for business {BusinessId}", business.Id);
            }
        }

        _logger.LogInformation("Daily summaries computed for {Count} businesses.", businesses.Count);
    }

    public async Task SendDailySummariesAsync()
    {
        _logger.LogInformation("Starting daily summary WhatsApp notifications.");
        var businesses = await _db.Businesses
            .Include(b => b.Users)
            .Where(b => b.IsActive)
            .ToListAsync();

        foreach (var business in businesses)
        {
            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById(business.Timezone ?? "Africa/Lagos");
                var localHour = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).Hour;
                if (localHour != 20) continue; // Only send at 8 PM local time

                var cs = BillingConfig.Symbol(business.Currency);
                if (!business.AlertDailySummary) continue;

                var summary = await _reports.GetDailySummaryAsync(business.Id, null);
                var owner = business.Users.FirstOrDefault(u => u.Role == UserRole.Owner && u.IsActive);
                if (owner == null) continue;

                var net = summary.TotalSales - summary.TotalExpenses;
                var netEmoji = net >= 0 ? "📈" : "📉";

                var lowStockLine = summary.LowStockCount > 0
                    ? $"\n⚠️ {summary.LowStockCount} product{(summary.LowStockCount != 1 ? "s" : "")} running low"
                    : "";

                // Count how many contacts owe money
                var overdueContacts = await _db.LedgerEntries
                    .Include(e => e.Contact)
                    .Where(e => e.BusinessId == business.Id)
                    .GroupBy(e => e.Contact.Name)
                    .Select(g => new
                    {
                        Name = g.Key,
                        Net = g.Where(e => e.EntryType == LedgerEntryType.Receivable).Sum(e => e.Amount)
                            - g.Where(e => e.EntryType == LedgerEntryType.ReceivablePayment).Sum(e => e.Amount)
                    })
                    .Where(c => c.Net > 0)
                    .ToListAsync();

                var receivableLine = "";
                if (overdueContacts.Count > 0)
                {
                    var topDebtors = string.Join(", ", overdueContacts.OrderByDescending(c => c.Net).Take(3).Select(c => $"{c.Name} ({cs}{c.Net:N0})"));
                    receivableLine = $"\n💰 {overdueContacts.Count} customer{(overdueContacts.Count != 1 ? "s" : "")} owe you {cs}{summary.OutstandingReceivables:N0}: {topDebtors}";
                }

                // #2 Overdue receivables (7+ days)
                var overdueLine = "";
                var overdueEntries = await _db.LedgerEntries
                    .Include(e => e.Contact)
                    .Where(e => e.BusinessId == business.Id && e.EntryType == LedgerEntryType.Receivable)
                    .ToListAsync();
                var payments = await _db.LedgerEntries
                    .Where(e => e.BusinessId == business.Id && e.EntryType == LedgerEntryType.ReceivablePayment)
                    .ToListAsync();

                var overdueDebtors = overdueEntries
                    .GroupBy(e => new { e.ContactId, e.Contact.Name })
                    .Select(g =>
                    {
                        var owed = g.Sum(e => e.Amount) - payments.Where(p => p.ContactId == g.Key.ContactId).Sum(p => p.Amount);
                        var oldest = g.Min(e => e.CreatedAtUtc);
                        var days = (DateTime.UtcNow - oldest).Days;
                        return new { g.Key.Name, Owed = owed, Days = days };
                    })
                    .Where(d => d.Owed > 0 && d.Days >= 7)
                    .OrderByDescending(d => d.Days)
                    .Take(3)
                    .ToList();

                if (overdueDebtors.Count > 0)
                {
                    var lines = overdueDebtors.Select(d => $"{d.Name} ({cs}{d.Owed:N0}, {d.Days} days)");
                    overdueLine = $"\n⏰ Overdue debts: {string.Join(", ", lines)}";
                }

                // #5 No sales today
                var noSalesLine = summary.SaleCount == 0
                    ? "\n🔕 No sales recorded today — did you forget to log them?"
                    : "";

                // #6 Staff with no activity
                var staffLine = "";
                var allStaff = business.Users.Where(u => u.IsActive && u.Role != UserRole.Owner && u.Role != UserRole.Viewer).ToList();
                if (allStaff.Count > 0)
                {
                    var todayUtc = DateTime.UtcNow.Date;
                    var activeStaffIds = await _db.Sales
                        .Where(s => s.BusinessId == business.Id && s.CreatedAtUtc >= todayUtc && s.RecordedByUserId != null)
                        .Select(s => s.RecordedByUserId!.Value)
                        .Distinct()
                        .ToListAsync();
                    var activeExpenseIds = await _db.Expenses
                        .Where(e => e.BusinessId == business.Id && e.CreatedAtUtc >= todayUtc && e.RecordedByUserId != null)
                        .Select(e => e.RecordedByUserId!.Value)
                        .Distinct()
                        .ToListAsync();
                    var allActiveIds = new HashSet<Guid>(activeStaffIds.Concat(activeExpenseIds));

                    var inactiveStaff = allStaff.Where(s => !allActiveIds.Contains(s.Id)).Select(s => s.FullName).ToList();
                    if (inactiveStaff.Count > 0)
                        staffLine = $"\n👥 No activity from: {string.Join(", ", inactiveStaff)}";
                }

                // #8 Payable reminders (7+ days outstanding)
                var payableLine = "";
                var payableEntries = await _db.LedgerEntries
                    .Include(e => e.Contact)
                    .Where(e => e.BusinessId == business.Id &&
                        (e.EntryType == LedgerEntryType.Payable || e.EntryType == LedgerEntryType.PayablePayment))
                    .ToListAsync();

                var overduePayables = payableEntries
                    .Where(e => e.EntryType == LedgerEntryType.Payable)
                    .GroupBy(e => new { e.ContactId, e.Contact.Name })
                    .Select(g =>
                    {
                        var owed = g.Sum(e => e.Amount) - payableEntries
                            .Where(p => p.ContactId == g.Key.ContactId && p.EntryType == LedgerEntryType.PayablePayment)
                            .Sum(p => p.Amount);
                        var oldest = g.Min(e => e.CreatedAtUtc);
                        var days = (DateTime.UtcNow - oldest).Days;
                        return new { g.Key.Name, Owed = owed, Days = days };
                    })
                    .Where(d => d.Owed > 0 && d.Days >= 7)
                    .OrderByDescending(d => d.Days)
                    .Take(3)
                    .ToList();

                if (overduePayables.Count > 0)
                {
                    var lines = overduePayables.Select(d => $"{d.Name} ({cs}{d.Owed:N0}, {d.Days} days)");
                    payableLine = $"\n💳 You owe: {string.Join(", ", lines)}";
                }

                var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
                var message = $"📊 *Daily Summary — {localNow:MMM d, yyyy}*\n" +
                              $"🛒 Sales: {cs}{summary.TotalSales:N0} ({summary.SaleCount} transactions)\n" +
                              $"💸 Expenses: {cs}{summary.TotalExpenses:N0}\n" +
                              $"{netEmoji} Net: {cs}{net:N0}" +
                              lowStockLine + receivableLine + overdueLine + payableLine + noSalesLine + staffLine +
                              "\n\nReply with any question about your business!";

                await _whatsApp.SendMessageAsync(owner.PhoneNumber, message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Daily summary notification failed for business {BusinessId}", business.Id);
            }
        }
    }

    public async Task SendWeeklySummariesAsync()
    {
        _logger.LogInformation("Starting weekly summary notifications.");
        var businesses = await _db.Businesses
            .Include(b => b.Users)
            .Where(b => b.IsActive)
            .ToListAsync();

        foreach (var business in businesses)
        {
            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById(business.Timezone ?? "Africa/Lagos");
                var localTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
                if (localTime.Hour != 8 || localTime.DayOfWeek != DayOfWeek.Monday) continue;

                var cs = BillingConfig.Symbol(business.Currency);
                var summary = await _reports.GetWeeklySummaryAsync(business.Id, null);
                var owner = business.Users.FirstOrDefault(u => u.Role == UserRole.Owner && u.IsActive);
                if (owner == null) continue;

                var topLine = summary.TopProducts.Count > 0
                    ? $"\n🏆 Top seller: {summary.TopProducts[0].ProductName} ({cs}{summary.TopProducts[0].TotalRevenue:N0})"
                    : "";

                // #3 Best seller change — compare this week's #1 to last week's #1
                var bestSellerChangeLine = "";
                try
                {
                    var lastWeekStart = DateOnly.Parse(summary.WeekStart).AddDays(-7);
                    var lastWeekSummary = await _reports.GetWeeklySummaryAsync(business.Id, lastWeekStart);
                    if (summary.TopProducts.Count > 0 && lastWeekSummary.TopProducts.Count > 0)
                    {
                        var thisWeekTop = summary.TopProducts[0].ProductName;
                        var lastWeekTop = lastWeekSummary.TopProducts[0].ProductName;
                        if (thisWeekTop != lastWeekTop)
                            bestSellerChangeLine = $"\n🔄 New #1: *{thisWeekTop}* overtook {lastWeekTop}";
                    }
                }
                catch { /* ignore comparison failures */ }

                var lowStockLine = summary.LowStockItems.Count > 0
                    ? $"\n⚠️ Low stock: {string.Join(", ", summary.LowStockItems.Take(3).Select(p => p.Name))}"
                    : "";

                var debtorLine = summary.TopDebtors.Count > 0
                    ? $"\n💰 Outstanding: {cs}{summary.TopDebtors.Sum(d => d.TotalReceivable):N0} receivable"
                    : "";

                var message = $"📊 *Weekly Summary ({summary.WeekStart} – {summary.WeekEnd})*\n" +
                              $"Sales: {cs}{summary.TotalSales:N0}\n" +
                              $"Expenses: {cs}{summary.TotalExpenses:N0}\n" +
                              $"Est. Profit: {cs}{summary.EstimatedProfit:N0}" +
                              topLine + bestSellerChangeLine + lowStockLine + debtorLine +
                              "\n\nReply with any question about your business!";

                await _whatsApp.SendMessageAsync(owner.PhoneNumber, message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Weekly summary notification failed for business {BusinessId}", business.Id);
            }
        }
    }

    private async Task ComputeDailySummaryForBusinessAsync(Guid businessId, DateOnly date)
    {
        var start = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = start.AddDays(1);

        var totalSales = await _db.Sales
            .Where(s => s.BusinessId == businessId && s.CreatedAtUtc >= start && s.CreatedAtUtc < end)
            .SumAsync(s => s.TotalAmount);

        var totalExpenses = await _db.Expenses
            .Where(e => e.BusinessId == businessId && e.CreatedAtUtc >= start && e.CreatedAtUtc < end)
            .SumAsync(e => e.Amount);

        var ledger = await _db.LedgerEntries
            .Where(e => e.BusinessId == businessId)
            .ToListAsync();

        var receivables = ledger.Where(e => e.EntryType == LedgerEntryType.Receivable).Sum(e => e.Amount)
                        - ledger.Where(e => e.EntryType == LedgerEntryType.ReceivablePayment).Sum(e => e.Amount);
        var payables = ledger.Where(e => e.EntryType == LedgerEntryType.Payable).Sum(e => e.Amount)
                    - ledger.Where(e => e.EntryType == LedgerEntryType.PayablePayment).Sum(e => e.Amount);

        var lowStockCount = await _db.Products
            .CountAsync(p => p.BusinessId == businessId && p.IsActive && p.CurrentStock <= p.LowStockThreshold);

        var existing = await _db.DailySummaries
            .FirstOrDefaultAsync(s => s.BusinessId == businessId && s.Date == date);

        if (existing != null)
        {
            existing.TotalSales = totalSales;
            existing.TotalExpenses = totalExpenses;
            existing.NetCashIn = totalSales - totalExpenses;
            existing.OutstandingReceivables = Math.Max(0, receivables);
            existing.OutstandingPayables = Math.Max(0, payables);
            existing.LowStockCount = lowStockCount;
        }
        else
        {
            _db.DailySummaries.Add(new DailySummary
            {
                BusinessId = businessId,
                Date = date,
                TotalSales = totalSales,
                TotalExpenses = totalExpenses,
                NetCashIn = totalSales - totalExpenses,
                OutstandingReceivables = Math.Max(0, receivables),
                OutstandingPayables = Math.Max(0, payables),
                LowStockCount = lowStockCount
            });
        }

        await _db.SaveChangesAsync();
    }
}
