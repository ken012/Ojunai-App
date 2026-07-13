namespace Ojunai.API.Common;

/// <summary>
/// In-memory per-sender rate limiter for chat channels (Telegram / Messenger), mirroring the
/// WhatsApp limiter (15 messages / minute / sender). Every inbound message triggers a paid Claude
/// parse, so an unthrottled linked sender could flood messages and run up the AI bill
/// (denial-of-wallet). This caps that per sender. In-process (per-instance) — acceptable for an abuse
/// cap; it is NOT a substitute for the billing/usage gate.
///
/// It also exposes a max inbound length so a single huge message can't inflate input tokens.
/// </summary>
public static class ChannelRateLimiter
{
    private static readonly Dictionary<string, List<DateTime>> _hits = new();
    private static readonly object _lock = new();
    private static DateTime _lastCleanup = DateTime.UtcNow;

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private const int MaxPerWindow = 15;

    /// <summary>Max characters of an inbound message forwarded to the model. Longer input is truncated.</summary>
    public const int MaxInboundLength = 2000;

    /// <summary>
    /// Returns true if this sender has already sent <see cref="MaxPerWindow"/> messages in the current
    /// window and should be blocked. A non-blocked call is counted as a hit.
    /// </summary>
    public static bool IsLimited(string? senderKey)
    {
        if (string.IsNullOrEmpty(senderKey)) return false;
        lock (_lock)
        {
            var now = DateTime.UtcNow;

            // Periodic cleanup to bound memory under a flood.
            if (now - _lastCleanup > TimeSpan.FromMinutes(5))
            {
                var stale = now - Window;
                foreach (var k in _hits.Where(kv => kv.Value.All(t => t < stale)).Select(kv => kv.Key).ToList())
                    _hits.Remove(k);
                _lastCleanup = now;
            }

            if (!_hits.TryGetValue(senderKey, out var ts))
            {
                ts = new List<DateTime>();
                _hits[senderKey] = ts;
            }
            ts.RemoveAll(t => now - t > Window);
            if (ts.Count >= MaxPerWindow) return true;
            ts.Add(now);
            return false;
        }
    }

    /// <summary>Truncate an inbound message to <see cref="MaxInboundLength"/> before parsing.</summary>
    public static string CapLength(string? text)
        => string.IsNullOrEmpty(text) ? "" : (text.Length <= MaxInboundLength ? text : text[..MaxInboundLength]);
}
