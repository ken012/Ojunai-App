using Ojunai.API.Models.Messaging;
using Ojunai.API.Models;

namespace Ojunai.API.Services.Channels.Messenger;

/// <summary>
/// Phase 3c — handles natural-language messages from a linked Messenger user. Resolves the
/// bound User/Business, calls Claude to parse intent, dispatches to the existing
/// ISalesService/IExpenseService/ILedgerService for the actual business action, then composes
/// a Messenger-appropriate text reply.
///
/// Functionally parallels <see cref="Telegram.ITelegramIntentHandler"/> — the same business
/// services back both channels. Channel-specific differences live in this handler: quick
/// replies (when used) disappear automatically when tapped (no manual cleanup like Telegram's
/// editMessageReplyMarkup), and sale confirmations carry no Get-Receipt button — Messenger
/// can't deliver PDFs natively the way Telegram's sendDocument does, so receipts stay in the
/// dashboard.
/// </summary>
public interface IMessengerIntentHandler
{
    Task HandleAsync(ConversationMessage message, ContactIdentity boundIdentity, CancellationToken ct = default);
}
