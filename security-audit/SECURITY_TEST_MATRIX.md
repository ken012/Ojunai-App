# Ojunai — Security Test Matrix

Maps security controls → threat → standard → test location/type → status → gap. Automated tests live in `Ojunai.Tests/Security/SecurityFixTests.cs` (xUnit; run `dotnet test Ojunai.Tests/Security`). "Manual" = verified by code review during this audit. "Gap" = not yet automated.

| Control | Threat addressed | Standard | Test location | Type | Status | Remaining gap |
|---|---|---|---|---|---|---|
| Payment amount validation fails closed | Forged/underpaid subscription activation (OJ-02) | OWASP API6; ASVS 4.0 V11 | `SecurityFixTests.PaidAmount_*`, `BillingConfig_UnsupportedCurrency_*` | Unit | ✅ Pass | End-to-end sandbox charge test |
| Flutterwave webhook server-side verification | Forged/replayed webhook → free plan (OJ-01) | OWASP API2; LLM n/a | Code review; `VerifyChargeWithApiAsync` gate | Manual | ✅ Implemented | Automated integration test w/ FLW sandbox |
| Export PIN derivation + lockout | Brute-force of export download (OJ-03) | OWASP API4; ASVS V2.2 | `SecurityFixTests.DerivePin_*` | Unit | ✅ Pass | Automated test of the 5-attempt lockout (needs DB/controller harness) |
| Prompt-injection sanitization | Indirect prompt injection via stored names (OJ-07) | OWASP LLM01 | `SecurityFixTests.SanitizeForPrompt_*` | Unit | ✅ Pass | Adversarial corpus of injection payloads |
| Admin destructive ops require POST + confirm | Accidental/logged-URL data wipe (OJ-04) | OWASP API5; CWE-650 | Code review (`[HttpPost]` + `confirm` guard) | Manual | ✅ Implemented | Endpoint test asserting GET is rejected |
| Admin key constant-time, case-sensitive compare | Admin auth bypass / entropy loss (OJ-04) | ASVS V2.10 | Code review | Manual | ✅ Implemented | Unit test on the compare helper |
| tx_ref bound to caller | Cross-tenant transaction claim (OJ-05) | OWASP API1 | Code review | Manual | ✅ Implemented | Test asserting mismatched businessId rejected |
| Admin-key gate on free-pack activate | Free paid-feature self-grant (OJ-06) | OWASP API5 | Code review | Manual | ✅ Implemented | Endpoint authz test |
| JWT signing key unified | Latent crash / key hygiene (OJ-08) | CWE-1188 | Code review | Manual | ✅ Implemented | — |
| Dashboard CSP + headers | Clickjacking / injection defense-in-depth (OJ-09) | OWASP A05 | `next.config.mjs` `headers()` | Manual | ✅ Partial | Full nonce script-src CSP + runtime validation |
| MailKit patched | Vulnerable dependency (OJ-10) | A06:2021 | `dotnet list package --vulnerable` | Tool | ✅ Cleared | — |
| **Existing controls verified (no regression tests yet)** | | | | | | |
| Tenant isolation on CRUD queries | Cross-tenant read/write (BOLA) | OWASP API1; ASVS V4 | Code review (all services filter `BusinessId`) | Manual | ✅ Verified | Automated cross-tenant negative tests (needs DB harness) |
| `TokenVersion` session revocation | Stolen/stale token after credential change | ASVS V3.3 | `ActiveUserMiddleware` review | Manual | ✅ Verified | Integration test: old token 401s after password change |
| Webhook HMAC verification (Twilio/Paystack/Messenger/Telegram/Resend) | Forged inbound webhooks | OWASP API2 | Code review; constant-time compare | Manual | ✅ Verified | Signature negative tests |
| Webhook idempotency | Replay / duplicate events | ASVS V11 | `PaystackEventLog`/`InboundMessageClaim` unique keys | Manual | ✅ Verified | Replay integration test |
| Account lockout + auth rate limit | Brute force / credential stuffing | ASVS V2.2 | Code review | Manual | ✅ Verified (per-instance) | Shared-store test; distributed-attack test (OJ-16) |
| Image-upload validation pipeline | Malicious upload / pixel-flood | ASVS V12; CWE-434 | Code review (`BackgroundImageService`) | Manual | ✅ Verified | Fuzz/polyglot upload test |
| No SQL injection | Injection | OWASP A03; ASVS V5 | grep + review (parameterized only) | Manual | ✅ Verified | — |
| **Gaps with no coverage yet** | | | | | | |
| Destructive AI intent confirmation | Excessive agency (OJ-12/13) | OWASP LLM06 | — | — | ❌ Gap | Implement + test |
| Telegram/Messenger rate limiting | Denial-of-wallet (OJ-14) | CWE-770 | — | — | ❌ Gap | Implement + test |
| Voice-AI reservation ownership | Cross-tenant write (OJ-11) | OWASP API1 | — | — | ❌ Gap | Downstream fix + test |
| Transactional CSV import | Partial-import / double-count (OJ-26) | ASVS V11 | — | — | ❌ Gap | Implement + test |

Run: `dotnet test Ojunai.Tests/Security` → **13 passed / 0 failed** as of 2026-07-13.
