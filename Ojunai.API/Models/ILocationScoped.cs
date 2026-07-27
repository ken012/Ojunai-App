namespace Ojunai.API.Models;

/// <summary>
/// Marks a transaction entity that happens AT a location. On insert, <see cref="Ojunai.API.Data.AppDbContext"/>
/// stamps <see cref="LocationId"/> from the ambient <see cref="Ojunai.API.Common.LocationScope"/> — but ONLY
/// when the business is genuinely multi-location (&gt;1 active location) and a valid location is selected. In
/// every other case (single-location, "All locations", bot/background) it stays null = business-wide, exactly
/// as before. This centralizes attribution so the write services don't each have to stamp it. See
/// docs/multi-location-spec.md.
/// </summary>
public interface ILocationScoped
{
    Guid BusinessId { get; }
    Guid? LocationId { get; set; }
}
