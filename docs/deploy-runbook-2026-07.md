# Deploy Runbook — July 2026 inventory + billing release

Covers the **39-commit** stack on `main` (origin/main..main), commits `9119a71 → 80e1a81`.
Ships the Stocky-replacement inventory build (POs, barcode, stocktake, bundles, variants,
batch/expiry), billing/pricing fixes, multi-channel alerts, CSV Phase 1 + update-details,
unit pluralization, PWA/mobile fixes, and variant-styles-in-list.

Server: `bizpilot@46.225.108.35` · API `/var/www/ojunai-api` (systemd `ojunai-api`, port 5000)
· Dashboard `/var/www/ojunai-dashboard` (pm2 `ojunai-dashboard`, port 3000) · DB `ojunai_prod`.

---

## 0. Pre-flight — verified ✅ (2026-07)
- **6 EF migrations, all additive in `Up()`** (AddColumn / CreateTable / CreateIndex only — no
  Drop/Alter/Rename). Drops exist only in `Down()` (rollback), as expected:
  `AddPerChannelSaleConfirmation`, `AddPurchaseOrdersAndProductSourcing`, `AddStocktakes`,
  `AddBundles`, `AddVariantGroups`, `AddProductBatches`.
- **`dotnet ef migrations has-pending-model-changes` → "No changes since the last migration."**
  (model matches migrations — no drift).
- Migrations **auto-apply on startup** (`Program.cs:453 await db.Database.MigrateAsync()`).
- **API builds** (`dotnet build` 0 errors) · **Dashboard builds** (`next build` clean).
- **Payments:** no `(int)` money truncation left; Flutterwave rounds to 2 dp
  (`FlutterwaveService.cs:409`); BillingConfig rounds monthly-from-annual.
- **Stock math:** sale create/void/return are transactional (`BeginTransactionAsync`);
  PO-receive is transactional (increments stock, keeps last cost, learns supplier);
  bundle sales deplete components with a guard; variants are plain products (normal decrement).

**Verdict: GO.** Additive-only migrations mean a code rollback never needs a DB rollback.

---

## 1. Backup the DB first (do NOT skip)
```
./scripts/backup-db.sh          # fresh off-box snapshot to R2 before touching prod
```
Confirm it reports success and a new object in R2. This is your restore point if anything
goes wrong with the migrations.

## 2. Push the code (so prod == origin)
```
git push origin main            # 39 commits
```

## 3. Deploy — API FIRST, then dashboard
Order matters: migrations are additive, so deploying the API first adds the new
columns/tables while the **old** dashboard keeps working. Dashboard-first would let the new
UI call fields the old API lacks.

```
./scripts/deploy-api.sh         # builds, backs up (keeps last 5), uploads, restarts,
                                # polls /health for 30s (waits through migrations + Hangfire)
```
Wait for `✓ Health check passed`. Then:
```
./scripts/deploy-dashboard.sh   # builds locally + on server, pm2 restart, checks :3000
```

## 4. Post-deploy smoke (5 min)
- **Health:** `curl -fs https://<api-host>/health` → 200.
- **Migrations landed:** `sudo -u postgres psql ojunai_prod -c "\dt"` shows `PurchaseOrders`,
  `Stocktakes`, `BundleComponents`, `VariantGroups`, `ProductBatches`.
- **App loads:** dashboard opens, existing products/sales/reports render (no console errors).
- **Billing:** open the upgrade UI in NGN + one FX currency (USD/GBP) → amounts correct.
- **Stock mutation:** record one sale (stock decrements), receive a small PO (stock rises,
  shows on mobile), all still correct.
- **Bot:** send "stock" on WhatsApp → replies with pluralized units.
- Full functional pass: `docs/pre-deploy-qa-full.md` (if saved) / the QA script from chat.

## 5. Rollback (if needed)
Because every migration is additive, **roll back code only — never run `Down()`** (that would
drop the new columns/tables and lose data).

- **API:** on the server, restore the previous build and restart:
  ```
  sudo ls -1t /var/www/ojunai-api-backups | grep '^api-' | head   # find latest pre-deploy
  sudo cp -r /var/www/ojunai-api-backups/api-<TS>/* /var/www/ojunai-api/
  sudo systemctl restart ojunai-api
  ```
- **Dashboard:** restore `.next` from `/var/www/ojunai-dashboard-backups/dashboard-<TS>` and
  `pm2 restart ojunai-dashboard`.
- The new (unused) columns/tables sitting in the DB are harmless to the old code.
- Only if the DB itself is corrupted: restore from the step-1 R2 snapshot.

---

## Notes / watch-items
- First API start after deploy runs 6 migrations — the 30s health poll covers it, but on a
  large `Products`/`Sales` table the CreateIndex ops may take a few extra seconds; if the poll
  times out, check `sudo journalctl -u ojunai-api -n 50` before assuming failure.
- Variant low-stock alerts still name the individual variant (findable now via the new
  "Variant styles" row). Rolling them up to the style is a queued follow-up, not a blocker.
