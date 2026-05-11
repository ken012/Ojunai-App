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
    ///
    /// Caveats when called from non-WhatsApp channels:
    /// - Currency/timezone instance fields on the service get set per-call from <paramref name="user"/>'s
    ///   business; concurrent multi-channel use is fine because the service is scoped per request.
    /// - Write intents currently record <c>Source="WhatsApp"</c> regardless of caller — addressed
    ///   by future thread-source-through-handlers cleanup.
    /// - Some write intents (large-sale confirmation, etc.) trigger pending actions stored against
    ///   the WhatsApp pending-action table; those confirmation prompts won't reach Telegram/Messenger
    ///   users. Callers should filter to safe intents (see ConversationOrchestrator/IntentHandlers).
    /// </summary>
    Task<string> ExecuteIntentForUserAsync(User user, ParsedMessage parsed);
}
