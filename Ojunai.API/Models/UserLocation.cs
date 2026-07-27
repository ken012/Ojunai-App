namespace Ojunai.API.Models;

/// <summary>
/// Scopes a staff user to a location. Semantics (enforced by <see cref="Ojunai.API.Services.LocationAccessService"/>):
/// a restricted user (Sales/Bookkeeper/Viewer) with one or more rows is limited to those locations; a
/// restricted user with NO rows gets the business's DEFAULT location only (owner-chosen policy — they can't
/// see other branches until explicitly assigned). Owners/Admins are all-access regardless of rows. Only
/// engages at MULTI-location businesses — single-location businesses (one active location) are unaffected.
/// See docs/multi-location-spec.md.
/// </summary>
public class UserLocation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid LocationId { get; set; }

    public User User { get; set; } = null!;
    public Location Location { get; set; } = null!;
}
