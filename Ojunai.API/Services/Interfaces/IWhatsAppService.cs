using Ojunai.API.DTOs.Parsing;
using Ojunai.API.Models;

namespace Ojunai.API.Services.Interfaces;

public interface IWhatsAppService
{
    Task HandleInboundAsync(string from, string messageId, string text);
    Task SendMessageAsync(string to, string text, Guid? businessId = null, Guid? userId = null);

    /// <summary>
    /// Executes one of WhatsApp's intent handlers against an arbitrary user and returns the
    /// formatted text reply. Exposed so Telegram and Messenger can delegate intents they don't
    /// implement locally (the ~50 reads / writes WhatsApp handles inline) instead of duplicating
    /// the dispatch + formatting logic.
    /// </summary>
    /// <param name="sourceChannel">
    /// What to set the <c>Source</c> field to on any records the intent creates (Sale, Expense,
    /// LedgerEntry, etc.). Use <see cref="Ojunai.API.Common.EntrySource.Telegram"/> /
    /// <see cref="Ojunai.API.Common.EntrySource.Messenger"/> so attribution stays correct in
    /// the dashboard. Defaults to <see cref="Ojunai.API.Common.EntrySource.WhatsApp"/> for the
    /// legacy in-service WhatsApp path.
    /// </param>
    /// <remarks>
    /// Caveats when called from non-WhatsApp channels:
    /// - Currency/timezone instance fields are set per-call from the user's business; scoped DI
    ///   ensures no cross-request bleed.
    /// - Some write intents (large-sale confirmation, etc.) trigger pending actions stored against
    ///   the WhatsApp pending-action table; those confirmation prompts won't reach Telegram/Messenger
    ///   users. Callers should filter to safe intents (see ConversationOrchestrator/IntentHandlers).
    /// </remarks>
    Task<string> ExecuteIntentForUserAsync(User user, ParsedMessage parsed, string? sourceChannel = null);
}
