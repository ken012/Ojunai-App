using System.Text.RegularExpressions;
using Ojunai.API.Common;
using Ojunai.API.Data;
using Ojunai.API.Models;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Services.Channels;

/// <summary>
/// Channel-agnostic branch (location) switching for the Telegram &amp; Messenger bots — the parity
/// equivalent of <c>WhatsAppService.BuildBranchPickerAsync</c> / <c>TryApplyBranchSelectionAsync</c>,
/// but surfaced as quick-reply buttons that fit those channels' token-callback model instead of a
/// free-text numbered reply. The plan gate (multi-location entitlement) and the accessible-location
/// scoping match WhatsApp exactly, so a restricted staffer only ever sees / switches to branches they
/// may access. Single-location (or unentitled) businesses get a friendly one-liner and no buttons.
/// </summary>
public sealed class LocationChatService
{
    private readonly AppDbContext _db;
    private readonly LocationAccessService _access;
    private readonly PlanGuard _planGuard;

    public LocationChatService(AppDbContext db, LocationAccessService access, PlanGuard planGuard)
    {
        _db = db;
        _access = access;
        _planGuard = planGuard;
    }

    // Same deterministic "branches"/"locations" trigger as the WhatsApp bot (WhatsAppService.BranchCommandRegex).
    // Kept here so both channel handlers share one source of truth and can't drift.
    private static readonly Regex BranchCommandRegex = new(
        @"^\s*(switch|change|list|show|view|see|my|which|what)?\s*(branch(es)?|location(s)?)\s*[\?!\.]*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>True when the raw message is the deterministic "branches"/"locations" command (no Claude call needed).</summary>
    public static bool IsBranchCommand(string? text) => !string.IsNullOrEmpty(text) && BranchCommandRegex.IsMatch(text);

    public sealed record BranchOption(Guid Id, string Name, bool IsCurrent);

    /// <summary>A picker to render. When <see cref="Options"/> is empty, <see cref="Text"/> is a terminal
    /// message (single-location / unentitled / no access) and the caller should send it with no buttons.</summary>
    public sealed record BranchPicker(string Text, IReadOnlyList<BranchOption> Options);

    /// <summary>Builds the branch picker for a sender: their accessible active branches, current one flagged.</summary>
    public async Task<BranchPicker> BuildPickerAsync(Guid businessId, Guid userId, UserRole role)
    {
        if (!await _planGuard.CanUseMultiLocationAsync(businessId))
            return new BranchPicker(
                "📍 You're running a single location, so there's nothing to switch. Multi-location is available on the *Scale* plan (or as an add-on).",
                Array.Empty<BranchOption>());

        var accessibleIds = await _access.AccessibleLocationIdsAsync(businessId, userId, role);
        var locations = await _db.Locations
            .Where(l => l.BusinessId == businessId && l.IsActive && accessibleIds.Contains(l.Id))
            .OrderByDescending(l => l.IsDefault).ThenBy(l => l.CreatedAtUtc)
            .Select(l => new { l.Id, l.Name })
            .ToListAsync();

        if (locations.Count <= 1)
        {
            var only = locations.FirstOrDefault();
            var msg = only == null
                ? "📍 You don't have any active locations yet."
                : $"📍 You're set up at *{only.Name}*. All your entries go there.";
            return new BranchPicker(msg, Array.Empty<BranchOption>());
        }

        var selected = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId).Select(u => u.SelectedLocationId).FirstOrDefaultAsync();
        var current = await _access.ResolveEffectiveLocationAsync(businessId, userId, role, selected);

        var options = locations.Select(l => new BranchOption(l.Id, l.Name, l.Id == current)).ToList();
        var currentName = options.FirstOrDefault(o => o.IsCurrent)?.Name;
        var text = currentName != null
            ? $"📍 *Your branches* — tap one to switch. Recording to *{currentName}* now."
            : "📍 *Your branches* — tap one to switch.";
        return new BranchPicker(text, options);
    }

    /// <summary>
    /// Applies a branch selection: validates the sender may access it (and that it's still an active branch
    /// of theirs), persists <see cref="User.SelectedLocationId"/>, returns a confirmation. Belt-and-braces
    /// even though the id comes from a server-side pending payload — guards a stale token whose branch was
    /// deactivated or a restricted user whose access was revoked between prompt and tap.
    /// </summary>
    public async Task<string> ApplySelectionAsync(Guid businessId, Guid userId, UserRole role, Guid locationId)
    {
        var loc = await _db.Locations
            .FirstOrDefaultAsync(l => l.Id == locationId && l.BusinessId == businessId && l.IsActive);
        if (loc == null)
            return "That branch isn't available anymore. Send *branches* to see your current options.";

        var effective = await _access.ResolveEffectiveLocationAsync(businessId, userId, role, locationId);
        if (effective != locationId)
            return "Sorry, you don't have access to that branch. Send *branches* to see your options.";

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.BusinessId == businessId);
        if (user == null) return "Couldn't find your account. Try again.";

        user.SelectedLocationId = locationId;
        await _db.SaveChangesAsync();
        return $"✅ Switched to *{loc.Name}*. Your sales & stock entries go here now. Send *branches* anytime to switch.";
    }
}
