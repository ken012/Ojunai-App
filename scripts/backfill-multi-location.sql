-- Multi-location Phase 0 backfill — IDEMPOTENT & re-runnable.
--
-- Run AFTER the AddMultiLocationSchema migration has applied. Gives every existing business a default
-- "Main" location, points Business.DefaultLocationId at it, and mirrors each product's single-pool
-- CurrentStock into one ProductLocationStock row at that default location. Nothing reads these yet
-- (Phase 0 is dark), so this is purely data-priming and safe to run repeatedly. Each step is guarded
-- with NOT EXISTS / IS DISTINCT FROM so a second run is a no-op.
--
-- Prod: deploy scripts skip migrations, so apply the migration + run this explicitly against ojunai_prod,
-- off-peak. On the current ~55-site scale this is near-instant; at larger scale, batch step 3 by business.
--
-- Usage:  psql "$CONN" -f scripts/backfill-multi-location.sql

BEGIN;

-- 1) One default "Main" location per business. Address/City/State/Currency/Timezone are left NULL so they
--    coalesce to the Business values (single-location businesses keep one source of truth); the receipt
--    counter + prefix ARE seeded so a future per-location receipt series continues unbroken.
INSERT INTO "Locations"
    ("Id","BusinessId","Name","Type","IsDefault","IsActive","NextReceiptNumber","ReceiptPrefix","CreatedAtUtc")
SELECT gen_random_uuid(), b."Id", 'Main', 'branch', true, true, b."NextReceiptNumber", b."ReceiptPrefix", now()
FROM "Businesses" b
WHERE NOT EXISTS (
    SELECT 1 FROM "Locations" l WHERE l."BusinessId" = b."Id" AND l."IsDefault" = true
);

-- 2) Point each business at its default location.
UPDATE "Businesses" b
SET "DefaultLocationId" = l."Id"
FROM "Locations" l
WHERE l."BusinessId" = b."Id" AND l."IsDefault" = true
  AND b."DefaultLocationId" IS DISTINCT FROM l."Id";

-- 3) One ProductLocationStock per product at its business's default location, mirroring the current
--    single-pool CurrentStock. After this, SUM(per-location stock) == Product.CurrentStock by construction.
INSERT INTO "ProductLocationStocks"
    ("Id","BusinessId","ProductId","LocationId","CurrentStock","LowStockThreshold")
SELECT gen_random_uuid(), p."BusinessId", p."Id", b."DefaultLocationId", p."CurrentStock", NULL
FROM "Products" p
JOIN "Businesses" b ON b."Id" = p."BusinessId"
WHERE b."DefaultLocationId" IS NOT NULL
  AND NOT EXISTS (
    SELECT 1 FROM "ProductLocationStocks" pls
    WHERE pls."ProductId" = p."Id" AND pls."LocationId" = b."DefaultLocationId"
  );

COMMIT;

-- ── Verification (all three should report 0 problem rows) ──────────────────────────────────
\echo 'Businesses without exactly one default location (want 0):'
SELECT b."Id", count(l.*) AS default_locations
FROM "Businesses" b
LEFT JOIN "Locations" l ON l."BusinessId" = b."Id" AND l."IsDefault" = true
GROUP BY b."Id" HAVING count(l.*) <> 1;

\echo 'Products missing a stock row at their default location (want 0):'
SELECT p."Id"
FROM "Products" p JOIN "Businesses" b ON b."Id" = p."BusinessId"
LEFT JOIN "ProductLocationStocks" pls ON pls."ProductId" = p."Id" AND pls."LocationId" = b."DefaultLocationId"
WHERE pls."Id" IS NULL;

\echo 'Products where per-location stock != business-wide CurrentStock (want 0):'
SELECT p."Id", p."CurrentStock", pls."CurrentStock" AS location_stock
FROM "Products" p JOIN "Businesses" b ON b."Id" = p."BusinessId"
JOIN "ProductLocationStocks" pls ON pls."ProductId" = p."Id" AND pls."LocationId" = b."DefaultLocationId"
WHERE p."CurrentStock" <> pls."CurrentStock";
