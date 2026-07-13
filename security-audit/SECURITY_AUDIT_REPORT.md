# Ojunai — Security Audit Report

**Date:** 2026-07-13
**Auditor role:** Application Security / Cloud / AI security review
**Scope:** `Ojunai.API` (ASP.NET Core 8) and `dashboard` (Next.js 15) at repository `Ojunai-AI`, commit baseline `5748cb1` (branch `main`). Fixes landed on `security/audit-fixes-2026-07`.
**Method:** Full source review (all controllers, services, middleware, jobs, channel adapters, frontend, CI, scripts, config), static analysis, `npm audit` + `dotnet list package --vulnerable`, and targeted regression tests. No production systems were touched; no real payments/messages were sent.

---

## 1. Executive summary

Ojunai is an **above-average, security-conscious codebase**. The hard parts of a multi-tenant fintech-adjacent platform are done well: tenant isolation is derived from the authenticated identity (never the client) and enforced on every data query; sessions are revocable server-side via a `TokenVersion` mechanism re-checked on each request; passwords and one-time codes are BCrypt-hashed; inbound webhooks (Twilio, Paystack, Messenger, Telegram, Resend) are signature-verified with constant-time comparison and DB-level idempotency; the LLM is used strictly as a parser with deterministic re-validation of its output; and there are no SQL-injection or user-supplied-URL SSRF sinks. No secrets are committed to the repository.

The audit nonetheless found **4 High-severity** issues, all in high-value flows, plus a set of Medium findings concentrated in three areas: the **Flutterwave payment path**, the **admin/export surfaces**, and **AI action safety on secondary channels**. The most serious was that the **Flutterwave subscription webhook activated plans directly from the webhook payload without any server-to-server verification** — a forged or replayed event (authenticated only by a static, leak-prone shared secret) could grant a free plan upgrade to any tenant. That, the **brute-forceable export PIN**, the **fail-open payment amount check**, and the **destructive admin GET endpoints** have been fixed in code with regression tests.

## 2. Overall risk rating

**Pre-audit: High** (a realistic, low-cost path to unauthorized plan grants and cross-tenant financial-data disclosure).
**Post-fix: Medium** (remaining items are either defense-in-depth, require downstream/operational changes, or are product decisions on AI action safety).

**Production readiness: Ready only after the Critical/High findings are resolved.** The four High findings are resolved in code on the fix branch; **merging that branch and completing the listed operator actions (admin-key handling, secret-rotation review, nginx/`/hangfire` check) moves the system to _Conditionally ready with documented risks_.**

## 3. Findings summary

| Severity | Count | Fixed in code | Remaining |
|---|---|---|---|
| Critical | 0 | — | — |
| High | 4 | 4 | 0 |
| Medium | 12 | 9 | 3 |
| Low/Info | 12 | 1 (+1 dep) | 10 |

_Update 2026-07-13: after the initial report, OJ-12, OJ-13, OJ-14, and OJ-26 were also fixed on the branch — Medium fixed count raised from 5 to 9._

Full detail with evidence in [SECURITY_FINDINGS.md](SECURITY_FINDINGS.md).

**High:** OJ-01 Flutterwave webhook trusts payload (no server-side verify); OJ-02 fail-open payment amount check; OJ-03 brute-forceable export PIN; OJ-04 admin destructive GET + key-in-query.

## 4. Scope & limitations

- **Source-only review** plus local build/tests. No runtime pentest against a deployed instance, no live payment/messaging traffic, no access to production environment variables, nginx config, cloud IAM, or the DNS/TLS setup. Statements about those are **unverified** and listed as operator actions.
- Payment-path fixes were validated by **compilation + unit tests of the extracted pure logic and code review**, not by end-to-end transactions against Flutterwave's sandbox (no credentials available here). The server-side-verify change fails closed and is backed by the existing reconciliation job, but should be smoke-tested in staging before/after deploy.
- Shopify/WooCommerce webhooks referenced in the brief **do not exist** in this tree (consistent with project notes that WooCommerce is on an unmerged branch). Their signature verification must be reviewed when that work lands.
- No claim of PCI DSS / GDPR / NDPR **compliance** is made. Observations only (Section 9).

## 5. Architecture & data-flow summary

.NET 8 Web API + PostgreSQL (EF Core) behind nginx; JWT (HS256) via `oj_auth` HttpOnly cookie; Hangfire (Postgres) for jobs. A Next.js dashboard is the web client. The signature product is an AI conversation layer over WhatsApp/Telegram/Messenger: Claude parses NL into structured intents that **deterministic C# handlers** execute after independent validation. Payments/subscriptions run through Paystack (NGN) and Flutterwave (other currencies) via signed webhooks + server-side verification. See [THREAT_MODEL.md](THREAT_MODEL.md) for the diagram, assets, actors, trust boundaries, and top attack chains.

Trust boundaries of note: **browser→API** (cookie + per-request identity re-check), **webhook→API** (signature verification; Flutterwave is the weak link — static secret), **API→payment provider** (authoritative state must come from verify APIs), **app→Claude** (untrusted NL + tenant catalogue; model output re-validated), **API→Voice-AI** (global admin key; reservation ownership not enforced Ojunai-side).

## 6. Confirmed findings by severity

See [SECURITY_FINDINGS.md](SECURITY_FINDINGS.md). Ordered: OJ-01…OJ-04 (High), OJ-05…OJ-17 (Medium), OJ-18…OJ-29 (Low/Info).

## 7. Fixed during the audit (branch `security/audit-fixes-2026-07`)

| ID | Title | Change |
|---|---|---|
| OJ-01 | Flutterwave webhook trusts payload | Added `VerifyChargeWithApiAsync` server-to-server verify gate before any state mutation (fails closed). |
| OJ-02 | Fail-open payment amount check | Extracted `IsPaidAmountAcceptable` (fail-closed); applied to both Flutterwave paths; amount/currency taken from the verified transaction. |
| OJ-03 | Brute-forceable export PIN | `[AuthRateLimit]` + per-token 5-attempt/15-min lockout + constant-time PIN compare. |
| OJ-04 | Admin destructive GET + weak key compare | `wipe-*` → POST with `confirm=<businessId>`; admin-key compare made case-sensitive. |
| OJ-05 | `/verify-flutterwave` tx_ref not caller-bound | Reject when the tx_ref's embedded businessId ≠ caller. |
| OJ-06 | Free WhatsApp-pack self-grant | Gated behind the admin key. |
| OJ-07 | AI prompt-injection via data-fence delimiters | `SanitizeForPrompt` strips brackets + box-drawing glyphs. |
| OJ-08 | JWT `Jwt:SecretKey` vs `Jwt:Secret` mismatch | Unified to `Jwt:Secret`. |
| OJ-09 | No dashboard CSP | Conservative CSP + hardening headers via Next `headers()`. |
| OJ-10 | MailKit 4.13.0 advisory | Upgraded to 4.17.0. |
| OJ-12 | Destructive AI intents auto-execute | New `DestructiveIntentGuard` + per-channel confirmation (WhatsApp text-yes, Telegram/Messenger Yes/No buttons) for delete-all / zero-all / clear-all-debts. |
| OJ-13 | `add_staff` model-supplied phone/role | Blocks Owner/Admin via chat; requires confirmation echoing name+phone+role. |
| OJ-14 | No Telegram/Messenger rate limit | New `ChannelRateLimiter` (15/min/sender) + inbound length cap, before the paid Claude call. |
| OJ-26 | Non-transactional CSV import | Duplicate-import guard (409) + honest partial-failure message (no false "rolled back"). |

## 8. Unresolved findings (require care / downstream / product decisions)

OJ-11 Voice-AI reservation IDOR (needs downstream ownership enforcement — an Ojunai-side check would risk breaking legitimate sells); OJ-15 account enumeration on register/reset; OJ-16 per-instance/fail-open auth rate limit; OJ-17 unscoped Voice-AI inventory endpoint. Prioritized in [SECURITY_REMEDIATION_PLAN.md](SECURITY_REMEDIATION_PLAN.md). _(OJ-12, OJ-13, OJ-14, OJ-26 were subsequently fixed — see the Fixed table above.)_

## 9. Operational findings

Admin key in query strings (move to header + scrub logs); `/hangfire` exposure depends on nginx forwarding `X-Forwarded-For` (verify); export/admin secrets in URLs; `deploy-api.sh` hardcodes the prod SSH host; JWT secret length not enforced at startup. Full operator action list in [SECURITY_OPERATIONS_CHECKLIST.md](SECURITY_OPERATIONS_CHECKLIST.md).

## 10. Dependency findings

- **.NET:** MailKit/MimeKit 4.13.0 (moderate) → **fixed** (4.17.0). ImageSharp already on patched 3.1.11. Others current.
- **npm (dashboard):** `serialize-javascript` (high) + workbox/rollup advisories are **transitive, build-time-only** under `@ducanh2912/next-pwa` (bundler tooling, not shipped) — low real risk; a breaking `next-pwa` bump is deferred. `next 15.5.15`, `react 19`, `axios ^1.14` current.

## 11. AI security findings

The LLM-as-parser design is the right call and largely holds: tenant is always server-derived (no confused-deputy), model output is deterministically re-validated (entity resolver, range checks), pending-action tokens are single-use and chat-bound, and there are no AI→SQL/shell/HTML sinks. Gaps: indirect prompt injection via unescaped data-fence delimiters (**fixed**, OJ-07); no deterministic confirmation for destructive/bulk intents (OJ-12); `add_staff` from model-supplied phone/role incl. Admin (OJ-13); no per-sender rate limit on Telegram/Messenger → denial-of-wallet (OJ-14). The AI must never be the sole authorization control — it isn't today for tenancy/role, but destructive-action confirmation should be made deterministic.

## 12. Privacy & data-protection observations

Customer/supplier PII (names, phones), financial records, and DOB are stored per-tenant. Positives: PII is stripped from the localStorage profile snapshot; `/api` responses are `no-store`; message-log retention sweep (180d default); account-deletion guide exists. Gaps to review against NDPR/GDPR obligations: PII may appear in application logs and error traces (no evidence of scrubbing); export PDFs carry financial PII (now better protected — OJ-03); background images are public-by-URL; no documented field-level encryption for PII at rest (relies on disk/DB encryption — verify). Not a compliance attestation.

## 13. Security strengths

Server-derived tenant isolation + per-request re-validation; `TokenVersion` revocation; BCrypt + hashed CSPRNG one-time tokens; account lockout + rate limiting; constant-time webhook HMAC verification + idempotency; deterministic AI-output validation; hardened image upload; parameterized queries; HttpOnly/Secure/SameSite=Strict cookies; strong security headers; no committed secrets; locked-down CI.

## 14. Recommended remediation roadmap

**Immediate (done in code, verify + deploy):** merge OJ-01…OJ-10 fixes; smoke-test Flutterwave in staging. **Urgent (≤7d):** admin key → header + log scrubbing; verify `/hangfire`/nginx; rotate any secret that may have hit logs (Flutterwave webhook secret, admin key). **Near-term (≤30d):** Telegram/Messenger rate limiting (OJ-14); destructive-AI-intent confirmation (OJ-12/OJ-13); Voice-AI reservation ownership (OJ-11); full nonce CSP (OJ-09). **Planned (≤90d):** shared-store rate limiting (OJ-16); transactional/idempotent imports (OJ-26); enumeration hardening (OJ-15); export-PIN → high-entropy token (OJ-03 residual). Detail in [SECURITY_REMEDIATION_PLAN.md](SECURITY_REMEDIATION_PLAN.md).

## 15. Final verification results

- **`dotnet build` (Ojunai.API):** succeeds, 0 errors. Only a benign `CS8604` nullability hint surfaced by the MailKit upgrade (pre-existing SMTP-password nullability).
- **`dotnet test` (Ojunai.Tests.Security):** **27/27 passing** — covers payment fail-closed logic, export-PIN derivation, prompt-injection sanitization, destructive-intent detection, add-staff confirmation, and the channel rate limiter.
- **`dotnet list package --vulnerable`:** MailKit advisory cleared after upgrade.
- **`npm audit` (dashboard):** remaining advisories are transitive build-time only (documented).
- **Not run (environment limits):** end-to-end payment/webhook tests against provider sandboxes; full dashboard runtime CSP validation; infra/cloud/DNS/TLS/IAM review (operator actions).

**Conclusion: Ready only after the High findings are resolved.** All four High findings are resolved in code on `security/audit-fixes-2026-07`; after merge + staging smoke-test + the Urgent operator actions, the posture is **Conditionally ready with documented risks**.
