using Ojunai.API.Models.Messaging;
using Ojunai.API.Models;

namespace Ojunai.API.Services.Channels.Telegram;

/// <summary>
/// Phase 2.5 — handles natural-language messages from a linked Telegram user. Resolves the
/// bound User/Business, calls Claude to parse intent, dispatches to the existing
/// ISalesService/IExpenseService/ILedgerService for the actual business action, then composes
/// a Telegram-appropriate reply (text confirmation + native PDF document for sale receipts).
///
/// This is intentionally narrower than WhatsAppService.HandleInboundAsync: no pending-action
/// state machine, no in-bot product creation, no multi-line sale parsing in v1. Those land
/// once we observe how Telegram users actually use the bot.
/// </summary>
public interface ITelegramIntentHandler
{
    Task HandleAsync(ConversationMessage message, ContactIdentity boundIdentity, CancellationToken ct = default);
}
