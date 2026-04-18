using System.Text.Json;
using System.Text.Json.Serialization;

namespace BizPilot.API.DTOs.Parsing;

public class ParsedMessage
{
    [JsonPropertyName("intent")]
    public string Intent { get; set; } = "unknown";

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("needsClarification")]
    public bool NeedsClarification { get; set; }

    [JsonPropertyName("clarificationQuestion")]
    public string? ClarificationQuestion { get; set; }

    [JsonPropertyName("businessAction")]
    public JsonElement BusinessAction { get; set; }
}

public class BusinessContext
{
    public string BusinessName { get; set; } = string.Empty;
    public string Currency { get; set; } = "NGN";
    public List<ProductContext> Products { get; set; } = new();
    public List<ContactContext> Contacts { get; set; } = new();

    // Total active products in the business. When Products list is truncated for large inventories,
    // this tells Claude the list isn't exhaustive so it can still handle references to un-listed products.
    public int TotalProducts { get; set; }

    // If the bot asked a clarification on a previous turn and is still waiting on an answer,
    // this carries the partial intent so Claude can merge the user's reply into it.
    public PendingActionContext? PendingAction { get; set; }

    public string Timezone { get; set; } = "Africa/Lagos";
}

public class PendingActionContext
{
    public string Intent { get; set; } = string.Empty;
    public string AwaitingField { get; set; } = string.Empty;
    public string QuestionText { get; set; } = string.Empty;
    public string PartialPayloadJson { get; set; } = string.Empty;
}

public record ProductContext(string Name, string Unit, decimal CurrentStock, string? Category = null);
public record ContactContext(string Name, string Type);
