namespace Ojunai.API.Services.Channels;

/// <summary>
/// Channel-agnostic read-intent handler. Both the Telegram and Messenger intent handlers
/// delegate "show me X" type queries here (today's sales, current stock, outstanding debts,
/// summaries, etc.) so the formatting is consistent and there's no duplication across channels.
///
/// WhatsApp has its own legacy implementation inline in <see cref="Ojunai.API.Services.WhatsAppService"/>
/// today — porting it to use this service is a worthwhile follow-up but not urgent: this service
/// mirrors the WhatsApp formatting closely so the user experience is similar across channels.
///
/// Returned strings use WhatsApp-style markdown (<c>*bold*</c>, <c>_italic_</c>). Telegram
/// renders that correctly with parse_mode=Markdown; Messenger displays the markers literally,
/// which is uglier but readable. Stripping markdown per channel is a polish task.
/// </summary>
public interface IChatQueryService
{
    Task<string> GetTodaySalesAsync(Guid businessId, CancellationToken ct = default);
    Task<string> GetWeekSalesAsync(Guid businessId, CancellationToken ct = default);
    Task<string> GetAllStockAsync(Guid businessId, bool showPrices, CancellationToken ct = default);
    Task<string> GetLowStockAsync(Guid businessId, CancellationToken ct = default);
    Task<string> GetTodayExpensesAsync(Guid businessId, CancellationToken ct = default);
    Task<string> GetRecentExpensesAsync(Guid businessId, CancellationToken ct = default);

    /// <param name="type">"receivable" or "payable".</param>
    Task<string> GetOutstandingAsync(Guid businessId, string type, CancellationToken ct = default);

    Task<string> GetDailySummaryAsync(Guid businessId, CancellationToken ct = default);
    Task<string> GetContactBalanceAsync(Guid businessId, string? contactName, CancellationToken ct = default);
    Task<string> GetCashPositionAsync(Guid businessId, CancellationToken ct = default);

    string GetHelpText();
    string GetGreetText(string? businessName);
}
