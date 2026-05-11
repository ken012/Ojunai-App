using Ojunai.API.Models.Messaging;

namespace Ojunai.API.Services.Channels;

/// <summary>
/// Default registry: builds a Channel→IChannelAdapter map at construction time from the
/// adapters DI provides. Each adapter's <see cref="IChannelAdapter.Channel"/> property is
/// the lookup key. Duplicate keys throw at startup so a misconfiguration fails loud.
/// </summary>
public sealed class ChannelRegistry : IChannelRegistry
{
    private readonly Dictionary<Channel, IChannelAdapter> _adapters;

    public ChannelRegistry(IEnumerable<IChannelAdapter> adapters)
    {
        _adapters = new Dictionary<Channel, IChannelAdapter>();
        foreach (var adapter in adapters)
        {
            if (!_adapters.TryAdd(adapter.Channel, adapter))
            {
                throw new InvalidOperationException(
                    $"Duplicate IChannelAdapter registered for channel {adapter.Channel}. " +
                    $"Existing: {_adapters[adapter.Channel].GetType().Name}, new: {adapter.GetType().Name}.");
            }
        }
    }

    public IChannelAdapter Get(Channel channel)
        => _adapters.TryGetValue(channel, out var a)
            ? a
            : throw new InvalidOperationException($"No IChannelAdapter registered for channel {channel}");

    public bool TryGet(Channel channel, out IChannelAdapter adapter)
    {
        if (_adapters.TryGetValue(channel, out var found))
        {
            adapter = found;
            return true;
        }
        adapter = null!;
        return false;
    }
}
