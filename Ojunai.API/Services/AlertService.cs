using Ojunai.API.Data;
using Ojunai.API.Models;
using Ojunai.API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Services;

public class AlertService : IAlertService
{
    private readonly AppDbContext _db;
    private readonly ILogger<AlertService> _logger;

    /// <summary>
    /// Window during which a duplicate alert (same DedupeKey + business) is suppressed.
    /// Long enough that the bell doesn't get spammed (e.g. low-stock alert per product
    /// per day), short enough that the alert can re-fire if the underlying state recurs.
    /// </summary>
    private static readonly TimeSpan DedupeWindow = TimeSpan.FromHours(20);

    public AlertService(AppDbContext db, ILogger<AlertService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Alert?> CreateAsync(
        Guid businessId,
        Guid? userId,
        AlertType type,
        AlertSeverity severity,
        string title,
        string body,
        string? linkUrl = null,
        string? metadataJson = null,
        string? dedupeKey = null)
    {
        var now = DateTime.UtcNow;

        if (!string.IsNullOrEmpty(dedupeKey))
        {
            var dupeExists = await _db.Alerts.AnyAsync(a =>
                a.BusinessId == businessId &&
                a.DedupeKey == dedupeKey &&
                a.DismissedAtUtc == null &&
                a.CreatedAtUtc > now.Subtract(DedupeWindow));
            if (dupeExists) return null;
        }

        var alert = new Alert
        {
            BusinessId = businessId,
            UserId = userId,
            Type = type,
            Severity = severity,
            Title = title,
            Body = body,
            LinkUrl = linkUrl,
            MetadataJson = metadataJson,
            DedupeKey = dedupeKey,
            CreatedAtUtc = now,
        };
        _db.Alerts.Add(alert);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Alert {Type} created for business {Business} (user {User})", type, businessId, userId);
        return alert;
    }

    public async Task<List<Alert>> ListAsync(Guid businessId, Guid userId, UserRole role, bool unreadOnly, int limit)
    {
        var canSeeBusinessWide = role == UserRole.Owner || role == UserRole.Admin;
        var query = _db.Alerts.Where(a =>
            a.BusinessId == businessId &&
            a.DismissedAtUtc == null &&
            (a.UserId == userId || (canSeeBusinessWide && a.UserId == null)));

        if (unreadOnly) query = query.Where(a => a.ReadAtUtc == null);

        return await query
            .OrderByDescending(a => a.CreatedAtUtc)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<int> UnreadCountAsync(Guid businessId, Guid userId, UserRole role)
    {
        var canSeeBusinessWide = role == UserRole.Owner || role == UserRole.Admin;
        return await _db.Alerts.CountAsync(a =>
            a.BusinessId == businessId &&
            a.DismissedAtUtc == null &&
            a.ReadAtUtc == null &&
            (a.UserId == userId || (canSeeBusinessWide && a.UserId == null)));
    }

    public async Task MarkReadAsync(Guid businessId, Guid userId, UserRole role, Guid alertId)
    {
        var alert = await ResolveOwnedAsync(businessId, userId, role, alertId);
        if (alert.ReadAtUtc == null)
        {
            alert.ReadAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
    }

    public async Task DismissAsync(Guid businessId, Guid userId, UserRole role, Guid alertId)
    {
        var alert = await ResolveOwnedAsync(businessId, userId, role, alertId);
        if (alert.DismissedAtUtc == null)
        {
            var now = DateTime.UtcNow;
            alert.DismissedAtUtc = now;
            if (alert.ReadAtUtc == null) alert.ReadAtUtc = now;
            await _db.SaveChangesAsync();
        }
    }

    public async Task MarkAllReadAsync(Guid businessId, Guid userId, UserRole role)
    {
        var canSeeBusinessWide = role == UserRole.Owner || role == UserRole.Admin;
        var unread = await _db.Alerts
            .Where(a =>
                a.BusinessId == businessId &&
                a.DismissedAtUtc == null &&
                a.ReadAtUtc == null &&
                (a.UserId == userId || (canSeeBusinessWide && a.UserId == null)))
            .ToListAsync();

        if (unread.Count == 0) return;
        var now = DateTime.UtcNow;
        foreach (var a in unread) a.ReadAtUtc = now;
        await _db.SaveChangesAsync();
    }

    public async Task EmitPostSaleAlertsAsync(Guid businessId, decimal saleAmount, Guid? saleId = null, string? sourceChannel = null)
    {
        var business = await _db.Businesses.FindAsync(businessId);
        if (business == null) return;

        // Per-source large-sale toggle. When sourceChannel is null we treat it as "any" (legacy)
        // and only check the global AlertDashboardLargeSale flag. When the channel is known we
        // ALSO require the matching LargeSaleAlert{Channel} bool — gives owners control over
        // which channels can trigger the alert.
        var largeSaleAllowedBySource = sourceChannel switch
        {
            Common.EntrySource.WhatsApp => business.LargeSaleAlertWhatsApp,
            Common.EntrySource.Telegram => business.LargeSaleAlertTelegram,
            Common.EntrySource.Messenger => business.LargeSaleAlertMessenger,
            Common.EntrySource.Dashboard or Common.EntrySource.Manual => business.LargeSaleAlertDashboard,
            _ => true,  // Voice, Import, or anything we don't recognize defaults to allowed
        };

        // Low stock — one alert per low product, dedup'd per product per ~20h
        if (business.AlertDashboardLowStock)
        {
            var lowStock = await _db.Products
                .Where(p => p.BusinessId == businessId && p.IsActive && p.CurrentStock <= p.LowStockThreshold)
                .OrderBy(p => p.CurrentStock)
                .Take(10)
                .ToListAsync();

            foreach (var p in lowStock)
            {
                var critical = p.CurrentStock <= 0;
                var title = critical ? $"{p.Name} is out of stock" : $"{p.Name} is running low";
                var body = critical
                    ? "Reorder now — you have 0 units left."
                    : $"{p.CurrentStock:0.##} {p.Unit} left (threshold: {p.LowStockThreshold:0.##}).";
                await CreateAsync(
                    businessId, userId: null,
                    type: AlertType.LowStock,
                    severity: critical ? AlertSeverity.Critical : AlertSeverity.Warning,
                    title, body,
                    linkUrl: "/inventory",
                    dedupeKey: $"low-stock:{p.Id}");
            }
        }

        // Large sale — fire once per qualifying sale; the alert ID itself prevents pile-up.
        // Honors both the global toggle and the per-source toggle resolved above.
        if (business.AlertDashboardLargeSale && largeSaleAllowedBySource && business.LargeSaleThreshold > 0 && saleAmount >= business.LargeSaleThreshold)
        {
            var cs = Common.BillingConfig.Symbol(business.Currency);
            await CreateAsync(
                businessId, userId: null,
                type: AlertType.LargeSale,
                severity: AlertSeverity.Info,
                title: $"Large sale: {cs}{saleAmount:N0}",
                body: $"A sale of {cs}{saleAmount:N0} was just recorded — over your {cs}{business.LargeSaleThreshold:N0} threshold.",
                linkUrl: "/sales",
                dedupeKey: saleId.HasValue ? $"large-sale:{saleId}" : null);
        }

        // Sales goal hit — once per local day per business
        if (business.DailySalesGoal.HasValue && business.DailySalesGoal.Value > 0)
        {
            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById(business.Timezone);
                var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
                var startLocal = localNow.Date;
                var startUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(startLocal, DateTimeKind.Unspecified), tz);
                var endUtc = startUtc.AddDays(1);

                var todayRevenue = await _db.Sales
                    .Where(s => s.BusinessId == businessId
                        && s.CreatedAtUtc >= startUtc && s.CreatedAtUtc < endUtc
                        && !s.IsDeleted)
                    .SumAsync(s => (decimal?)s.TotalAmount) ?? 0;

                if (todayRevenue >= business.DailySalesGoal.Value)
                {
                    var cs = Common.BillingConfig.Symbol(business.Currency);
                    await CreateAsync(
                        businessId, userId: null,
                        type: AlertType.SalesGoalHit,
                        severity: AlertSeverity.Info,
                        title: "Daily sales goal hit",
                        body: $"You've reached today's goal of {cs}{business.DailySalesGoal.Value:N0}. Today's total: {cs}{todayRevenue:N0}.",
                        linkUrl: "/",
                        dedupeKey: $"sales-goal:{startLocal:yyyyMMdd}");
                }
            }
            catch (TimeZoneNotFoundException)
            {
                _logger.LogWarning("Unknown timezone {Tz} for business {Business}; skipping goal-hit check", business.Timezone, businessId);
            }
        }
    }

    private async Task<Alert> ResolveOwnedAsync(Guid businessId, Guid userId, UserRole role, Guid alertId)
    {
        var canSeeBusinessWide = role == UserRole.Owner || role == UserRole.Admin;
        var alert = await _db.Alerts.FirstOrDefaultAsync(a =>
            a.Id == alertId &&
            a.BusinessId == businessId &&
            (a.UserId == userId || (canSeeBusinessWide && a.UserId == null)))
            ?? throw new KeyNotFoundException("Alert not found.");
        return alert;
    }
}
