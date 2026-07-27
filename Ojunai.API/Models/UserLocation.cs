namespace Ojunai.API.Models;

/// <summary>
/// Scopes a staff user to a location. Semantics (Phase 1+): a user with NO rows implicitly accesses ALL
/// of their business's locations (back-compat — existing staff are unaffected); a user with one or more
/// rows is restricted to those locations. Owners/admins are all-access regardless. ADDITIVE Phase 0 —
/// nothing reads or writes this yet. See docs/multi-location-spec.md.
/// </summary>
public class UserLocation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid LocationId { get; set; }

    public User User { get; set; } = null!;
    public Location Location { get; set; } = null!;
}
