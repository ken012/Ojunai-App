# BizPilot AI — Admin Toolkit & Metrics Reference

**Last updated:** April 2026  
**Base URL:** `https://api.bizpilot-ai.com/api`  
**Authentication:** All admin endpoints require `?key=YOUR_ADMIN_KEY` query parameter.

---

## Quick Access

| Metric | URL |
|--------|-----|
| Growth overview | `/admin/metrics/overview?key=KEY` |
| Revenue & MRR | `/admin/metrics/revenue?key=KEY` |
| Churn details | `/admin/metrics/churn?key=KEY` |
| Top businesses | `/admin/metrics/top-businesses?key=KEY` |
| Message volume | `/admin/metrics/message-volume?key=KEY` |
| Failed payments | `/admin/metrics/failed-payments?key=KEY` |
| Billing overview | `/admin/billing-overview?key=KEY` |
| Billing event log | `/admin/billing-events?key=KEY` |
| Recategorize products | `POST /admin/recategorize-products?key=KEY` |
| Rollback import | `POST /import/rollback/{jobId}` |
| AI misparse rate | `/admin/telemetry/misparse-rate?key=KEY` |
| AI confidence | `/admin/telemetry/confidence-distribution?key=KEY` |
| AI retry patterns | `/admin/telemetry/retry-patterns?key=KEY` |
| AI top failures | `/admin/telemetry/top-failures?key=KEY` |
| Onboarding funnel | `/admin/onboarding-analytics?key=KEY` |

---

## Growth & Usage Metrics

### GET /admin/metrics/overview

**Purpose:** Daily check — how is the product doing?

**Parameters:**
- `key` (required) — admin key
- `days` (optional, default 30) — lookback window for signups and churn

**Returns:**
```json
{
  "period": "Last 30 days",
  "totalBusinesses": 142,
  "totalUsers": 387,
  "dailyActiveBusinesses": 45,
  "weeklyActiveBusinesses": 89,
  "monthlyActiveBusinesses": 120,
  "newSignups": 23,
  "trialConversion": {
    "started": 142,
    "converted": 67,
    "rate": "47.2%"
  },
  "recentChurnEvents": 5
}
```

**Key metrics explained:**
- **DAU/WAU/MAU:** Businesses that sent at least one WhatsApp message in the period
- **Trial conversion:** Businesses that ever moved from free to paid (SubscribedPlan != null)
- **Churn events:** Cancellations + expirations + refunds in the period

---

### GET /admin/metrics/revenue

**Purpose:** Revenue tracking, MRR estimation, provider/currency breakdown.

**Parameters:**
- `key` (required)
- `months` (optional, default 3) — lookback window

**Returns:**
```json
{
  "period": "Last 3 months",
  "estimatedMrr": 125000,
  "totalRevenue": 450000,
  "totalPayments": 67,
  "byPlan": [
    { "plan": "shop", "total": 150000, "count": 20 },
    { "plan": "pro", "total": 250000, "count": 35 },
    { "plan": "business", "total": 50000, "count": 12 }
  ],
  "byCurrency": [
    { "currency": "NGN", "total": 300000, "count": 40 },
    { "currency": "GHS", "total": 100000, "count": 15 },
    { "currency": "USD", "total": 50000, "count": 12 }
  ],
  "byProvider": [
    { "provider": "paystack", "total": 300000, "count": 40 },
    { "provider": "flutterwave", "total": 150000, "count": 27 }
  ],
  "byMonth": [
    { "month": "2026-02", "total": 120000, "count": 18 },
    { "month": "2026-03", "total": 150000, "count": 22 },
    { "month": "2026-04", "total": 180000, "count": 27 }
  ]
}
```

**MRR calculation:** Sum of payments in the last 31 days. Annual payments are divided by 12.

**Note:** Revenue is in mixed currencies. For a true MRR in a single currency, normalize manually using your pricing table.

---

### GET /admin/metrics/churn

**Purpose:** Who left and why? Identify churn patterns.

**Parameters:**
- `key` (required)
- `days` (optional, default 30)

**Returns:**
```json
{
  "period": "Last 30 days",
  "totalEvents": 8,
  "uniqueBusinesses": 6,
  "byCancelled": 3,
  "byExpired": 4,
  "byRefunded": 1,
  "details": [
    {
      "businessName": "Kofi's Shop",
      "businessId": "...",
      "eventType": "subscription.expired",
      "plan": "shop",
      "provider": "flutterwave",
      "status": "expired",
      "createdAtUtc": "2026-04-15T08:30:00Z"
    }
  ]
}
```

**Churn types:**
- **Cancelled:** User explicitly cancelled auto-renewal
- **Expired:** Manual-renewal user didn't renew before expiry + grace
- **Refunded:** Payment was disputed/refunded by provider

---

### GET /admin/metrics/top-businesses

**Purpose:** Identify power users for case studies, support priority, and feature insights.

**Parameters:**
- `key` (required)
- `days` (optional, default 30)
- `limit` (optional, default 20)

**Returns:**
```json
{
  "period": "Last 30 days",
  "byMessages": [
    { "business": { "id": "...", "name": "Ade's Supermarket", "plan": "pro", "currency": "NGN", "country": "Nigeria" }, "messageCount": 847 }
  ],
  "bySalesVolume": [
    { "business": { "id": "...", "name": "Kofi Electronics", "plan": "shop", "currency": "GHS", "country": "Ghana" }, "salesCount": 234, "salesTotal": 1500000 }
  ]
}
```

---

### GET /admin/metrics/message-volume

**Purpose:** WhatsApp usage trend. Spot outages, growth inflections, or bot issues.

**Parameters:**
- `key` (required)
- `days` (optional, default 30)

**Returns:**
```json
{
  "period": "Last 30 days",
  "totalInbound": 12450,
  "averagePerDay": 415,
  "peakDay": { "date": "2026-04-12", "count": 623 },
  "daily": [
    { "date": "2026-03-21", "count": 380 },
    { "date": "2026-03-22", "count": 412 }
  ]
}
```

**Use this to:**
- Detect outages (sudden drop to 0)
- Track growth (upward trend)
- Identify weekly patterns (quieter on weekends?)

---

### GET /admin/metrics/failed-payments

**Purpose:** Revenue at risk. Which payments failed and why?

**Parameters:**
- `key` (required)
- `days` (optional, default 30)

**Returns:**
```json
{
  "period": "Last 30 days",
  "totalFailed": 4,
  "byType": [
    { "type": "payment.failed", "count": 2 },
    { "type": "payment.rejected", "count": 1 },
    { "type": "reconciliation.past_due", "count": 1 }
  ],
  "details": [
    {
      "businessName": "Helen Store",
      "eventType": "payment.failed",
      "plan": "pro",
      "provider": "paystack",
      "amount": 12500,
      "currency": "NGN",
      "errorDetails": "Card declined",
      "createdAtUtc": "2026-04-18T14:22:00Z"
    }
  ]
}
```

**Event types:**
- **payment.failed:** Card charge attempt failed (provider retrying)
- **payment.rejected:** Amount mismatch (possible tampering)
- **reconciliation.past_due:** Auto-renew subscription expired with no renewal webhook

---

## Billing Admin

### GET /admin/billing-overview

**Purpose:** Snapshot of all active subscribers.

**Returns:** Subscriber counts by plan, provider, currency, billing cycle, auto vs manual, expiring soon, in grace, past due.

### GET /admin/billing-events

**Purpose:** Audit log of every billing action.

**Parameters:**
- `key` (required)
- `businessId` (optional) — filter to one business
- `eventType` (optional) — e.g., "payment.success", "subscription.cancelled"
- `days` (optional, default 7)
- `limit` (optional, default 50)

**Event types logged:**
- `payment.success` — successful charge (Paystack or Flutterwave)
- `payment.failed` — card charge failed
- `payment.rejected` — amount mismatch, blocked
- `payment.disputed` — chargeback opened
- `payment.refunded` — refund processed, subscription cancelled
- `subscription.created` — new Paystack subscription
- `subscription.cancelled` — user or provider cancelled
- `subscription.expired` — manual subscription expired after grace
- `reconciliation.past_due` — stale auto-renew detected by daily job
- `renewal.reminder` — WhatsApp renewal reminder sent
- `trial.reminder` — WhatsApp trial reminder sent

---

## AI Telemetry

### GET /admin/telemetry/misparse-rate

**Purpose:** Which intents is the AI struggling with?

**Returns:** Percentage of messages per intent that ended in NeedsClarification or Failed. Threshold of concern: >5% for any single intent.

### GET /admin/telemetry/confidence-distribution

**Purpose:** How confident is the AI across all messages?

**Returns:** Distribution of confidence scores in buckets (0-0.5, 0.5-0.75, 0.75-0.9, 0.9-1.0).

### GET /admin/telemetry/retry-patterns

**Purpose:** Are users having to repeat themselves?

**Returns:** Messages where the user sent the same or similar message multiple times within 5 minutes.

### GET /admin/telemetry/top-failures

**Purpose:** What specific phrasings are failing most often?

**Returns:** Top 20 messages that resulted in "unknown" intent or low confidence, grouped by similarity.

**All telemetry endpoints accept:**
- `key` (required)
- `days` (optional, default 7, max 90)

---

## Onboarding Analytics

### GET /admin/onboarding-analytics

**Purpose:** Where are users dropping off in the signup funnel?

**Returns:**
- Funnel steps with counts (menu → name → type → city → password → complete)
- Active onboarding flows (in-progress signups)
- Recent completed signups with business details

---

## Dashboard Features (User-Facing)

### Monthly P&L Card

Visible on the main dashboard overview for ALL plan tiers. Shows:
- Current month's total sales (revenue)
- Current month's total expenses
- Net profit or loss (green/red)

### Sales Tracking

Three tabs on the Sales page:
- **Active** — current sales with void and return buttons
- **Voided** — sales voided due to mistakes (stock restored)
- **Returned** — sales returned by customers (stock restored, tracked separately)

---

## Product Management

### POST /admin/recategorize-products

**Purpose:** Fix miscategorized products by re-running the auto-detection logic on existing inventory.

**Parameters:**
- `key` (required)
- `businessId` (optional) — limit to one business
- `aggressive` (optional, default false) — if true, re-categorizes ALL products including manually set ones. If false (safe mode), only touches Uncategorized / "General / Other" products.

**Usage:**

Safe mode (only fixes uncategorized products):
```
POST /admin/recategorize-products?key=KEY
```

Aggressive mode (re-categorizes everything):
```
POST /admin/recategorize-products?key=KEY&aggressive=true
```

For a specific business:
```
POST /admin/recategorize-products?key=KEY&businessId=UUID&aggressive=true
```

**Returns:**
```json
{
  "mode": "aggressive",
  "total": 200,
  "recategorized": 45,
  "unchanged": 155,
  "changes": [
    { "product": "Gold Amethyst Ring", "from": "Uncategorized", "to": "Jewelry & Accessories / Rings" },
    { "product": "Gold Diamond Bracelet", "from": "Clothing & Apparel / Underwear", "to": "Jewelry & Accessories / Bracelets" }
  ]
}
```

**When to use:**
- After deploying an updated CategoryInferrer (new categories or improved keyword matching)
- After a CSV import produced wrong categories
- To clean up a specific business's inventory

---

## CSV Import Management

### POST /import/rollback/{jobId}

**Purpose:** Undo an entire CSV import batch. Requires ManageSettings permission (Owner/Admin only).

**What it does:**
- Products created by the import: deactivated (`IsActive = false`)
- Expenses created by the import: soft-deleted
- Contacts and ledger entries: left untouched (too risky to delete)
- Job status set to "RolledBack"

**Usage:** Available via the dashboard Import page — click "Undo Import" on a completed import card. Or via API:
```
POST /import/rollback/{jobId}
```

**Returns:**
```json
{
  "success": true,
  "message": "Import rolled back. 45 records affected."
}
```

---

## Recommended Daily Checks

1. **Morning:** `/admin/metrics/overview` — check DAU and any churn
2. **Morning:** `/admin/metrics/failed-payments` — address any payment issues
3. **Weekly:** `/admin/metrics/revenue` — track MRR trend
4. **Weekly:** `/admin/telemetry/misparse-rate` — is the AI getting worse?
5. **Monthly:** `/admin/metrics/churn` — identify patterns
6. **Monthly:** `/admin/metrics/top-businesses` — know your power users

---

## Environment Variables Required

```
Admin__AnalyticsKey=your-admin-key-at-least-32-chars
```

The key is case-insensitive. Must be at least 32 characters.

---

*This document is for internal use only. Do not share the admin key or expose these endpoints publicly.*
