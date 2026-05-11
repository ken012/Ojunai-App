using Ojunai.API.Data;
using Ojunai.API.Models;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Services;

/// <summary>
/// 3-tier name matching that mirrors WhatsAppService.FindProductAsync:
///   1. Exact case-insensitive — happiest path, used by every well-formed message.
///   2. Substring match with shortcut threshold — "rice" → "white rice" works because typed
///      name is &lt; 50% of matched length; "Solitaire Platinum 950 Earrings" → "Solitaire
///      Platinum 950 Earrings with Moissanite" gets rejected because the user clearly meant
///      a different SKU.
///   3. Levenshtein fuzzy — catches typos like "rics" → "rice". Suggests the closest match
///      rather than auto-accepting, so we never book a sale against the wrong product.
/// </summary>
public sealed class EntityResolverService : IEntityResolverService
{
    private readonly AppDbContext _db;
    public EntityResolverService(AppDbContext db) => _db = db;

    public async Task<(Product? Product, string? Error)> FindProductAsync(Guid businessId, string name, CancellationToken ct = default)
    {
        // Tier 1 — exact match.
        var product = await _db.Products
            .FirstOrDefaultAsync(p => p.BusinessId == businessId && p.IsActive
                                   && p.Name.ToLower() == name.ToLower(), ct);
        if (product != null) return (product, null);

        // Tier 2 — substring shortcut. Only auto-accept when the typed name is dramatically
        // shorter than the match (shortcut, not variant).
        var matches = await _db.Products
            .Where(p => p.BusinessId == businessId && p.IsActive
                     && p.Name.ToLower().Contains(name.ToLower()))
            .ToListAsync(ct);

        if (matches.Count == 1)
        {
            var match = matches[0];
            if (name.Length * 2 < match.Name.Length)
                return (match, null);
            return (null, $"You don't have *{name}* in stock. Closest existing product: *{match.Name}*. " +
                          $"If you meant a different product, add it from the dashboard first.");
        }
        if (matches.Count > 1)
        {
            var names = string.Join(", ", matches.Select(p => p.Name));
            return (null, $"Multiple products match '{name}': {names}. Please be more specific.");
        }

        // Tier 3 — Levenshtein fuzzy.
        var allProducts = await _db.Products
            .Where(p => p.BusinessId == businessId && p.IsActive)
            .ToListAsync(ct);

        if (allProducts.Count == 0)
            return (null, "You don't have any products in your inventory yet. Add some from the dashboard first.");

        var lowerName = name.ToLowerInvariant();
        var scored = allProducts
            .Select(p => new { Product = p, Distance = Levenshtein(lowerName, p.Name.ToLowerInvariant()) })
            .Where(x => x.Distance <= Math.Max(2, lowerName.Length / 3))
            .OrderBy(x => x.Distance)
            .ToList();

        if (scored.Count == 0)
        {
            var available = string.Join(", ", allProducts.Take(15).Select(p => p.Name));
            var more = allProducts.Count > 15 ? $" (and {allProducts.Count - 15} more)" : "";
            return (null, $"Product '{name}' not found. Your inventory:\n\n{available}{more}");
        }

        if (scored.Count == 1 || scored[0].Distance < scored[Math.Min(1, scored.Count - 1)].Distance)
        {
            return (null, $"Product '{name}' not found. Did you mean *{scored[0].Product.Name}*? Try again with that exact name.");
        }

        var suggestions = string.Join(", ", scored.Take(3).Select(s => s.Product.Name));
        return (null, $"Product '{name}' not found. Did you mean one of: {suggestions}?");
    }

    public async Task<(Contact? Contact, string? Error)> FindContactAsync(Guid businessId, string name, CancellationToken ct = default)
    {
        // Tier 1 — exact match.
        var contact = await _db.Contacts
            .FirstOrDefaultAsync(c => c.BusinessId == businessId
                                   && c.Name.ToLower() == name.ToLower(), ct);
        if (contact != null) return (contact, null);

        // Tier 2 — prefix match. Common for first-name shortcuts: "Mary" → "Mary Johnson".
        var prefixMatches = await _db.Contacts
            .Where(c => c.BusinessId == businessId
                     && (c.Name.ToLower().StartsWith(name.ToLower() + " ")
                       || c.Name.ToLower() == name.ToLower()))
            .ToListAsync(ct);

        if (prefixMatches.Count == 1) return (prefixMatches[0], null);
        if (prefixMatches.Count > 1)
        {
            var names = string.Join(", ", prefixMatches.Select(c => c.Name));
            return (null, $"Multiple contacts match '{name}': {names}. Please use the full name.");
        }

        // Tier 3 — Levenshtein fuzzy for typos.
        var allContacts = await _db.Contacts
            .Where(c => c.BusinessId == businessId)
            .ToListAsync(ct);

        if (allContacts.Count == 0)
            return (null, "You don't have any contacts saved yet. Add them from the dashboard first.");

        var lowerName = name.ToLowerInvariant();
        var scored = allContacts
            .Select(c => new { Contact = c, Distance = Levenshtein(lowerName, c.Name.ToLowerInvariant()) })
            .Where(x => x.Distance <= Math.Max(2, lowerName.Length / 3))
            .OrderBy(x => x.Distance)
            .ToList();

        if (scored.Count == 0)
            return (null, $"I couldn't find *{name}* in your contacts. Add them from the dashboard first.");

        if (scored.Count == 1 || scored[0].Distance < scored[Math.Min(1, scored.Count - 1)].Distance)
            return (null, $"I couldn't find *{name}* in your contacts. Did you mean *{scored[0].Contact.Name}*?");

        var suggestions = string.Join(", ", scored.Take(3).Select(s => s.Contact.Name));
        return (null, $"I couldn't find *{name}*. Did you mean one of: {suggestions}?");
    }

    /// <summary>
    /// Standard Levenshtein edit distance. O(|a|·|b|) time/space — fine for inventory sizes under
    /// ~10k products. Symmetric: dist(a,b) == dist(b,a). Identical strings → 0.
    /// </summary>
    private static int Levenshtein(string a, string b)
    {
        if (string.IsNullOrEmpty(a)) return b?.Length ?? 0;
        if (string.IsNullOrEmpty(b)) return a.Length;

        var m = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) m[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) m[0, j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            for (int j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                m[i, j] = Math.Min(
                    Math.Min(m[i - 1, j] + 1, m[i, j - 1] + 1),
                    m[i - 1, j - 1] + cost);
            }
        }
        return m[a.Length, b.Length];
    }
}
