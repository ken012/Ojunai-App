using System.Text.Json;

namespace Ojunai.API.Common;

public static class JsonElementExtensions
{
    public static string? GetStringOrNull(this JsonElement el, string property)
    {
        if (el.ValueKind == JsonValueKind.Undefined) return null;
        if (!el.TryGetProperty(property, out var val)) return null;
        return val.ValueKind == JsonValueKind.Null ? null : val.GetString();
    }

    public static decimal? GetDecimalOrNull(this JsonElement el, string property)
    {
        if (el.ValueKind == JsonValueKind.Undefined) return null;
        if (!el.TryGetProperty(property, out var val)) return null;
        if (val.ValueKind == JsonValueKind.Null) return null;
        return val.TryGetDecimal(out var d) ? d : null;
    }
}
