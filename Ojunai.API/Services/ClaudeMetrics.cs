using System.Diagnostics.Metrics;

namespace Ojunai.API.Services;

/// <summary>
/// OpenTelemetry instruments for Claude API usage — the app's most expensive dependency.
/// Recording to a Meter with no listener is effectively free, so this is wired unconditionally;
/// the counters only leave the process once OTel metrics export is configured (Program.cs registers
/// this meter, gated on an OTLP endpoint).
///
/// Token counts are the cost driver (input/output billed full rate, cache_read ~10%,
/// cache_creation ~125%). Tag by model so spend can be split per model; alert on the input+output
/// rate to catch a runaway-cost spike early.
/// </summary>
public static class ClaudeMetrics
{
    public const string MeterName = "Ojunai.Claude";
    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> Calls = Meter.CreateCounter<long>("claude.calls");
    private static readonly Counter<long> InputTokens = Meter.CreateCounter<long>("claude.tokens.input");
    private static readonly Counter<long> OutputTokens = Meter.CreateCounter<long>("claude.tokens.output");
    private static readonly Counter<long> CacheReadTokens = Meter.CreateCounter<long>("claude.tokens.cache_read");
    private static readonly Counter<long> CacheCreationTokens = Meter.CreateCounter<long>("claude.tokens.cache_creation");

    public static void Record(string model, int input, int output, int cacheRead, int cacheCreation)
    {
        var tag = new KeyValuePair<string, object?>("model", model);
        Calls.Add(1, tag);
        if (input > 0) InputTokens.Add(input, tag);
        if (output > 0) OutputTokens.Add(output, tag);
        if (cacheRead > 0) CacheReadTokens.Add(cacheRead, tag);
        if (cacheCreation > 0) CacheCreationTokens.Add(cacheCreation, tag);
    }
}
