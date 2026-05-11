using Ojunai.API.Models.Messaging;

namespace Ojunai.API.Services.Channels;

/// <summary>
/// Single entry point that all channel webhook controllers invoke after their adapter has
/// parsed the inbound payload into a <see cref="ConversationMessage"/>. The orchestrator owns
/// channel-blind concerns (identity resolution, cap enforcement, intent dispatch, reply
/// rendering); adapters own channel-specific concerns (signature verification, payload parsing,
/// outbound rendering).
/// </summary>
public interface IConversationOrchestrator
{
    Task ProcessInboundAsync(ConversationMessage message, CancellationToken ct = default);
}
