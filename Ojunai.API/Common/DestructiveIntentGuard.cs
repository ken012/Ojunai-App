using System.Text.Json;

namespace Ojunai.API.Common;

/// <summary>
/// Identifies AI-parsed intents that are irreversible / bulk / account-impacting and therefore must
/// require an explicit human confirmation before executing. The AI is a parser, not the authority —
/// a single misread or an injected instruction should never silently delete a catalogue, zero all
/// stock, clear everyone's debt, or provision a staff account. Each channel routes a flagged intent
/// through its own confirm flow (WhatsApp text "yes", Telegram/Messenger Yes/No buttons); this class
/// is the single, shared definition of WHAT needs confirming and the message to show.
/// </summary>
public static class DestructiveIntentGuard
{
    /// <summary>
    /// Returns a plain-language description of the destructive effect (for a "This will …, confirm?"
    /// prompt) when the intent+payload is destructive, or null when it can execute directly.
    /// </summary>
    public static string? DescribeIfDestructive(string? intent, JsonElement ba)
    {
        bool Flag(string name) =>
            ba.ValueKind == JsonValueKind.Object
            && ba.TryGetProperty(name, out var el)
            && el.ValueKind == JsonValueKind.String
            && string.Equals(el.GetString(), "true", StringComparison.OrdinalIgnoreCase);

        string? Str(string name) =>
            ba.ValueKind == JsonValueKind.Object
            && ba.TryGetProperty(name, out var el)
            && el.ValueKind == JsonValueKind.String
                ? el.GetString()
                : null;

        switch (intent)
        {
            // A batch_action wraps sub-actions in its "complete" array; each element is itself the
            // sub-payload carrying an "intent" field. The confirmation gate only inspects the top-level
            // intent, so without this a destructive op smuggled inside a batch (e.g. "sold 1 rice and
            // clear all debts") would skip confirmation entirely. Recurse into the sub-actions and, if
            // any is destructive, force the WHOLE batch through confirmation.
            case "batch_action":
            {
                if (ba.ValueKind == JsonValueKind.Object
                    && ba.TryGetProperty("complete", out var complete)
                    && complete.ValueKind == JsonValueKind.Array)
                {
                    var effects = new List<string>();
                    foreach (var el in complete.EnumerateArray())
                    {
                        if (el.ValueKind != JsonValueKind.Object) continue;
                        var subIntent = el.TryGetProperty("intent", out var si) && si.ValueKind == JsonValueKind.String
                            ? si.GetString()
                            : null;
                        if (string.IsNullOrEmpty(subIntent)) continue;
                        var sub = DescribeIfDestructive(subIntent, el);
                        if (sub != null) effects.Add(sub);
                    }
                    if (effects.Count > 0) return string.Join("; and ", effects);
                }
                return null;
            }

            case "delete_product" when Flag("deleteAll"):
                return "permanently delete ALL products from your catalogue";
            case "delete_product" when !string.IsNullOrWhiteSpace(Str("deleteCategory")):
                return $"permanently delete ALL products in the \"{Str("deleteCategory")}\" category";
            case "remove_inventory" when Flag("zeroAll"):
                return "set the stock of EVERY product to zero";
            case "record_receivable_payment" when Flag("clearAllDebts"):
                return "clear ALL customer debts (mark every customer as fully paid)";
            case "record_payable_payment" when Flag("clearAllDebts"):
                return "clear ALL supplier debts (mark every supplier as fully paid)";
            case "add_staff":
            {
                var name = Str("fullName") ?? Str("name") ?? "a new staff member";
                var phone = Str("phoneNumber") ?? Str("phone");
                var role = Str("role");
                var who = string.IsNullOrWhiteSpace(phone) ? name : $"{name} ({phone})";
                var roleBit = string.IsNullOrWhiteSpace(role) ? "" : $" as {role}";
                return $"add {who}{roleBit} as staff — they'll get a WhatsApp setup code";
            }
            default:
                return null;
        }
    }

    /// <summary>True when the intent+payload needs an explicit confirmation before executing.</summary>
    public static bool RequiresConfirmation(string? intent, JsonElement ba)
        => DescribeIfDestructive(intent, ba) != null;
}
