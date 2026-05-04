using System.Text.Json;
using Ojunai.API.DTOs.Parsing;
using Ojunai.API.Services;
using Ojunai.API.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Ojunai.Tests.ConversationRunner;

/// <summary>
/// Conversation regression harness. Loads the corpus YAML, sends each message through the real
/// <see cref="ClaudeParsingService"/>, and verifies the parse matches the expected intent and fields.
///
/// Usage:
///   CLAUDE_API_KEY=sk-ant-xxx dotnet run --project Ojunai.Tests/ConversationRunner -- [corpus-path]
///
/// Exit code is zero on all-pass, 1 on any failure — so CI/deploy gates can use it directly later.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var corpusPath = args.Length > 0
            ? args[0]
            : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Corpus", "conversation-corpus.yml");

        if (!File.Exists(corpusPath))
        {
            Console.Error.WriteLine($"Corpus file not found: {corpusPath}");
            return 2;
        }

        var config = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .AddEnvironmentVariables("CLAUDE_")
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Claude:ApiKey"] = Environment.GetEnvironmentVariable("CLAUDE_API_KEY")
                    ?? Environment.GetEnvironmentVariable("CLAUDE__APIKEY"),
                ["Claude:Model"] = Environment.GetEnvironmentVariable("CLAUDE_MODEL") ?? "claude-sonnet-4-6",
                ["Claude:MaxTokens"] = "1024"
            })
            .Build();

        if (string.IsNullOrWhiteSpace(config["Claude:ApiKey"]))
        {
            Console.Error.WriteLine("CLAUDE_API_KEY environment variable is required.");
            return 2;
        }

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging(b => b.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; }));
        services.AddHttpClient("Claude", client =>
        {
            client.BaseAddress = new Uri("https://api.anthropic.com");
            client.DefaultRequestHeaders.Add("x-api-key", config["Claude:ApiKey"]);
            client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddTransient<IClaudeParsingService, ClaudeParsingService>();

        var provider = services.BuildServiceProvider();
        var claude = provider.GetRequiredService<IClaudeParsingService>();

        var yaml = await File.ReadAllTextAsync(corpusPath);
        var corpus = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build()
            .Deserialize<Corpus>(yaml);

        if (corpus?.Cases == null)
        {
            Console.Error.WriteLine("Corpus YAML has no cases.");
            return 2;
        }

        var context = BuildContext(corpus);

        Console.WriteLine($"Running {corpus.Cases.Count} cases against {config["Claude:Model"]}...\n");

        var passed = 0;
        var failed = new List<(string Id, string Reason, string Expected, string Actual)>();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        foreach (var c in corpus.Cases)
        {
            try
            {
                // Attach pending-action context if the case describes one
                var caseContext = context;
                if (c.Pending != null)
                {
                    caseContext = CloneContext(context);
                    caseContext.PendingAction = new PendingActionContext
                    {
                        Intent = c.Pending.Intent ?? "",
                        AwaitingField = c.Pending.AwaitingField ?? "",
                        QuestionText = c.Pending.QuestionText ?? "",
                        PartialPayloadJson = c.Pending.PartialPayload ?? "{}"
                    };
                }

                var parsed = await claude.ParseAsync(c.Message, caseContext);
                var result = Verify(c, parsed);

                if (result.Pass)
                {
                    passed++;
                    Console.WriteLine($"  ✓ {c.Id}");
                }
                else
                {
                    failed.Add((c.Id, result.Reason, result.Expected, result.Actual));
                    Console.WriteLine($"  ✗ {c.Id} — {result.Reason}");
                }
            }
            catch (Exception ex)
            {
                failed.Add((c.Id, $"exception: {ex.Message}", "", ""));
                Console.WriteLine($"  ✗ {c.Id} — exception: {ex.Message}");
            }
        }

        stopwatch.Stop();

        Console.WriteLine();
        Console.WriteLine("═══════════════════════════════════════════════════");
        Console.WriteLine($"  {passed} passed, {failed.Count} failed  ({stopwatch.Elapsed.TotalSeconds:0.0}s)");
        Console.WriteLine("═══════════════════════════════════════════════════");

        if (failed.Count > 0)
        {
            Console.WriteLine("\nFailures:");
            foreach (var (id, reason, expected, actual) in failed)
            {
                Console.WriteLine($"\n  {id}: {reason}");
                if (!string.IsNullOrEmpty(expected)) Console.WriteLine($"    expected: {expected}");
                if (!string.IsNullOrEmpty(actual))   Console.WriteLine($"    got:      {actual}");
            }
        }

        return failed.Count == 0 ? 0 : 1;
    }

    private static BusinessContext BuildContext(Corpus corpus)
    {
        var products = (corpus.Seed?.Products ?? new()).Select(p => new ProductContext(
            p.Name ?? "",
            p.Unit ?? "unit",
            p.CurrentStock,
            p.Category
        )).ToList();

        var contacts = (corpus.Seed?.Contacts ?? new()).Select(c => new ContactContext(
            c.Name ?? "",
            c.Type ?? "Customer"
        )).ToList();

        return new BusinessContext
        {
            BusinessName = corpus.Seed?.Business?.Name ?? "Test Business",
            Currency = "NGN",
            Products = products,
            Contacts = contacts,
            TotalProducts = products.Count
        };
    }

    private static BusinessContext CloneContext(BusinessContext source) => new()
    {
        BusinessName = source.BusinessName,
        Currency = source.Currency,
        Products = source.Products,
        Contacts = source.Contacts,
        TotalProducts = source.TotalProducts
    };

    /// <summary>
    /// Compares the parsed output against a case's expectations. Returns a structured result so
    /// the caller can print a useful diff on failure.
    /// </summary>
    private static VerifyResult Verify(TestCase c, ParsedMessage parsed)
    {
        var expected = c.Expect ?? new Expect();

        // "allow_clarification" accepts any outcome that would result in the bot asking for clarification:
        //   - Claude explicitly set NeedsClarification=true
        //   - Claude returned intent=unknown (can't classify → server will ask)
        //   - Confidence < 0.60 (server-side low-confidence branch asks anyway)
        // All three produce the same user experience: a clarifying response.
        if (expected.AllowClarification && (parsed.NeedsClarification
                                            || string.Equals(parsed.Intent, "unknown", StringComparison.OrdinalIgnoreCase)
                                            || parsed.Confidence < 0.60))
            return new(true, "", "", "");

        if (!string.IsNullOrEmpty(expected.Intent))
        {
            if (!string.Equals(expected.Intent, parsed.Intent, StringComparison.OrdinalIgnoreCase))
            {
                return new(
                    false,
                    "intent mismatch",
                    expected.Intent,
                    $"{parsed.Intent} (confidence {parsed.Confidence:0.00})"
                );
            }
        }

        if (expected.MinConfidence.HasValue && parsed.Confidence < expected.MinConfidence.Value)
        {
            return new(false, "confidence too low", $">= {expected.MinConfidence}", parsed.Confidence.ToString("0.00"));
        }

        if (expected.Contains != null)
        {
            var actualJson = JsonSerializer.Serialize(parsed.BusinessAction);
            foreach (var (key, expectedValue) in expected.Contains)
            {
                if (!JsonContainsPath(parsed.BusinessAction, key, expectedValue))
                {
                    return new(
                        false,
                        $"missing or wrong value for '{key}'",
                        JsonSerializer.Serialize(expectedValue),
                        actualJson
                    );
                }
            }
        }

        return new(true, "", "", "");
    }

    /// <summary>
    /// Checks that a JSON element contains a given key with an expected value. Handles nested objects
    /// via dot notation (not used in the current corpus) and array-of-object partial matching —
    /// e.g. expected items:[{productName:"rice",quantity:3}] matches as long as at least one item in
    /// the parsed array has those field values.
    /// </summary>
    private static bool JsonContainsPath(JsonElement actual, string key, object? expectedValue)
    {
        if (!actual.TryGetProperty(key, out var prop))
            return false;

        if (expectedValue is null) return prop.ValueKind == JsonValueKind.Null;

        if (expectedValue is System.Collections.IList list)
        {
            if (prop.ValueKind != JsonValueKind.Array) return false;
            var actualItems = prop.EnumerateArray().ToList();
            // Each expected item must match some actual item (not position-dependent)
            foreach (var expectedItem in list)
            {
                if (expectedItem is IDictionary<object, object> expectedDict)
                {
                    var matched = actualItems.Any(actualItem =>
                        expectedDict.All(kvp =>
                            JsonContainsPath(actualItem, kvp.Key.ToString()!, kvp.Value)));
                    if (!matched) return false;
                }
                else
                {
                    // Scalar in list — weak check
                    var matched = actualItems.Any(ai => ScalarEquals(ai, expectedItem));
                    if (!matched) return false;
                }
            }
            return true;
        }

        return ScalarEquals(prop, expectedValue);
    }

    private static bool ScalarEquals(JsonElement actual, object? expected)
    {
        if (expected is null) return actual.ValueKind == JsonValueKind.Null;

        var expectedStr = expected.ToString();
        switch (actual.ValueKind)
        {
            case JsonValueKind.String:
                return string.Equals(actual.GetString(), expectedStr, StringComparison.OrdinalIgnoreCase);
            case JsonValueKind.Number:
                return decimal.TryParse(expectedStr, out var exd)
                    && actual.TryGetDecimal(out var ad)
                    && ad == exd;
            case JsonValueKind.True:
            case JsonValueKind.False:
                return string.Equals(expectedStr, actual.GetBoolean().ToString(), StringComparison.OrdinalIgnoreCase);
            default:
                return false;
        }
    }

    private record VerifyResult(bool Pass, string Reason, string Expected, string Actual);
}

// ── YAML shape ──────────────────────────────────────────────────────────────────

public class Corpus
{
    public SeedConfig? Seed { get; set; }
    public List<TestCase> Cases { get; set; } = new();
}

public class SeedConfig
{
    public BusinessSeed? Business { get; set; }
    public List<ProductSeed> Products { get; set; } = new();
    public List<ContactSeed> Contacts { get; set; } = new();
}

public class BusinessSeed
{
    public string? Name { get; set; }
    public string? Plan { get; set; }
}

public class ProductSeed
{
    public string? Name { get; set; }
    public string? Unit { get; set; }
    public decimal CurrentStock { get; set; }
    public decimal? CostPrice { get; set; }
    public decimal? SellingPrice { get; set; }
    public string? Category { get; set; }
}

public class ContactSeed
{
    public string? Name { get; set; }
    public string? Type { get; set; }
}

public class TestCase
{
    public string Id { get; set; } = "";
    public string Message { get; set; } = "";
    public Expect? Expect { get; set; }
    public PendingSeed? Pending { get; set; }
    public string? Notes { get; set; }
}

public class Expect
{
    public string? Intent { get; set; }
    public Dictionary<string, object?>? Contains { get; set; }
    public double? MinConfidence { get; set; }
    public bool AllowClarification { get; set; }
}

public class PendingSeed
{
    public string? Intent { get; set; }
    public string? AwaitingField { get; set; }
    public string? QuestionText { get; set; }
    public string? PartialPayload { get; set; }
}
