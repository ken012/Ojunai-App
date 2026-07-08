namespace Ojunai.API.Common;

/// <summary>
/// Pluralizes a product's unit label for a given quantity so bot replies read naturally:
/// "1 pack" vs "10 packs", "1 piece" vs "10 pieces", "1 box" vs "10 boxes". Mirrors the
/// dashboard's pluralUnit() helper (dashboard/src/lib/format.ts) so the web app and the
/// WhatsApp/Telegram/Messenger bots stay consistent.
/// </summary>
public static class UnitFormat
{
    // Measurement abbreviations that never take a plural "s" ("10 kg", not "10 kgs").
    private static readonly HashSet<string> Invariable = new(StringComparer.OrdinalIgnoreCase)
    {
        "kg", "g", "mg", "l", "ml", "cl", "oz", "lb", "lbs",
        "m", "cm", "mm", "km", "ft", "in",
    };

    /// <summary>
    /// Returns the unit word pluralized for <paramref name="qty"/>: "pack" → "packs",
    /// "box" → "boxes", "berry" → "berries". Measurement abbreviations (kg, ml, …) and
    /// already-plural inputs ("packs") are left as-is, and a qty of exactly 1 keeps the
    /// singular. Returns "" for a blank unit so callers can compose "{qty} {Plural(qty, unit)}".
    /// </summary>
    public static string Plural(decimal qty, string? unit)
    {
        var u = (unit ?? "").Trim();
        if (u.Length == 0 || qty == 1m) return u;
        if (Invariable.Contains(u)) return u;

        var lower = u.ToLowerInvariant();
        // Already plural — ends in a single 's' (not "ss"): "packs", "pieces".
        if (lower.Length >= 2 && lower[^1] == 's' && lower[^2] != 's') return u;
        if (lower.EndsWith("s") || lower.EndsWith("x") || lower.EndsWith("z")
            || lower.EndsWith("ch") || lower.EndsWith("sh")) return u + "es";      // box → boxes
        if (lower.Length >= 2 && lower[^1] == 'y' && !"aeiou".Contains(lower[^2]))
            return u[..^1] + "ies";                                                 // berry → berries
        return u + "s";
    }
}
