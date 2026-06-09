# Observability setup (OpenTelemetry → Grafana Cloud)

The API emits OpenTelemetry **traces + metrics** over OTLP. It is **gated on the
`OTEL_EXPORTER_OTLP_ENDPOINT` env var**: if that var is unset, OpenTelemetry is not
registered at all (zero overhead, no failed exports). So this code is safe to deploy
*before* Grafana is configured — telemetry simply stays off until the env vars are set.

Correlation IDs (RequestId / BusinessId / UserId) are always on in logs via
`RequestContextMiddleware` (no setup needed).

## What gets exported
- **HTTP server** — request rate + latency percentiles (p50/p95/p99)
- **HttpClient** — outbound dependency latency (Claude, Paystack, …)
- **Runtime** — GC, threadpool
- **Npgsql meter** — DB connection-pool usage (the 50-conn pool)
- **Ojunai.Claude meter** — per-call token spend, tagged by `model`:
  `claude.calls`, `claude.tokens.input`, `claude.tokens.output`,
  `claude.tokens.cache_read`, `claude.tokens.cache_creation`
  (in Prometheus/Mimir these appear as `claude_calls_total`, `claude_tokens_input_total`, …)

## Step 1 — get OTLP credentials from Grafana Cloud
1. grafana.com → your stack.
2. **Connections → add new connection → "OpenTelemetry (OTLP)"** (or the "OpenTelemetry"
   tile on the stack home / "Configure").
3. Note the **OTLP Endpoint** (e.g. `https://otlp-gateway-prod-<zone>.grafana.net/otlp`)
   and your **Instance ID** (a number).
4. Click **Generate token / Create a token** (a Cloud Access Policy token with
   metrics + traces *write* scope). Copy it — shown once.
5. Grafana shows a ready-made snippet with `OTEL_EXPORTER_OTLP_ENDPOINT` and
   `OTEL_EXPORTER_OTLP_HEADERS` (the `Authorization: Basic …` is already base64-encoded
   for you). Copy both.

## Step 2 — set the env vars on the server (systemd)
```bash
sudo systemctl edit ojunai-api
```
Add:
```ini
[Service]
Environment="OTEL_EXPORTER_OTLP_ENDPOINT=https://otlp-gateway-prod-<zone>.grafana.net/otlp"
Environment="OTEL_EXPORTER_OTLP_HEADERS=Authorization=Basic <base64(instanceID:token)>"
Environment="OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf"
# optional — code already defaults to ojunai-api
Environment="OTEL_SERVICE_NAME=ojunai-api"
```
Then:
```bash
sudo systemctl daemon-reload
sudo systemctl restart ojunai-api
```

> ⚠️ **`OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf` is REQUIRED.** Grafana Cloud's OTLP
> gateway is HTTP; the .NET exporter defaults to gRPC, so without this nothing exports.
>
> The `OTEL_EXPORTER_OTLP_HEADERS` value contains a secret token — the systemd drop-in is
> root-only, which is fine. Don't commit it to the repo.

## Step 3 — verify (within ~1 min of restart)
- Grafana → **Explore** → your Prometheus/Mimir data source → query
  `http_server_request_duration_seconds_count` or `claude_tokens_input_total`.
- Traces: **Explore** → Tempo data source → filter by service `ojunai-api`.

## Suggested first alert
Alert on Claude spend rate, e.g. `sum(rate(claude_tokens_input_total[5m])) + sum(rate(claude_tokens_output_total[5m]))`
above your normal ceiling. (The hard cost *cap* already exists in code — `Claude:MaxConcurrency`,
Phase 0 — so this alert is early-warning, not the only line of defense.)
