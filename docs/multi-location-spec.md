# Multi-location support — design spec

**Status:** PLAN (not built). Drafted 2026-07-27 from a codebase survey + 3-lens design pass.
Lets one business operate several physical locations (branches/warehouses) with per-location stock,
sales, expenses and reporting, while the single-location experience stays byte-for-byte unchanged.

## Decisions locked with the owner
1. **Entitlement: Scale/Enterprise included; add-on for everyone below.** Multi-location stays where the
   catalog already grants it (Scale + Enterprise). Pro and lower tiers obtain it via the existing
   `addon.multi_location` (~$10.99/mo). So the original "Pro and higher" ask is satisfied **via the
   add-on path**, not by moving the tier flag — which means **no price-sheet change**.
2. **Quota: fixed cap per tier/entitlement, config-driven** (e.g. Scale up to N, add-on holders up to M),
   read from `PlanCatalog` so it's tunable without a deploy. Boolean gate ("may have >1 location") +
   a separate numeric quota.
3. **Shared catalog, per-location stock only.** One business-wide SKU/price/variant/bundle list; only the
   *quantity* is per-location. (Per-location catalogs/pricing explicitly deferred.)
4. **Downgrade = soft-deactivate, never delete** (default policy; see §7).

## Current state — where single-location is baked in (survey)
- **Stock is a single pool:** `Product.CurrentStock` (Product.cs:12) is one decimal; every mutation
  (InventoryService, SalesService bundle depletion, PO receive, stocktake, holds, batches) writes it directly.
- **Transactions are BusinessId-only:** `Sale`, `Expense`, `InventoryTransaction`, `StockHold`,
  `Stocktake`, `ProductBatch`, `PurchaseOrder`, `LedgerEntry`, `ContactIdentity` have no location dimension.
- **Receipts:** business-wide `Business.NextReceiptNumber` atomic counter.
- **`DailySummary`** is uniquely keyed `(BusinessId, Date)`.
- **Gating scaffold already exists but is unwired:** legacy `PlanLimits.HasMultiBranch` (Scale only),
  v2 `PlanCatalog` `"multi_location"` feature (Scale/Enterprise), and `addon.multi_location`
  (AddOnCatalog) — but `PlanGuard.CheckFeatureAsync` only reads the legacy `"multi_branch"` string, and
  **there is no `Location` model**. This is a wiring + data-model job, not a pricing redesign.
- **Bot/Voice resolve to ONE business:** inbound → `ContactIdentity.BusinessId`; Voice AI is a 1:1
  `Business ↔ VoiceAIBusinessId` link. No location signal anywhere in that chain.

## Data model
- **New `Location`**: `Id, BusinessId, Name, Type(branch|warehouse), IsDefault, IsActive`, per-location
  overrides `Address/City/State, Currency?, Timezone?, NextReceiptNumber, ReceiptPrefix?`. Partial unique
  index `(BusinessId) WHERE IsDefault` — exactly one default per business. Overrides coalesce to `Business.*`.
- **New `ProductLocationStock`** (the core call): `Id, BusinessId, ProductId, LocationId, CurrentStock,
  LowStockThreshold?` (coalesces to `Product.LowStockThreshold`), `Version` (optimistic concurrency).
  Unique `(ProductId, LocationId)`. **Product catalog stays business-wide** — only quantity moves here.
- **`Product.CurrentStock` is KEPT** as a maintained denormalized roll-up (`SUM(ProductLocationStock)`), so
  every existing business-wide query keeps returning the right number. Invariant enforced by funnelling all
  mutations through one location-aware method + a periodic reconciliation check.
- **Nullable `LocationId`** added to Sale, Expense, InventoryTransaction, StockHold, Stocktake/StocktakeItem,
  ProductBatch, PurchaseOrder (as `ReceivingLocationId`), LedgerEntry, ContactIdentity. NULL = default/legacy.
  Additive composite indexes `(BusinessId, LocationId, CreatedAtUtc)` — old indexes kept.
- **`UserLocation`** join table (`UserId, LocationId`): owners/admins span all locations (no rows = all-access,
  back-compat); staff with ≥1 row are scoped. **Not** in the JWT — resolved server-side.
- **New `StockTransfer` / `StockTransferItem`** (Phase 3): a transfer = two InventoryTransactions
  (`TransferOut`/`TransferIn`) in one DB transaction; business-wide `Product.CurrentStock` nets to zero.

## Scoping
- Requests carry **`X-Location-Id`** (header/query), validated against the caller's `UserLocation` set.
  `null`/"ALL" ⇒ today's business-wide aggregation. A specific id ⇒ filter `(BusinessId, LocationId)`.
- `BusinessDto` gains `locations[]` + `isMultiLocation`. Dashboard adds a `useLocation()` hook + a nav
  switcher (hidden when `!isMultiLocation`).

## Tier-gating (single gate, two backends)
Add `PlanGuard.CanUseMultiLocationAsync(businessId)` + `GetLocationQuotaAsync(businessId)`; route **all**
enforcement through it. Resolves: if `Business.PricingV2Enabled` → `PlanCatalog[plan].Features` contains
`"multi_location"` (Scale/Enterprise) **OR** an active `BusinessAddOn` granting it; else legacy
`HasMultiBranch`. Enforcement points (fail-closed): Location-create endpoint (2nd+ location requires gate
pass AND count < quota; the default location is always allowed), `X-Location-Id` resolution (a gated
business is pinned to its default), add-on activation webhook (flips the gate on). **Unify the three grant
paths before shipping any location UI** (else buy-add-on-but-legacy-denies inconsistencies).

## Migration + backfill (strictly additive)
Additive EF migration: create `Location`, `ProductLocationStock`, `UserLocation` (+ `StockTransfer*` in
Phase 3); add nullable `LocationId` columns + additive indexes; add `Business.DefaultLocationId`; keep
`Product.CurrentStock` and `Business.NextReceiptNumber`; the **one** index replacement is `DailySummary`
→ `(BusinessId, LocationId, Date)` (backfill LocationId first). **Prod deploy scripts skip migrations**, so
schema + backfill run as an explicit, re-runnable step against `ojunai_prod`, batched by BusinessId.
Backfill: one default **"Main"** `Location` per business (receipt counter/currency/timezone seeded from
Business); one `ProductLocationStock` per product mirroring `CurrentStock`; stamp historical rows (or
coalesce NULL→default at read). Register/business-creation must also seed the default location + stock rows.

## Phasing
- **Phase 0 — schema + backfill, dark.** Nothing reads locations; ~55 sites verified unchanged. Invariant:
  `SUM(ProductLocationStock)==Product.CurrentStock`, exactly one default Location each.
- **Phase 1 — location-aware write path, single read.** Mutations dual-write `ProductLocationStock` + keep
  the `CurrentStock` roll-up; reads still business-wide. De-risks writes with zero UX change.
- **Phase 2 — gate + Location CRUD + dashboard switcher + roll-up + optional per-location reports;** wire
  add-on activation in the Paystack/Flutterwave/Stripe webhooks.
- **Phase 3 — per-location depth:** stocktakes/holds/PO-receiving/batches by location, per-location
  thresholds/alerts, **stock transfers**, per-location P&L/aging/turnover/heatmaps + PDF exports.
- **Phase 4 — bot/Voice per-location routing** (see below).

## Conversational resolution (Phase 4) — the genuinely hard part
At inbound bot/voice time, resolve the location AFTER business resolution, BEFORE any write, via a 3-tier
fallback (first non-null wins): (1) **channel-bound** location (a branch's own WhatsApp/Telegram number →
`ContactIdentity.LocationId`); (2) **staff default** (`User.DefaultLocationId`); (3) **in-conversation
"Which branch?"** one-tap button prompt (reuse the pending-action/button-callback machinery; buttons are
rate-limit-exempt) for ambiguous **write** intents only — read intents roll up business-wide. Single-location
businesses hit tier 1 instantly (zero friction). **Multi-location writes must never silently default** to the
wrong branch — fail-closed/prompt. **Voice AI = handoff to the separate Voice team** (their repo,
ken012/Inventory-VoiceAI): Ojunai sends `LocationId` on provision + accepts it back on the sale/reservation
webhook; do not edit that repo here.

## Downgrade / add-on lapse
Non-destructive: gate flips false → extra (non-default) locations `IsActive=false`, read-only (data + history
retained, reactivate intact on re-upgrade). Default location always stays active. Reuse the existing
grace-period + expiry-job pattern; **warn before freezing** (freezing a branch's POS mid-day is a support
incident). Frozen-location stock is NOT auto-merged into default (would corrupt counts) — prompt to transfer.

## Top risks
- **Roll-up drift** (`CurrentStock` vs per-location) → one mutation method + reconciliation check.
- **Silent bot mis-attribution** → multi-location writes prompt, never default.
- **Oversell across locations** → sale validates the RESOLVED location's stock, not the business total.
- **Bundle depletion** must hit the sale's location, not the global pool.
- **Backfill on prod** is a manual, idempotent, verified step (deploy skips migrations) — a half-applied
  backfill shows zero stock; verify counts before flipping any gate.
- **Report double-counting** — roll-up queries must exclude Transfer-type transactions.
- **Gate inconsistency** — unify the 3 grant paths before exposing location UI.

## Remaining sub-decisions (not blocking Phase 0/1)
- Quota numbers per tier + per add-on (config values).
- Receipt numbering: per-location series vs business-wide + prefix.
- `DailySummary`: change the key vs a parallel `LocationDailySummary` table.
- Staff scope default (all-access vs explicit) and whether `MaxStaff` is per-business or per-location.
- Per-location currency overrides (interacts with the "currency choice is free" policy).
- Same customer messaging two branch numbers → one lead or two.
