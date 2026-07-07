# Product Variants — Implementation Plan (proposal, for sign-off)

**Status:** proposal — no code written. This is the Tier-2 "variants" feature (size/color/etc.).
**Author:** engineering. **Date:** 2026-07.

---

## 1. Goal

Let a merchant manage one *style* (e.g. "Classic Tee") that comes in multiple **variants**
(Red/S, Red/M, Blue/L…), where **each variant has its own stock, price, SKU and barcode**, while
the dashboard presents them grouped under the style instead of as N unrelated rows.

## 2. Why this was flagged as the risky one

The app is built around a **flat `Product`** (own `CurrentStock`, `SellingPrice`, `CostPrice`) that
is sold as `SaleItem { ProductId, Quantity, UnitPrice }`. A "true" variant model adds a dimension
*below* the product, which — done naively — ripples through **every** write and read path: sales,
inventory transactions, receipts, all reports, purchase orders, stocktake, bundles, barcode lookup,
and the WhatsApp/Telegram/Messenger natural-language resolver.

The key realisation that de-risks this: **in this codebase every SKU is already its own `Product`.**
Merchants already create "Tee Red S", "Tee Red M" as separate products today. So variants don't have
to be a new sub-entity — they can be a **grouping + UX + reporting layer over existing products.**

## 3. Two approaches

### Approach A — Nested variant entity (the "e-commerce proper" model)
`Product` becomes a template; a new `ProductVariant` table holds per-variant stock/price/SKU/barcode;
`SaleItem`, `InventoryTransaction`, receipts, and every report gain a `VariantId`.

- ✅ Textbook-clean data model.
- ❌ **Invasive & high-risk.** Threads `VariantId` through the sale path, inventory, receipts, all
  reports, the bot resolver, PO/stocktake/bundle. Exactly the blast radius we want to avoid pre-launch.

### Approach B — Grouped products (recommended)
Each variant **stays a full `Product`** (own stock/price/SKU/barcode). We add a lightweight
**`VariantGroup`** (the "style") and tag its member products. Grouping/rollup/UX sit *on top*.

- ✅ **Sale, inventory, receipt, PO, stocktake, bundle, barcode paths are UNCHANGED** — each variant is
  a product, so all existing machinery just works. Low risk, fits the codebase.
- ✅ Ships incrementally and feature-flagged; zero change for merchants who don't turn it on.
- ⚠️ Reports are naturally per-variant; a per-style **rollup** is an additive extra (Phase V2).
- ⚠️ The product list needs a grouping view so a 6-variant style isn't 6 loose rows.

| | Approach A (nested) | **Approach B (grouped) — recommended** |
|---|---|---|
| Data model purity | Higher | Good enough (each variant = product) |
| Sale/inventory/receipt/report changes | **Everywhere** | **None** (variants are products) |
| Bot resolver changes | Required | Optional (naming/aliases work v1) |
| Risk to existing flow | **High** | **Low** (additive) |
| Effort | Large | Medium |
| Reversibility | Hard | Easy (drop the group layer) |

**Recommendation: Approach B.** It delivers the merchant value (manage a style, per-variant
stock/price/barcode, grouped display, sell/scan each variant) without touching the money/stock paths.

## 4. Data model (Approach B)

**New `VariantGroup`** (the style):
- `Id`, `BusinessId`
- `Name` — style name, e.g. "Classic Tee"
- `Axes` — JSON array of option names, e.g. `["Size","Color"]`
- `Category?` (shared), `CreatedAtUtc`

**`Product` gains (both nullable → additive, existing rows unaffected):**
- `VariantGroupId Guid?` — null = standalone product (today's behaviour); set = a variant of a style.
- `VariantOptions string?` — JSON of this variant's values, e.g. `{"Size":"M","Color":"Red"}`.

That's the whole schema change: **one new table + two nullable columns.** No changes to `SaleItem`,
`InventoryTransaction`, receipts, or reports.

## 5. Phased, feature-flagged rollout

Gate the whole thing behind an opt-in flag (see Decision 2) so nothing changes for existing merchants
until enabled.

**Phase V1 — Create & sell grouped variants (the core):**
- `VariantGroup` entity + the two `Product` columns + migration (additive).
- Endpoints: create a style (name + axes + values → **generates the variant `Product` rows**),
  list/get a style with its variants, add/remove a variant, ungroup.
- Auto-name variants for display + bot matching, e.g. `"Classic Tee — Red / M"` (see Decision 3).
- Dashboard: a "variant product" creator (define axes + values → grid of variants, each with
  price/stock/barcode); **grouped product list** (one expandable style row: N variants, total stock,
  price range) with standalone products rendering exactly as today.
- **Everything downstream works unchanged** — selling/scanning/PO/stocktake/bundling a variant is just
  selling/scanning/etc. a product.

**Phase V2 — Rollups & polish (additive):**
- Reports: optional **"group by style"** rollup (per-variant reports keep working untouched).
- **"Group existing products into a style"** tool for merchants who already made separate SKUs.
- Bot resolver: match option values ("sell 2 red medium tees") beyond just the auto-name/aliases.

**Phase V3 — Power features (optional, later):**
- Matrix stock view; bulk price/stock edits across a style; per-axis images.

## 6. Integration points & how each is handled

| Area | Impact under Approach B |
|---|---|
| Sales (`SalesService.CreateAsync`) | **None** — a variant is a product; sale records it as today. |
| Inventory / stocktake / wastage | **None** — per-product, already per-variant. |
| Purchase Orders | **None** — order/receive a variant like any product (bonus: PO lines can target variants). |
| Bundles | **None** — a component can already be any product (i.e. a variant). |
| Receipts | **None** — line shows the variant's product name (auto-named "Style — Red / M"). |
| Barcode scan | **None** — each variant has its own barcode already. |
| Low-stock alerts | **None** — per-variant, already works. |
| Reports | Unchanged (per-variant). Optional style rollup added in V2. |
| Product list UI | **New grouped view** (the main UI work). |
| Bot NL resolver | Works v1 via variant naming/aliases; smarter option matching in V2. |

## 7. Hard problems (called out honestly)

1. **Bot natural-language variant selection.** "sell 2 red medium tees" → the exact variant. V1 leans
   on auto-naming + aliases so the *existing* resolver matches; a caller who's vague ("sell a tee")
   gets a clarify prompt or the resolver picks by best match. Proper axis-value matching is V2. This
   is the fuzziest area and the main reason to phase it — not a launch blocker.
2. **Reporting granularity.** Per-variant is the default and stays correct. A per-style rollup is an
   additive report, not a rewrite. No breakage either way.
3. **Migrating existing separate SKUs.** Merchants who already made "Tee Red S" etc. as loose products
   get an optional "group into a style" tool in V2. Not required to launch V1.
4. **List UX at scale.** A style with 30 variants must collapse cleanly. Grouped/expandable rows.

## 8. Decisions needed from you

1. **Approach** — confirm **B (grouped products)** vs A (nested entity). *Recommend B.*
2. **Gating** — feature-flag per business? plan-gated (e.g. Pro+ only)? or on for everyone? *Recommend
   an opt-in flag so it's invisible until enabled.*
3. **Auto-naming convention** for variants (display + bot matching) — e.g. `"{Style} — {v1} / {v2}"`.
4. **V1 scope** — ship create/manage/sell grouped variants now, defer reporting rollup + bot
   option-matching + the grouping-migration tool to V2? *Recommend yes.*

## 9. Effort & risk

- **Approach B, Phase V1:** ~medium effort (new group entity + the variant-creator UX + grouped list).
  **Low risk** — purely additive; sale/stock/report paths untouched; feature-flagged.
- **Approach A:** large effort, **high risk** (touches the sale/receipt/report/resolver paths). Not
  recommended for a pre-launch product.

**Bottom line:** with Approach B, variants stops being "the scary invasive one" and becomes a
contained, additive, feature-flagged layer — mostly UI + a thin grouping model — that reuses the
inventory/sales machinery we already have.
