namespace Ojunai.API.Models;

/// <summary>
/// A physical location (branch or warehouse) for a business. Multi-location is gated to Scale+ and the
/// multi_location add-on; EVERY business — including single-location ones — has exactly one IsDefault
/// location (the backfilled "Main"), so single-location behavior is unchanged. Per-location stock lives
/// in <see cref="ProductLocationStock"/>; <see cref="Product.CurrentStock"/> stays as the business-wide
/// roll-up. ADDITIVE Phase 0 — nothing reads or writes this yet. See docs/multi-location-spec.md.
/// </summary>
public class Location
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BusinessId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>"branch" or "warehouse".</summary>
    public string Type { get; set; } = "branch";

    /// <summary>Exactly one default location per business (enforced by a partial unique index).</summary>
    public bool IsDefault { get; set; } = false;
    public bool IsActive { get; set; } = true;

    // ── Per-location overrides — a null value coalesces to the parent Business value ──
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    /// <summary>Branch phone shown on this branch's receipts. There's no business-level phone, so this is the
    /// only phone source — null just means no phone line on the receipt.</summary>
    public string? Phone { get; set; }
    public string? Currency { get; set; }
    public string? Timezone { get; set; }

    // Per-location receipt series. The default location is seeded from Business.NextReceiptNumber /
    // Business.ReceiptPrefix on backfill so existing receipt sequences continue unbroken.
    public int NextReceiptNumber { get; set; } = 1;
    public string? ReceiptPrefix { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Business Business { get; set; } = null!;
}
