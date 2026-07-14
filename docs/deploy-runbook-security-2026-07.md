# Deploy Runbook — Security release (2026-07)

**What's shipping:** `main` @ `56eee03` — the merge of `security/audit-fixes-2026-07`
(two audit passes: OJ-01…OJ-29 + F00…F29). **Code-only — NO DB migrations.**
Scalability P1s are already live from the June deploy; this deploy is security fixes only.

**Verified before merge:** API builds (0 errors), dashboard builds, `Ojunai.Tests.Security`
34/34 pass, clean fast-forward onto main.

Server: `bizpilot@46.225.108.35` · API systemd `ojunai-api` (:5000) · dashboard pm2 `ojunai-dashboard` (:3000) · DB `ojunai_prod`.

---

## A. Pre-deploy (5 min)

1. **Fresh DB backup** (restore point). `backup-db.sh` runs **on the prod box**, not the Mac
   (it needs `/etc/ojunai/backup.env` + Postgres). Run the installed copy as root:
   ```bash
   ssh -t bizpilot@46.225.108.35 'sudo /usr/local/bin/ojunai-backup-db.sh'
   ```
   (Optional for this release — it's code-only/no-migration, so last night's auto-backup is
   already a valid restore point.)
2. **⚠️ Confirm `Flutterwave:SecretKey` is set correctly in prod env.** The Flutterwave webhook
   now does a **server-to-server verify and fails closed** — if this key is missing/wrong,
   legitimate payments won't activate. This is the release's only functional dependency.
3. **Env hygiene:** `ASPNETCORE_ENVIRONMENT=Production`; `Cors:AllowedOrigins` = the real dashboard
   origin (not `*`); `Jwt:Secret` ≥32 bytes.
4. **Keep Telegram/Messenger channel-native SIGNUP disabled** (F05 — unfixed by design; do not enable).

## B. Deploy — API first, then dashboard

You're on `main` locally. No migration, so no special wait.
```bash
git checkout main && git pull            # ensure latest (should already be 56eee03)
./scripts/deploy-api.sh                   # wait for "✓ Health check passed"
./scripts/deploy-dashboard.sh             # ships CSP + export-XSS fix (built on server)
```

## C. Post-deploy smoke — do immediately

### 🔴 Critical (the release's main risk)
1. **Flutterwave activation still works** — run **one real sandbox charge** end-to-end → plan/pack
   activates. Proves the new fail-closed verify didn't break the legit path.
2. **Paystack activation still works** — one sandbox charge → activates; also confirm an **annual NGN**
   renewal activates (F27 touched annual handling).

### 🟠 Security fixes fire as intended
3. **Export/report XSS (F01/F03):** set a business name to `"><b>x</b>` → open the export **print / PDF**
   view → renders as literal text, no HTML/script executes.
4. **Export PIN lockout (OJ-03):** wrong download PIN several times → rate-limited/locked; correct PIN works.
5. **Destructive-AI confirmation (OJ-12 / F00 / F02):** message the bot "delete all products" /
   "clear all debts" / "set all stock to zero" → it **asks to confirm** (doesn't execute); confirm →
   executes; deny/ignore → nothing happens. Combined phrasing ("zero all stock and clear debts") → still gated.
6. **Staff self-grant (OJ-13):** "add staff … as admin" via chat → blocked / confirmation echo; can't
   create Owner/Admin from chat.
7. **Channel rate limit (OJ-14):** >15 messages/min to Telegram/Messenger bot → rate-limited **before**
   the paid Claude call.
8. **Admin endpoints (OJ-04):** `wipe-*` via GET is rejected; via POST with `confirm=<businessId>` works;
   admin key is case-sensitive; `X-Admin-Key` header works.
9. **Dashboard CSP (OJ-09):** dashboard loads and works (charts, images, print) with no CSP errors
   breaking functionality in the console.

### 🟡 Core regression (nothing broke)
10. Login / logout / password reset.
11. Record a sale (stock decrements), receive a small PO, WhatsApp "stock" → normal replies.
12. Existing products / sales / reports load normally.

## D. Operator hardening (≤7 days — separate from the code deploy)
- Rotate **`Flutterwave:WebhookSecret`** — update the Flutterwave dashboard **and** prod env *together*
  (must match or inbound webhooks fail signature).
- Rotate **`Admin:AnalyticsKey`** (≥32 chars); move operator tooling to the **`X-Admin-Key` header**.
- **nginx:** an external request to `/hangfire` must be denied; access logs scrub `key`/`token` params.

## E. Rollback
Code-only, no migrations → restore the previous build and restart; no DB action.
- **API:** `sudo cp -r /var/www/ojunai-api-backups/api-<TS>/* /var/www/ojunai-api/ && sudo systemctl restart ojunai-api`
- **Dashboard:** restore `.next` from `/var/www/ojunai-dashboard-backups/dashboard-<TS>` → `pm2 restart ojunai-dashboard`

---

**Highest-value single check: C.1 (Flutterwave sandbox charge).** If a legit charge activates cleanly, the risky part of this release is proven.
