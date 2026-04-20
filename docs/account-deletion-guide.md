# BizPilot AI — Account Deletion Guide

**Last updated:** April 2026  
**Purpose:** Step-by-step guide for fully deleting a business account from the production database.

---

## When To Use This

- User requests full account deletion (GDPR/NDPR compliance)
- Cleaning up test accounts from production
- Removing demo accounts before go-live

---

## Prerequisites

1. SSH access to the production server
2. PostgreSQL access: `sudo -u postgres psql bizpilot_prod` or `psql -d bizpilot_prod`
3. The business ID (UUID) of the account to delete

---

## Step 1: Find the Business ID

```sql
SELECT "Id", "Name", "Currency", "Country", "CreatedAtUtc" 
FROM "Businesses" 
ORDER BY "CreatedAtUtc" DESC;
```

Or search by name:

```sql
SELECT "Id", "Name" FROM "Businesses" WHERE "Name" ILIKE '%searchterm%';
```

---

## Step 2: Verify the Account

Before deleting, confirm you have the right account:

```sql
SELECT b."Id", b."Name", b."Plan", b."Currency", b."Country",
       u."FullName", u."PhoneNumber", u."Email", u."Role"
FROM "Businesses" b
JOIN "Users" u ON u."BusinessId" = b."Id"
WHERE b."Id" = 'BUSINESS_ID_HERE';
```

---

## Step 3: Find the Phone Number (for onboarding cleanup)

```sql
SELECT "PhoneNumber" FROM "Users" 
WHERE "BusinessId" = 'BUSINESS_ID_HERE' AND "Role" = 'Owner';
```

Save this phone number — you'll need it in Step 5.

---

## Step 4: Delete All Business Data

Copy the entire block below, replace `BUSINESS_ID_HERE` with the actual UUID, and paste into psql:

```sql
BEGIN;
DELETE FROM "SaleItems" WHERE "SaleId" IN (SELECT "Id" FROM "Sales" WHERE "BusinessId" = 'BUSINESS_ID_HERE');
DELETE FROM "Sales" WHERE "BusinessId" = 'BUSINESS_ID_HERE';
DELETE FROM "Expenses" WHERE "BusinessId" = 'BUSINESS_ID_HERE';
DELETE FROM "InventoryTransactions" WHERE "BusinessId" = 'BUSINESS_ID_HERE';
DELETE FROM "StockHolds" WHERE "BusinessId" = 'BUSINESS_ID_HERE';
DELETE FROM "LedgerEntries" WHERE "BusinessId" = 'BUSINESS_ID_HERE';
DELETE FROM "Products" WHERE "BusinessId" = 'BUSINESS_ID_HERE';
DELETE FROM "Contacts" WHERE "BusinessId" = 'BUSINESS_ID_HERE';
DELETE FROM "DailySummaries" WHERE "BusinessId" = 'BUSINESS_ID_HERE';
DELETE FROM "MessageLogs" WHERE "BusinessId" = 'BUSINESS_ID_HERE';
DELETE FROM "PendingActions" WHERE "BusinessId" = 'BUSINESS_ID_HERE';
DELETE FROM "ImportJobs" WHERE "BusinessId" = 'BUSINESS_ID_HERE';
DELETE FROM "BillingEvents" WHERE "BusinessId" = 'BUSINESS_ID_HERE';
DELETE FROM "Users" WHERE "BusinessId" = 'BUSINESS_ID_HERE';
DELETE FROM "Businesses" WHERE "Id" = 'BUSINESS_ID_HERE';
COMMIT;
```

**Important:** The order matters. Child tables (SaleItems, InventoryTransactions, LedgerEntries) must be deleted before parent tables (Sales, Products, Contacts) due to foreign key constraints.

Each line should return `DELETE N` where N is the number of rows deleted. If any line fails, run `ROLLBACK;` to undo everything and investigate.

---

## Step 5: Delete Onboarding State

Using the phone number from Step 3:

```sql
DELETE FROM "OnboardingStates" WHERE "PhoneNumber" = '+PHONE_NUMBER_HERE';
```

This allows the phone number to re-register as a new account via WhatsApp.

---

## Step 6: Verify Deletion

```sql
SELECT COUNT(*) FROM "Businesses" WHERE "Id" = 'BUSINESS_ID_HERE';
-- Should return 0

SELECT COUNT(*) FROM "Users" WHERE "BusinessId" = 'BUSINESS_ID_HERE';
-- Should return 0

SELECT COUNT(*) FROM "OnboardingStates" WHERE "PhoneNumber" = '+PHONE_NUMBER_HERE';
-- Should return 0
```

---

## Quick Reference: FK Deletion Order

Delete tables in this order to avoid foreign key violations:

```
1.  SaleItems          (depends on Sales, Products)
2.  Sales              (depends on Businesses, Contacts)
3.  Expenses           (depends on Businesses)
4.  InventoryTransactions (depends on Businesses, Products)
5.  StockHolds         (depends on Businesses, Products)
6.  LedgerEntries      (depends on Businesses, Contacts)
7.  Products           (depends on Businesses)
8.  Contacts           (depends on Businesses)
9.  DailySummaries     (depends on Businesses)
10. MessageLogs        (depends on Businesses, Users)
11. PendingActions     (depends on Businesses, Users)
12. ImportJobs         (depends on Businesses)
13. BillingEvents      (depends on Businesses)
14. Users              (depends on Businesses)
15. Businesses         (root table)
16. OnboardingStates   (linked by phone number, not FK)
```

---

## Data Reset Only (Keep Account)

If you want to clear all transaction data but keep the business, users, and products:

```sql
BEGIN;
DELETE FROM "SaleItems" WHERE "SaleId" IN (SELECT "Id" FROM "Sales" WHERE "BusinessId" = 'BUSINESS_ID_HERE');
DELETE FROM "Sales" WHERE "BusinessId" = 'BUSINESS_ID_HERE';
DELETE FROM "Expenses" WHERE "BusinessId" = 'BUSINESS_ID_HERE';
DELETE FROM "InventoryTransactions" WHERE "BusinessId" = 'BUSINESS_ID_HERE';
DELETE FROM "StockHolds" WHERE "BusinessId" = 'BUSINESS_ID_HERE';
DELETE FROM "LedgerEntries" WHERE "BusinessId" = 'BUSINESS_ID_HERE';
DELETE FROM "DailySummaries" WHERE "BusinessId" = 'BUSINESS_ID_HERE';
DELETE FROM "MessageLogs" WHERE "BusinessId" = 'BUSINESS_ID_HERE';
DELETE FROM "PendingActions" WHERE "BusinessId" = 'BUSINESS_ID_HERE';
DELETE FROM "ImportJobs" WHERE "BusinessId" = 'BUSINESS_ID_HERE';
DELETE FROM "BillingEvents" WHERE "BusinessId" = 'BUSINESS_ID_HERE';
UPDATE "Products" SET "CurrentStock" = 0 WHERE "BusinessId" = 'BUSINESS_ID_HERE';
COMMIT;
```

This keeps the business, users, products (with stock reset to 0), and contacts intact.

---

## Bulk Delete Multiple Accounts

To delete multiple test accounts at once, replace the single UUID with an `IN` clause:

```sql
BEGIN;
DELETE FROM "SaleItems" WHERE "SaleId" IN (SELECT "Id" FROM "Sales" WHERE "BusinessId" IN ('ID1', 'ID2', 'ID3'));
DELETE FROM "Sales" WHERE "BusinessId" IN ('ID1', 'ID2', 'ID3');
-- ... repeat for all tables ...
COMMIT;
```

---

## Safety Notes

- **Always use `BEGIN`/`COMMIT`** — if anything fails, `ROLLBACK` undoes everything
- **Always verify the business ID first** — wrong ID deletes the wrong account
- **This is irreversible after `COMMIT`** — there is no undo
- **Back up first if unsure:** `pg_dump bizpilot_prod > backup_$(date +%Y%m%d).sql`
- **Do not delete during peak hours** — large deletes can lock tables

---

*This document is for internal admin use only.*
