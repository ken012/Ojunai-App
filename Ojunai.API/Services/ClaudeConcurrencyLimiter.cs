namespace Ojunai.API.Services;

/// <summary>
/// Process-global ceiling on concurrent in-flight calls to the (paid) Claude API. Registered as a
/// singleton so every <see cref="ClaudeParsingService"/> instance shares one semaphore.
///
/// Why: inbound messages are processed by Hangfire workers, and Hangfire's default worker count is
/// ProcessorCount × 5. A traffic spike (market open, summary fan-out colliding with inbound, a new
/// channel going live) could otherwise fire that many simultaneous Claude calls — tripping
/// Anthropic 429s, which Hangfire then retries, turning a provider hiccup into a self-inflicted
/// retry storm of *paid* calls. Excess callers wait here instead of fanning out.
///
/// Single-instance only: this caps concurrency within one process, which is correct for the current
/// single-host deployment. If/when the API runs as multiple replicas, this needs to become a
/// distributed limiter (see the "before a second instance" checklist in the scalability audit).
/// </summary>
public sealed class ClaudeConcurrencyLimiter
{
    private readonly SemaphoreSlim _gate;

    public int MaxConcurrency { get; }

    public ClaudeConcurrencyLimiter(int maxConcurrency)
    {
        // Guard against a misconfigured 0/negative which would deadlock every call.
        MaxConcurrency = maxConcurrency > 0 ? maxConcurrency : 10;
        _gate = new SemaphoreSlim(MaxConcurrency, MaxConcurrency);
    }

    public Task WaitAsync(CancellationToken ct = default) => _gate.WaitAsync(ct);

    public void Release() => _gate.Release();
}
