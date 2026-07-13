# Ojunai — Security Operations Checklist

Actions that **cannot be verified from source code** and must be performed/confirmed by the Ojunai operator against the production environment, provider dashboards, and infrastructure. Check each box after verification.

## Audit #2 owner actions (added 2026-07-13)
- [ ] **F05 — channel signup phone verification (BLOCKER before enabling Telegram/Messenger signup in prod).** The Telegram/Messenger signup path creates an active **Owner** account from a **user-typed, unverified** phone number. An attacker can squat a victim's number and, because inbound WhatsApp senders resolve by `PhoneNumber + IsActive`, intercept that victim's future WhatsApp data. The code fix requires new persistent state (a "pending phone / phone-verified" record) and a signup state-machine change, so it needs a schema migration and is deferred to owner review. **Remediate one of two ways before turning on channel signup:** (a) send the same WhatsApp OTP the web path uses (`PhoneVerificationService`) after the phone step and only call `CompleteTelegram/MessengerSignupAsync` once the code is confirmed; or (b) for Telegram, send a `request_contact` keyboard button and require `contact.user_id == sender` before trusting the number. Until then, keep `Telegram:BotUsername` / `Messenger:PageUsername` signup disabled. (The false "verified contact-share" comment in `AuthService` has been corrected.)
- [ ] **F23 — migrate admin tooling to the `X-Admin-Key` header.** `AdminController` now prefers the header but still accepts `?key=` as a deprecated fallback. Update any operator scripts/bookmarks to send the key as the `X-Admin-Key` request header, then the query-string fallback can be removed. Confirm the reverse-proxy access-log format is not capturing request bodies/headers containing the key.

## Credential rotation (treat as compromised — they have appeared in leak-prone locations)
- [ ] **`Flutterwave:WebhookSecret`** — sent in plaintext on every webhook; rotate in the Flutterwave dashboard and prod env. (OJ-01)
- [ ] **`Admin:AnalyticsKey`** — has traveled in URL query strings → access logs / browser history. Rotate; ensure the new value is ≥32 chars, high-entropy, mixed-case. (OJ-04)
- [ ] **Export/JWT secret (`Jwt:Secret`)** — rotate if export links (which are signed with it) may have leaked via logs; note rotation invalidates all sessions. (OJ-03/OJ-19)
- [ ] Review whether any secret was ever printed in CI logs, build artifacts, or the `bin/`/`publish/` copies of `appsettings.Development.json` on disk. Rotate anything that was.
- [ ] Confirm **`Jwt__SecretKey`** env var (if it was added to work around OJ-08) equals `Jwt:Secret`, then remove it after deploying the code fix.

## Production environment-variable review
- [ ] All secrets provided via environment/secret manager only — never committed (`.gitignore` confirmed correct; no secrets in tracked files).
- [ ] `ASPNETCORE_ENVIRONMENT=Production` (so Swagger, dev signature bypasses, and verbose paths are disabled).
- [ ] `Jwt:Secret` is ≥32 bytes and high-entropy (startup only checks non-empty — OJ-28).
- [ ] `Cors:AllowedOrigins` lists only the real dashboard origin(s) — not `*`, not localhost, in prod.
- [ ] `Twilio:SkipSignatureValidation` / `Messenger:SkipSignatureValidation` are unset/false (they only apply in Development, but confirm the environment is Production).

## Cloud IAM & network
- [ ] PostgreSQL is **not** reachable from the public internet (private network / firewall / security group only).
- [ ] The app's DB role is least-privilege (DML on app tables; not superuser). Migrations run with a separate/elevated identity if needed.
- [ ] Object storage for uploads and backups (R2/S3) is private; no public bucket listing; signed URLs where applicable.
- [ ] Backup credentials (`/etc/ojunai/backup.env`) are restricted to root/backup user and not world-readable.

## DNS & TLS
- [ ] HTTPS enforced end-to-end; HTTP→HTTPS redirect at the edge (app also sets `UseHttpsRedirection` + HSTS in Production).
- [ ] TLS ≥1.2, modern ciphers; valid certificate with auto-renewal.
- [ ] HSTS preload considered for `ojunai.com`.

## Reverse proxy (nginx)
- [ ] **`/hangfire` is either not proxied publicly, or nginx always sets `X-Forwarded-For`** — otherwise the loopback-only Hangfire filter can be bypassed and job internals/stack traces exposed (OJ-18). Test: an external request to `/hangfire` must be denied.
- [ ] Access logs **scrub `key` and `token` query parameters** (admin key, export token) — or these secrets are moved to headers/POST bodies (OJ-04b/OJ-19).
- [ ] `X-Forwarded-For` is set by nginx and only trusted from the proxy (app trusts loopback only — confirmed in code).

## Backups & DR
- [ ] Nightly off-box backup verified recent (freshness monitor already in place per project docs).
- [ ] **Restore test performed** from an encrypted backup into a scratch DB (monthly auto-restore already configured — confirm last run passed).
- [ ] Backups are encrypted at rest and access-controlled.

## Logging, monitoring & alerting
- [ ] Alerts fire on: repeated admin-key auth failures (`AdminAuditEntry` failures-by-IP), payment `payment.rejected` spikes, webhook signature failures, and (recommended) repeated export-PIN failures.
- [ ] Application logs do **not** contain passwords, full tokens, full PANs, or unnecessary PII — spot-check for PII leakage in error traces (privacy review).
- [ ] Log retention and access are defined; logs are tamper-resistant (append-only / shipped off-box).

## Payment webhook configuration
- [ ] Paystack + Flutterwave webhook URLs point to the correct prod endpoints; secrets match prod env.
- [ ] **Test vs live keys are not mixed** (esp. Flutterwave, whose static `verif-hash` provides no key-derived test/live separation — OJ-01/L3).
- [ ] After deploying OJ-01, run one real sandbox charge end-to-end to confirm legitimate activations still succeed.

## Messaging webhook configuration
- [ ] Twilio/Telegram/Messenger webhook URLs + secrets set for prod; signature verification confirmed working (no dev bypass in prod).
- [ ] When Shopify/WooCommerce integrations land (currently absent), add and review raw-body HMAC verification before enabling.

## CI/CD & source
- [ ] Branch protection on `main` (required review, no force-push).
- [ ] CI has no long-lived secrets; deploy tokens are scoped and rotated; no untrusted PR execution (current CI is a migrations guardrail only — good).
- [ ] Remove the hardcoded prod SSH host from `scripts/deploy-api.sh` (OJ-27); parameterize via env.

## AI / provider
- [ ] Confirm production data sent to Anthropic is within DPA terms; no unintended PII beyond what's needed for parsing.
- [ ] Set/verify an Anthropic spend limit and alerting (denial-of-wallet backstop until OJ-14 lands).

## Incident response
- [ ] Named owner for security incidents; documented runbook for secret rotation, tenant data-wipe reversal, and payment reconciliation disputes.
- [ ] Dependency-update cadence defined (monthly `dotnet list package --vulnerable` + `npm audit`).
