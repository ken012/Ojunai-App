# Ojunai — Security Remediation Plan

Prioritized actions. **Complexity:** S (≤½ day) · M (1–3 days) · L (>3 days). **Discipline:** BE (.NET backend) · FE (Next.js) · Ops/Infra · Product. Verification criteria are the acceptance test for "done."

---

## Immediate — before production / within 24h

| ID | Action | Cmplx | Disc | Depends on | Verification |
|---|---|---|---|---|---|
| OJ-01 | **Merge server-side Flutterwave webhook verification** (`VerifyChargeWithApiAsync` gate). | Done (S) | BE | Staging FLW keys | In staging, a forged `charge.completed` (valid `verif-hash`, fake tx id) does **not** activate a plan; a real sandbox charge does. |
| OJ-02 | **Merge fail-closed amount checks** (`IsPaidAmountAcceptable`). | Done (S) | BE | — | `SecurityFixTests.PaidAmount_*` pass; a webhook with an unpriced currency is rejected. |
| OJ-03 | **Merge export-PIN rate limit + per-token lockout.** | Done (S) | BE | — | 6th wrong PIN for a token returns "request a new link"; `DerivePin` tests pass. |
| OJ-04 | **Merge admin `wipe-*` → POST + confirm; case-sensitive key.** | Done (S) | BE | — | `GET /api/admin/wipe-all-data` returns 404/405; POST without `confirm` returns 400. |
| OP-A | **Rotate secrets that may have reached logs:** `Flutterwave:WebhookSecret`, `Admin:AnalyticsKey` (both have appeared in plaintext headers / query strings). Treat as compromised. | S | Ops | Provider dashboards | New values set in prod env; old values invalid; providers re-pointed. |
| OP-B | **Confirm `Jwt:SecretKey` env var (if any) equals `Jwt:Secret`**, then remove it (OJ-08). | S | Ops | — | Channel-signup (`/api/auth/post-signup`) works after removing `Jwt__SecretKey`. |

## Urgent — within 7 days

| ID | Action | Cmplx | Disc | Verification |
|---|---|---|---|---|
| OJ-04b | **Move the admin key out of the query string** to an `X-Admin-Key` header for all admin endpoints; **scrub `key`/`token` from nginx access logs.** Update `docs/admin-toolkit-reference.md`. | M | BE + Ops | Admin calls with the header succeed; `?key=` no longer appears in access logs. |
| OJ-18 | **Verify `/hangfire` exposure:** confirm nginx either does not proxy `/hangfire` or always sets `X-Forwarded-For`. Add a monitored alert if `/hangfire` is reachable from a public IP. | S | Ops | External request to `/hangfire` is denied. |
| OJ-06 | Deploy admin-key gate on `whatsapp-packs/activate` (done in code); confirm the dashboard doesn't call it as a normal user. | Done (S) | BE + FE | Owner without admin key gets 403. |
| OJ-19 | Move the export download token to the POST body (not query); scrub from logs. | M | BE + FE | Download link no longer carries the token in the URL. |
| OP-C | **Production env-var review + cloud/DB exposure check** (see Ops checklist): DB not internet-exposed, least-privilege DB role, secrets only in env, no debug mode. | M | Ops | Checklist items signed off. |

## Near-term — within 30 days

| ID | Action | Cmplx | Disc | Verification |
|---|---|---|---|---|
| OJ-14 | **Port the WhatsApp per-sender rate limiter + usage/paywall gate to Telegram & Messenger**; cap inbound message length before Claude. | M | BE | A message flood on Telegram is throttled; billed Claude calls are bounded; oversized inputs are truncated. |
| OJ-12 | **Deterministic confirmation for destructive/bulk AI intents** (delete-all, zero-all, clear-all-debts, `delete_product`, `undo`) across all channels, independent of the large-sale gate. | M | BE + Product | These intents require an explicit "yes" echoing the parsed scope before executing. |
| OJ-13 | **Confirm/echo `add_staff`** number+role before user creation; disallow `Admin` provisioning via chat. | S | BE + Product | Chat-initiated staff add requires confirmation; `Admin` role is refused. |
| OJ-11 | **Voice-AI reservation ownership:** have the Voice-AI service enforce that a reservation belongs to the calling business (or expose a business-scoped mutating route) and call it from `BusinessController`. | M | BE + downstream | A user cannot cancel/fulfill/sell another tenant's reservation GUID. |
| OJ-09 | **Full nonce-based CSP** (`script-src 'self' 'nonce-…'`, `connect-src 'self' <api-origin>`) validated against the running dashboard. | M | FE | App works with the strict CSP; no console CSP violations. |
| OJ-17 | Scope `voice-ai-inventory/{productId}` to the calling account; add the missing amount check to the Flutterwave voice-AI webhook branch. | S | BE | Cross-account product lookup via the internal key is refused. |

## Planned — within 60–90 days

| ID | Action | Cmplx | Disc | Verification |
|---|---|---|---|---|
| OJ-16 | Back the auth rate limiter with a shared store (Redis/Postgres) for multi-instance; add global failure counter / step-up. | M | BE + Ops | Rate limit holds across instances. |
| OJ-26 | Make CSV import transactional (or per-batch with correct rollback messaging) + content-hash dedupe to prevent double-counting. | L | BE | A mid-import failure leaves no partial data / accurate message; a re-uploaded file is rejected as duplicate. |
| OJ-15 | Generic responses on register + phone-reset (or move duplicate detection behind OTP). | M | BE + Product | Register/reset responses no longer reveal identifier existence. |
| OJ-03b | Replace the derived 4-digit export PIN with a high-entropy secret embedded in the signed token. | M | BE | Downloads require the high-entropy secret, not a guessable PIN. |
| OJ-20/21/22/24/28/29 | Low-severity hardening: hash `SignupChannelToken` at rest; reject-on-absent token-version; server-side logout revocation; validate `Sale.ContactId` tenant; enforce `Jwt:Secret` length ≥32 at startup; remove `oj_auth` localStorage dead code. | S each | BE/FE | Each covered by a targeted test or code review. |
| OJ-10b | Track/upgrade `@ducanh2912/next-pwa` to clear the transitive `serialize-javascript` advisory when a non-breaking path exists. | S | FE | `npm audit` clean (or documented build-time-only). |

---

## Dependencies between actions
- OP-A (secret rotation) should follow OJ-04b/OJ-19 (stop putting secrets in URLs) so rotated secrets don't immediately re-leak.
- OJ-09 full CSP depends on inventorying the dashboard's inline scripts / external origins first.
- OJ-11 depends on a change in the separate Voice-AI service repo.
