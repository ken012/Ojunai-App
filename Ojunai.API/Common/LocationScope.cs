namespace Ojunai.API.Common;

/// <summary>
/// Ambient per-request location context (multi-location Phase 2b). The base controller sets this from the
/// <c>X-Location-Id</c> header on each dashboard request; the AppDbContext dual-write mirror reads it to
/// route a MULTI-location business's stock change to the right <c>ProductLocationStock</c> row. Flows down
/// the async request via <see cref="AsyncLocal{T}"/>, so it's isolated per request and reaches SaveChanges
/// without threading a parameter through every service.
///
/// Unset (null) — the common case: bot/background writes, single-location businesses, or a dashboard "All
/// locations" view — means "the business's default location", i.e. exactly the Phase-1 behaviour. The value
/// is validated against the business's active locations INSIDE the mirror (an invalid/foreign id falls back
/// to the default), so the setter can trust the raw header without a DB round-trip.
/// </summary>
public static class LocationScope
{
    private static readonly AsyncLocal<Guid?> _current = new();

    public static Guid? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }

    /// <summary>
    /// Sets <see cref="Current"/> for the lifetime of the returned scope and restores the previous
    /// value on dispose. Preferred over a manual set/try-finally when a whole method (with many early
    /// returns) should run under one ambient location — e.g. the Telegram/Messenger inbound handlers
    /// stamping a sender's effective branch. The scope is always overwritten with the caller's value
    /// before any DB write and always restored on dispose, so it can't bleed into later work in this
    /// flow. (Restoring the PREVIOUS value keeps nesting correct; at a job entry point the previous
    /// value is null, so dispose leaves the context clean.)
    /// </summary>
    public static IDisposable Push(Guid? value)
    {
        var previous = _current.Value;
        _current.Value = value;
        return new Scope(previous);
    }

    private sealed class Scope : IDisposable
    {
        private readonly Guid? _previous;
        private bool _disposed;
        public Scope(Guid? previous) => _previous = previous;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _current.Value = _previous;
        }
    }
}
