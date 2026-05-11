using Ojunai.API.Models.Messaging;

namespace Ojunai.API.Services.Channels;

/// <summary>
/// DI-resolved lookup from <see cref="Channel"/> enum → <see cref="IChannelAdapter"/>.
/// All adapters register through this; webhook controllers and the orchestrator both
/// resolve adapters by channel rather than by concrete type.
/// </summary>
public interface IChannelRegistry
{
    IChannelAdapter Get(Channel channel);
    bool TryGet(Channel channel, out IChannelAdapter adapter);
}
