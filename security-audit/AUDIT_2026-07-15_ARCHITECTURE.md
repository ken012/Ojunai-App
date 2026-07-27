# Ojunai — Architecture & Trust-Boundary Map (Audit 2026-07-15)

**Auditor role:** Principal Security / Platform / Backend review
**Baseline:** `main` @ `156beeb` (prior audit baseline was `5748cb1`; this pass focuses on the ~51-file delta merged since — the activity-audit-log stack and July inventory/billing work — plus re-verification of the prior fixes).

> This is the **second** formal audit. The first (`security-audit/SECURITY_AUDIT_REPORT.md`, 2026-07-13) found 0 Critical / 4 High (all fixed) / 12 Medium (9 fixed) / 12 Low, and its fix branch `security/audit-fixes-2026-07` is **confirmed merged into main**. This document supersedes only the architecture view; findings are in `AUDIT_2026-07-15_FINDINGS.md`.

---

## 1. Stack (as-built, verified in source)

| Layer | Technology | Notes |
|---|---|---|
| Web client | Next.js 15.5.15 / React 19 / TypeScript | `dashboard/`; PWA (next-pwa); shadcn/Tailwind |
| API | ASP.NET Core 8 (net8.0) | `Ojunai.API/`; 374 `.cs` files, ~141k LOC |
| Data | PostgreSQL via EF Core 8 (Npgsql 8.0.11) | Pooled (max 50); soft-delete global query filters |
| Jobs | Hangfire 1.8.23 on PostgreSQL storage | Durable inbound message processing + recurring jobs |
| Auth | JWT HS256 in `oj_auth` HttpOnly cookie | Per-request DB re-validation (see §3) |
| LLM | Anthropic Claude (api.anthropic.com) | Used strictly as a **parser** → deterministic C# handlers |
| Telemetry | OpenTelemetry → OTLP (Grafana Cloud) | Gated on `OTEL_EXPORTER_OTLP_ENDPOINT` |
| Email | MailKit 4.17.0 (SMTP) | |
| PDF | QuestPDF 2026.2.4 | Reports/receipts/exports |
| Reverse proxy | nginx (out of tree) | ForwardedHeaders trusts loopback only |

**Messaging channels:** WhatsApp (Twilio), Telegram, Messenger (behind `Multichannel:V1Enabled` flag → `ConversationOrchestrator`).
**Payments:** Paystack (NGN), Flutterwave (other currencies), + Voice-AI provisioning service (out of tree).
**Platform integrations named in brief but NOT present in this tree:** Shopify, WooCommerce (WooCommerce is on an unmerged branch). Their webhook/OAuth code cannot be audited here — see readiness section of the findings report.

## 2. Attack surface (every externally reachable entrypoint)

**Browser → API (cookie auth):** all `[Authorize]` controllers — Auth, Business, Products, Inventory, Sales, Contacts, Ledger, Reports, Expenses, Staff, Subscription, Channels, Export, Import, Alerts, Events, PurchaseOrders, Stocktakes, StockHolds, VariantGroups, ResendNotifications.

**Anonymous / signature-authenticated:**
- `POST /api/webhooks/whatsapp` — Twilio HMAC (`ValidateTwilioSignatureAsync`), 64KB cap.
- `POST /api/webhooks/telegram`, `/messenger` — provider verification; `GET` Messenger hub verify-token.
- `POST /api/subscription/webhook/*` — Paystack + Flutterwave signatures.
- Resend (email) webhook — signature-verified.
- `GET/HEAD /health` — anonymous, DB ping only.

**Admin surface:** `AdminController` — gated by an admin key (own auth scheme; JWT cookie deliberately not read for `/api/admin`).
**Ops surface:** `/hangfire` dashboard — `HangfireLocalAuthFilter` (loopback/same-host only, rejects null RemoteIp).

## 3. The tenant-isolation model (the core control)

There is **no** ambient/global `businessId` EF query filter. Isolation is enforced by two cooperating mechanisms:

1. **Server-derived identity.** `OjunaiBaseController.BusinessId => User.GetBusinessId()` reads the `businessId` **JWT claim** — never a client-supplied route/query/body value.
2. **Per-request re-validation** (`ActiveUserMiddleware`, runs on every non-`[AllowAnonymous]` request): reloads the user+business from the DB and rejects if (a) user missing/inactive, (b) business inactive, (c) the JWT `businessId` claim ≠ the user's actual `BusinessId` (blocks token-forgery cross-tenant), (d) JWT `tokenVersion` ≠ DB `TokenVersion` (fail-closed revocation on password change/reset). Stale cookie is proactively cleared.
3. **Manual per-query scoping.** Every data query must include `&& x.BusinessId == businessId`. Verified consistent in `ProductService` and sampled services. **This is the residual risk surface:** any single query that forgets the predicate is a cross-tenant leak. The findings report enumerates the service-layer load-by-id audit.

For **inbound chat**, tenant is resolved from the **signature-authenticated sender identity** (Twilio-signed `From` phone / verified chat-id mapping), never from message content or LLM output.

## 4. Trust boundaries

```
                 ┌────────────────────────────────────────────────────┐
   Untrusted     │  Browser (dashboard)   Chat users (WA/TG/Messenger) │
   Internet      └───────┬───────────────────────┬────────────────────┘
                         │ HTTPS cookie           │ provider webhook (signed)
              ┌──────────▼─────────┐   ┌──────────▼───────────────┐
  BOUNDARY 1  │ CORS allowlist +   │   │ Signature verify (HMAC,  │  BOUNDARY 2
  (browser→   │ JWT + per-req DB    │   │ constant-time) + size cap│  (webhook→API)
   API)       │ re-validation       │   │ + DB idempotency         │
              └──────────┬─────────┘   └──────────┬───────────────┘
                         │                        │ sender→tenant map (verified)
                         ▼                        ▼
              ┌───────────────────────────────────────────────────┐
              │  Controllers → Services (manual businessId scope)  │
              │  EF Core (parameterized; NO raw SQL) → PostgreSQL  │
              └───────┬───────────────────────┬──────────┬────────┘
                      │ BOUNDARY 3            │          │ BOUNDARY 5
                      ▼ (app→Claude)          ▼ BOUNDARY 4 (app→payment provider)
              ┌──────────────┐        ┌───────────────┐  (app→Voice-AI: global
              │ Claude parse │        │ Paystack /    │   admin key; reservation
              │ (untrusted   │        │ Flutterwave   │   ownership enforced
              │ NL; output   │        │ verify APIs   │   downstream)
              │ re-validated)│        │ (authoritative│
              └──────────────┘        │  state)       │
                                      └───────────────┘
```

- **B1 browser→API:** strongest boundary. Cookie is HttpOnly/Secure/SameSite=Strict; identity re-checked per request.
- **B2 webhook→API:** signature + idempotency. Flutterwave historically the weak link (static shared secret) — now backed by server-side verify (OJ-01 fix).
- **B3 app→Claude:** LLM output is advisory only; entity resolver + range checks re-validate deterministically; tenant never model-derived.
- **B4 app→payment provider:** authoritative payment state must come from verify APIs, not webhook payload (post-OJ-01).
- **B5 app→Voice-AI:** global admin key; reservation ownership enforced by the downstream service, not Ojunai-side (OJ-11, residual).

## 5. Secrets & config posture

Real config (`appsettings*.json`, `.env*`) is **gitignored and absent from all history**; only placeholder `.example` files are tracked. Secrets injected via environment variables (JWT, DB conn, Twilio, Claude, Paystack, Flutterwave, OTLP). No committed key material. See findings report for the residual hardening items (JWT length enforcement, deploy-script host disclosure, error-path body logging).
