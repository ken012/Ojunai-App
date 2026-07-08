# Ojunai — Pre-Deploy QA Checklist

Covers the full undeployed stack: the Stocky-replacement build (Purchase Orders, barcode,
Stocktake, Bundles, Variants, Batch/Expiry) plus the earlier batch (Chat Channels, live pricing,
receipt preview, Voice settings, alerts, password). Do the ⚠️ money/stock items first.

## 0. Deploy & verify
- [ ] `./scripts/deploy-api.sh` → `✓ Health check passed`.
- [ ] Migrations applied cleanly: `sudo journalctl -u ojunai-api --since '5 min ago' | grep -iE 'migrat|error'` — expect `AddPurchaseOrdersAndProductSourcing`, `AddStocktakes`, `AddBundles`, `AddVariantGroups`, `AddProductBatches` (+ `AddPerChannelSaleConfirmation` if not already), no errors.
- [ ] `./scripts/deploy-dashboard.sh` → `✅ Dashboard deployed`.

## 1. ⚠️ Regression — existing flows unchanged
- [ ] Record a normal sale (dashboard) → stock down, receipt works.
- [ ] Sale via WhatsApp bot ("sold 2 rice for 5k") → works, stock down.
- [ ] Restock / Stock-out / Adjust / Damage / Wastage on a normal product → all as before.
- [ ] Low-stock alert fires (WhatsApp + dashboard bell) for a normal product.
- [ ] Inventory list, search, stock filters, product create/edit → normal.
- [ ] Credit sale → receivable appears in Contacts & Ledger.

## 2. ⚠️ Purchase Orders
- [ ] Create PO with a supplier + 2 products → saves as Draft.
- [ ] Receive in full → stock += received, cost updated, payable to supplier for the total.
- [ ] Partial receive → status "Partial", stock/payable only for received; receive rest → "Received".
- [ ] No-supplier PO receive → stock updates, no payable, no error.
- [ ] Over-receive attempt → clamps to remaining.
- [ ] Uncheck "record payable" → stock updates, no payable.
- [ ] Mark sent / cancel (Received PO can't be cancelled).

## 3. Barcode (rear camera confirmed working)
- [ ] Product edit → set a barcode (scan or type), save → persists.
- [ ] Scanner shows the live REAR-camera feed; auto-reads a barcode.
- [ ] Inventory "Scan" → known barcode opens that product; unknown → Add Product with barcode prefilled.
- [ ] Record Sale "Scan" → adds the matched product; scan same item again → quantity bumps.
- [ ] Deny camera → graceful message (no crash); the status line reports the reason.

## 4. Stocktake
- [ ] Start a count (all / by category) → snapshots system stock.
- [ ] Enter counts → live per-row + net variance (red short / green surplus).
- [ ] Complete → each counted product's stock = your count; an Adjustment shows in its history.
- [ ] Cancel → nothing changes.

## 5. ⚠️ Bundles
- [ ] Mark a product a bundle with 2 components (qty each), save.
- [ ] Sell 1 bundle (dashboard) → each component drops by its qty (not the bundle); component StockOut notes "bundle: …".
- [ ] Sell a bundle via bot → same component depletion.
- [ ] Sell a bundle when a component is short → "not enough X to make Y", sale blocked.
- [ ] Bundle does NOT appear in low-stock alerts; shows a "Bundle" badge in the list.

## 6. Variants (opt-in)
- [ ] Inventory → Variants → Enable → create style "Tee" (Size S,M,L × Color Red,Blue) → 6 variants generated.
- [ ] Set price/stock per variant; add a variant; dissolve keeps them as standalone products.
- [ ] Sell one variant (normal sale; search finds it) → that exact variant's stock drops.
- [ ] Variant members don't clutter the inventory list; "All" chip count matches the list.
- [ ] With variants disabled (a test business) → no Variants button, list unchanged.

## 7. Batch / expiry (opt-in per product)
- [ ] Product edit → tick "Track batches & expiry", save.
- [ ] Restock that product → Expiry date + Lot# fields appear; set them → lot recorded.
- [ ] Inventory → Expiry (`/expiring`) → lot shows under 7/30/90 window; expired flagged red.
- [ ] Write off a lot → records a wastage (stock drops) + lot closes; shows in wastage report.
- [ ] A non-batch product's restock shows NO expiry fields (unchanged).

## 8. Earlier undeployed batch (regression on prior work)
- [ ] Settings → Chat Channels renders; connect/disconnect + per-channel sale-confirm toggles.
- [ ] Settings → Plan & Billing: real prices (not ₦0) in your currency; one currency selector drives tiers AND WhatsApp packs.
- [ ] Receipt preview opens the sample PDF overlay.
- [ ] Voice AI settings save (7 languages, greeting, voices) — if the voice backend has the new schema.
- [ ] Alerts: business alerts only fire once an alert channel is selected (new `none` default).
- [ ] Password: show/hide toggle; policy enforced; birth-year change → toast + bell alert (year not shown).
- [ ] Flutterwave: a USD/GBP renewal is set up at the exact price (no dropped cents) — verify a test sub if feasible.

## 9. Cross-cutting
- [ ] Permissions: a Viewer/Sales role can't create POs / stocktakes / receive / write-off / manage variants (buttons hidden, endpoints 403).
- [ ] Mobile: scanner works over HTTPS on a phone (rear camera); pages render.
- [ ] No console/network errors on `/purchasing`, `/stocktake`, `/variants`, `/expiring`.

## Notes
- Camera scanning needs HTTPS + a real browser (not an in-app browser like WhatsApp/Instagram).
- Any account with a blank AlertChannel now has business alerts off until a channel is picked (intended new default).
- Deferred to later phases: FEFO auto-depletion on sale (V2), Variants V2 (bot NL matching, style rollup, group-existing-SKUs), forecasting, stock transfers.
