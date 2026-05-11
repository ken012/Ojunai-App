namespace Ojunai.API.Models.Messaging;

/// <summary>
/// Outcome of an outbound send attempt by an <see cref="Services.Channels.IChannelAdapter"/>.
/// Used by the orchestrator for logging, retry decisions, and metrics.
/// </summary>
public sealed record SendResult(
    bool Success,
    string? ProviderMessageId,
    string? FailureReason);
