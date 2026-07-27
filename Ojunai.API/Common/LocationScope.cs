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
}
