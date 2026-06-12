
# OJUNAI — Pre-Production Go-Live Scalability Report

**Scope:** Safety to scale ~100x current users without re-architecture surprises. Single-instance .NET 8 monolith (`Ojunai.API`) + Postgres/EF Core + in-process Hangfire + Next.js PWA dashboard.

---

## 1. Go-live verdict

**GO-WITH-CONDITIONS.**

The architecture is fundamentally sound for 100x on a single vertically-scaled host: inbound webhooks are async/durable, money paths have signature verification + idempotency + audit + reconciliation, multi-tenant indexing is consistently correct, and config is already in-memory static. There are **zero P0 blockers** — nothing will cause an outage, data loss, or runaway cost purely by reaching 100x. The conditions are the **P1 set**: a duplicate-sale/double-charge idempotency race (`msg-1`), a paid-but-not-activated billing window (`pay-1`), uncapped Claude concurrency (`resil-3`), the hourly full-business summary sweep (`db-3`/`jobs-1`/`jobs-3`/`msg-4`), the unbounded activity-feed query (`perf-1`), and basic operability instrumentation (`obs-2`/`obs-4`). All are fixable in days, not a re-architecture.

---

## 2. Executive summary (for the founder)

1. **You can scale on a bigger box — but not by adding a second server yet.** Several correctness guarantees (per-phone serialization, rate limits, image uploads on local disk) silently break the moment a second API instance is added. Vertical scaling to 100x is safe today; horizontal scaling needs a short, well-understood checklist first.

2. **Two money/data-integrity races are the most important fixes.** Under provider retries at volume, a sale can be recorded twice (and Claude charged twice), and in a narrow crash window a customer can pay without getting their plan activated — and reconciliation won't catch it. Both are rare today, routine at 100x. Fix before go-live.

3. **The daily-summary job is the single biggest piece of wasted load.** It runs the full per-business compute **every hour, 24x/day**, loading each merchant's entire ledger history into memory, when it only needs to run once near each timezone's 8 PM. This is the clearest "fix one thing, remove a lot of pressure" item.

4. **There is no ceiling on concurrent paid Claude calls.** A traffic spike (market open across regions, or Telegram/Messenger going live) can fan out 20+ simultaneous Claude calls with no bulkhead, risking 429s → retry storms → bill spikes. Add a global concurrency cap before turning on more channels.

5. **You're flying with limited instruments.** No request correlation IDs, no latency percentiles, no queryable Claude-spend metric. The app keeps running at 100x, but when something slows down you'll be grepping logs by timestamp. Cheap to add, high leverage for the exact incidents growth produces.

---

## 3. P0 blockers

**None.** No finding meets the P0 bar (will break / outage / data loss / security breach / runaway cost *purely by reaching 100x* on the current single-instance topology). The money path, the inbound async design, and the multi-tenant data model are all genuinely solid. The P1 set below is the real go-live gate.

---

## 4. P1 — fix around go-live

| Finding | Domain | Why it breaks at 100x | Fix | Effort |
|---|---|---|---|---|
| **msg-1** Inbound idempotency is read-then-act on a **non-unique** index; no `AutomaticRetry` cap | Messaging | Provider re-delivery + Hangfire's default 10 retries fire the TOCTOU race routinely at volume → **duplicate sales/expenses (financial corruption) + double Claude charges**. The Telegram/Messenger intent path writes no per-message dedup row inside the job, so a retry re-records the sale (`TelegramIntentHandler.cs:466`). | Filtered **UNIQUE** index on `(Channel, WhatsAppMessageId)`; insert dedup row FIRST inside the job (insert-or-ignore on 23505); atomic `UPDATE…WHERE` for `PendingTelegramActionService.ConsumeAsync`; add `[AutomaticRetry(Attempts=…)]` with a dedup-safe handler. | M |
| **pay-1** Idempotency row committed in a **separate transaction before** the money mutation | Payments | Crash/timeout/deploy-restart between the two `SaveChangesAsync` (Paystack `391`→`769`, Flutterwave `644`→`742`) permanently marks the event "seen" → **customer paid, plan never activated**, and `PaymentReconciliationJobService` only catches expired auto-renews, so it's invisible. More frequent at 100x with more webhooks + deploys. | Collapse dedup insert + activation into **one** `SaveChanges`/transaction, using the unique `EventId` index to detect true dupes; and/or extend reconciliation to scan `charge.success` BillingEvents lacking an activation. | M |
| **resil-3** Hangfire default workers = the **unbounded concurrency cap** on paid Claude calls; no bulkhead | Resilience | A spike (market open, summary fan-out colliding with inbound, multichannel go-live) drives ~20 simultaneous Claude calls → Anthropic 429s → retry storm of *paid* calls; each call pins a worker + DbContext + connection, starving payments/renewals/summaries jobs. | Set explicit Hangfire `WorkerCount`; dedicated queue for inbound/Claude work; **global `SemaphoreSlim`** around `CallModelAsync` sized to your Anthropic concurrency limit so excess queues instead of fanning out. Do this **before** enabling Telegram/Messenger prod traffic. | M |
| **db-3 / jobs-1 / jobs-3 / msg-4** Daily+weekly summary jobs run **hourly**; compute fans out per-business with **no hour guard** and loads full ledgers into memory; send path runs ~10 queries + a blocking provider send per business serially, no MPS throttle | DB / Jobs / Messaging | `ComputeDailySummariesAsync` recomputes every active business **24x/day** (`SummaryJobService.cs:41-63`, full-ledger `ToListAsync` at `303-305`), 23/24 runs pure waste. At a few thousand businesses this is tens of thousands of queries/hour overlapping live traffic; the 8 PM-local send cohort serializes into a multi-minute run that can overrun its tick and (once Sent.dm's MPS contract is live) breach provider limits. | Add a local-hour guard to the **compute** path (run once/day near send window); read the already-persisted `DailySummary` instead of re-running live queries at send time; filter the due-timezone cohort in the **SQL WHERE**; fan out per-business sends as individual Hangfire jobs; add a shared outbound MPS limiter. | M–L |
| **perf-1** Activity-feed loads **entire** per-business tables and paginates/filters in C# | API perf | `ReportService.GetActivityFeedAsync` (`438-673`) runs four unbounded full-table reads with Includes, applies date/search/source filters and Skip/Take **in memory** — the user's date range never reaches Postgres. This is the dashboard's main history screen; high-volume merchants balloon heap/GC and connection-hold time, spiking p95 on the busiest tenants. | Push date range + type + pagination into SQL; `.Select` to DTO before `ToListAsync`; add `AsNoTracking`; cap `Take` server-side. | M |
| **obs-2** No request correlation / trace IDs | Observability | At 100x, journald interleaves many concurrent webhooks + 14 jobs with no shared id to follow one message (webhook → Claude → DB write → reply). MTTD scales with traffic. | Lightweight middleware: set/propagate `X-Request-Id` + `BusinessId` into `ILogger.BeginScope`; enable `IncludeScopes`. No new dependency. | S |
| **obs-4** No infra metrics / latency percentiles | Observability | When latency rises you can't tell if it's API CPU, the 50-conn pool saturating, Hangfire backing up, or upstream Claude/Twilio latency — no p50/p95/p99, no pool gauge, no queue depth. Diagnosis becomes guesswork. | OpenTelemetry .NET auto-instrumentation (ASP.NET Core + HttpClient + Npgsql) to a single Prometheus/OTLP scrape target on the host. | M |

---

## 5. P2/P3 — scale-up backlog

Grouped; none are go-live blockers.

**Connection pool & Hangfire tuning (P2):** `db-1`/`jobs-5` — single 50-conn pool shared by web + ~20 Hangfire workers; no explicit `WorkerCount`, no `MaxPoolSize` sized against Postgres `max_connections`. Right-size before adding per-business job fan-out. `jobs-6` — add `[DisableConcurrentExecution]` to heavy hourly jobs so overruns don't overlap. `jobs-2`/`jobs-4` — fan-out jobs load all businesses (+Users) into memory hourly with no `AsNoTracking`; aged-receivable scan reloads full ledger per business 24x/day → set-based SQL + daily cadence.

**Query efficiency / over-fetch (P2):** `db-2`/`perf-4` — `ReportService` dashboard/overview/cash/summary paths load entire `LedgerEntry` history to compute sums; replace with SQL `GROUP BY EntryType` (the `(BusinessId, EntryType)` index already supports it). `perf-2` — clamp `pageSize` on all list endpoints (mirror `DashboardController`'s `Math.Clamp(…, 10, 100)`). `perf-5`/`perf-6`/`db-6` — add `AsNoTracking` + `AsSplitQuery` to read-heavy list/report queries (only 7 of 53 files use `AsNoTracking`). `fe-1`/`fe-2` — dashboard `fetchAllPaged` on expenses/inventory + per-page 5–8 call fan-out amplify backend QPS; add server aggregate endpoints and cache `/business`+`/auth/me`.

**Payment hardening (P2):** `pay-3` — add rowversion/`xmin` concurrency token + partial unique index ("one active whatsapp_pack") to prevent lost updates / double-active packs. `pay-4` — catch `DbUpdateException` (23505) on the dedup insert → clean no-op instead of 500.

**Resilience hygiene (P2):** `resil-2` — Flutterwave uses `new HttpClient()` per call (4 sites); register a named client. `resil-5` — Telegram/Messenger/Slack use the default 100s-timeout client; register named clients with 5–10s timeouts + real cancellation tokens. `resil-4` — onboarding (unknown numbers) reaches paid Claude with only a per-phone limiter; add a coarse **global** inbound/Claude budget that fails closed to a canned menu. `msg-5` — introduce one outbound send abstraction with token-bucket + 429 backoff (critical as Sent.dm MPS comes online).

**Data growth & retention (P2/P3):** `db-4` — add a daily sweep for `PhoneVerificationCodes` + token tables past `ExpiresAtUtc` (mirror `MessageLogRetentionJobService`). `db-7` — composite index for admin DAU/WAU/MAU scans if MessageLogs grows.

**Observability finish (P2):** `obs-1` — persist per-call Claude token usage onto MessageLog + a spend rollup/alert (numbers are already extracted at `ClaudeParsingService.cs:154-157`). `obs-3` — Serilog structured JSON sink. `obs-5` — split `/health/live` vs `/health/ready` + Hangfire heartbeat. `obs-6` — apply existing `Redact()` to email/phone in `EmailService`/`NotificationDispatcher`/payment services.

**"Before we add a second instance" checklist (P2/P3 — latent traps, not today-problems):** `scale-1` (local-disk image uploads → object storage), `msg-2` (per-phone lock + rate limiter → shared store), `scale-3`/`jobs-7` (Hangfire-per-replica → dedicate one worker or disable server on web replicas), `scale-4`/`db-5` (move `MigrateAsync` out of startup into a one-shot deploy step or advisory lock), `scale-5` (auth/inbound rate limiters → distributed), `cache-1`/`cache-4`/`pay-5`/`scale-6`/`obs-7` (in-memory dedup/throttle state → DB/Redis). `fe-3` (PM2 cluster mode), `fe-4` (lazy-load recharts), `perf-7` (PDF export → Hangfire) round out the P3 hygiene.

---

## 6. Cross-cutting architecture themes

Four systemic patterns explain almost every finding:

1. **Single-instance assumptions baked into correctness, not just config.** The app is *closer to stateless than most* (DB-backed conversation state, atomic usage upsert, DB dedup, JWT auth), but a handful of guarantees live in process memory or local disk: per-phone serialization lock, rate limiters, notification/alert dedup, and image uploads. These work perfectly at 100x **on one box** and silently break on the second. The fix isn't re-architecture — it's a disciplined "before-replicas" checklist (`scale-1/2/3/4/5`, `msg-2`, `cache-1/4`, `pay-5`). **Decision to make now:** scale vertically to 100x (cheapest, safest), and treat horizontal scale as a separate, gated project.

2. **Fan-out jobs that load everything and process serially.** The dominant job pattern is "load ALL active businesses → `foreach` → multiple queries each, several materializing full child tables." It's correct and idempotent, but runtime = N × per-business cost, the heaviest sweep runs hourly with no need to, and ledger loads grow with history. This is the largest avoidable load source (`db-3`, `jobs-1/2/3/4`, `msg-4`). Two cheap principles fix most of it: **gate work in the SQL WHERE before materializing**, and **aggregate in SQL, not C#**.

3. **No bulkhead/backpressure around external dependencies.** Inbound is async (excellent), but once in a Hangfire worker there's no global cap on the paid Claude dependency, no Polly retry/circuit-breaker/timeout policy anywhere, default 100s timeouts on some clients, and no outbound provider rate limiting. Combined with Hangfire's default 10-retry, a *provider* problem becomes a *self-inflicted* retry storm and worker-pool starvation (`resil-3/4/5`, `msg-5`, `pay-1/4`). The system needs concurrency ceilings and idempotent-retry caps on every paid/external edge.

4. **Strong domain observability, weak infra observability.** Business metrics (misparse, MRR, churn, failed payments, voice failures) are genuinely above-stage. But there are no correlation IDs, no latency/throughput/pool/queue metrics, and the most expensive dependency (Claude) is logged to console only (`obs-1/2/3/4/5`). At 100x the bottleneck becomes *diagnosis speed*, exactly when it matters most.

---

## 7. Sequenced roadmap to 100x

**Phase 0 — Correctness & cost safety (before go-live; ~days).** These are the conditions on the verdict.
- `msg-1` unique dedup index + insert-first + retry cap (stops duplicate sales / double charges).
- `pay-1` single-transaction idempotency + reconciliation backstop (stops paid-but-not-activated).
- `resil-3` global Claude concurrency cap + explicit Hangfire `WorkerCount` (stops retry-storm/bill-spike and worker starvation). *Do this before enabling Telegram/Messenger prod traffic.*
- `pay-4` clean dedup no-op; `resil-5` named clients with short timeouts. (Cheap, ride along.)

**Phase 1 — Shed the biggest avoidable load (around go-live; ~days).**
- `db-3`/`jobs-1`/`jobs-3`/`msg-4`: hour-guard the compute path, read persisted summaries at send time, filter the due-timezone cohort in SQL, fan out per-business sends. Single highest-leverage load reduction.
- `perf-1` activity-feed SQL pushdown + `perf-2` pageSize clamp (protect the busiest screen and close the over-fetch DoS vector).

**Phase 2 — Instruments before the storm (around go-live; ~1–2 days).**
- `obs-2` correlation IDs, `obs-4` OTel metrics, `obs-1` Claude-spend metric+alert. Land these *before* heavy growth so the next incidents are diagnosable.

**Phase 3 — Scale-up hardening (during ramp).**
- Pool/Hangfire right-sizing (`db-1`, `jobs-5/6`), query efficiency + `AsNoTracking` (`db-2`, `perf-4/5/6`, `db-6`), payment concurrency tokens (`pay-3`), token-table retention (`db-4`), outbound MPS limiter (`msg-5`), global onboarding budget (`resil-4`), Flutterwave named client (`resil-2`), structured logging + split health (`obs-3/5/6`), dashboard fan-out reduction (`fe-1/2`).

**Phase 4 — Horizontal-scale enablement (only if vertical isn't enough — a deliberate project, not a surprise).** Complete the "before-replicas" checklist as a unit: object storage for uploads (`scale-1`), distributed per-phone lock + rate limiters + dedup state (`msg-2`, `scale-5`, `cache-1/4`, `pay-5`, `scale-6`), Hangfire topology decision (`scale-3`, `jobs-7`), migrations out of startup (`scale-4`, `db-5`), PM2 cluster (`fe-3`). **None of this is needed to reach 100x on one box** — it's the gate for instance #2.

---

## 8. What's already right

Honest credit — this is well above typical early-stage:

- **Inbound is async and durable.** Every channel verifies signature, dedups, enqueues to Hangfire, and acks fast (`WebhooksController.cs:70/114/239/327`). Provider-timeout and request-thread-exhaustion — the classic webhook failure modes — are designed out.
- **Money path is mature.** HMAC verification with constant-time compare on both providers, amount-vs-canonical-price checks on every activation, an idempotency table with unique index covering both providers, auditable `BillingEvent` on every transition, and a reconciliation job for dropped auto-renew webhooks.
- **Data model is multi-tenant-correct.** Consistent `(BusinessId, CreatedAtUtc)` composite indexes, functional indexes for case-insensitive search, optimistic concurrency + CHECK constraint on stock, atomic `INSERT…ON CONFLICT` usage counter (no read-modify-write race), and a set-based retention sweep on the largest table.
- **Genuinely close to stateless.** Conversation state, import payloads, and dedup all DB-backed; JWT auth with no server session.
- **Config caching is already optimal.** All pricing/plan/quota catalogs are immutable `static readonly` dictionaries — no stampede surface, no Redis needed at 100x.
- **Cost defense on Claude is layered.** Simple-command short-circuit, smalltalk bypass, prompt caching, optional Haiku-first tier, and hard usage gates *before* the paid call.
- **Dashboard won't be the bottleneck.** Pure client-rendered SPA; the Node tier only serves shell + static, with a correct, PII-safe service-worker cache model.

The verdict is **GO-WITH-CONDITIONS**: ship after Phase 0 and Phase 1, with Phase 2 landing alongside. Nothing here requires re-architecture to reach 100x — it requires a focused week of targeted hardening and the discipline to keep horizontal scaling a deliberate, checklist-gated step rather than an emergency lever.