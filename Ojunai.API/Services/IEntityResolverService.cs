using Ojunai.API.Models;

namespace Ojunai.API.Services;

/// <summary>
/// Channel-agnostic helper for looking up a Product or Contact by user-typed name. Lifted from
/// the inline FindProductAsync logic in WhatsAppService so the Telegram NL pipeline can share
/// the same 3-tier matching (exact → substring shortcut → Levenshtein fuzzy) without duplicating
/// the code. WhatsAppService can migrate to this in a future cleanup pass; for now they coexist.
/// </summary>
public interface IEntityResolverService
{
    /// <summary>
    /// Resolves a user-typed product name against the business's active inventory.
    ///   - <c>Product = X, Error = null</c>: found a clean match.
    ///   - <c>Product = null, Error = "..."</c>: couldn't auto-match; error text is a user-friendly
    ///     explanation including suggestions (typo candidates, available products, etc.).
    /// </summary>
    Task<(Product? Product, string? Error)> FindProductAsync(Guid businessId, string name, CancellationToken ct = default);

    /// <summary>Same shape but for contacts (customers/suppliers).</summary>
    Task<(Contact? Contact, string? Error)> FindContactAsync(Guid businessId, string name, CancellationToken ct = default);
}
