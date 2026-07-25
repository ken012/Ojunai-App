# Stripe billing — deploy & runbook

Stripe is the third billing provider (added 2026-07-25, commit `7852520`), routing **USD / GBP / CAD /
EUR** subscription checkouts to hosted Stripe Checkout (with optional Stripe Tax, off by default —
see step 3). CAD and EUR are now real billing
currencies (previously mapped to USD). NGN stays on Paystack; GHS/KES/ZAR/UGX stay on Flutterwave.

Provider routing (`BillingConfig.GetProvider`):

| Currency | Provider |
|---|---|
| NGN | Paystack |
| USD, GBP, CAD, EUR | **Stripe** |
| GHS, KES, ZAR, UGX | Flutterwave |

Routing governs **new** checkouts only. Existing subscriptions keep the provider stored on
`Business.BillingProvider` for cancel/renew — so legacy Flutterwave USD/GBP subs ride out on Flutterwave;
no forced migration.

## Verified (test mode, 2026-07-25)
Webhook + activation validated end-to-end (27/27) against a real DB + the Stripe.net SDK: signature
verify (bad → 401), tier/pack activation, idempotent replay, `invoice.paid` renewal (first
`subscription_create` invoice skipped), cancel, amount-tamper + currency-mismatch rejection. Checkout
Session creation validated against the live test API. The `AddStripeFields` migration applies on startup.

## Go-live steps

1. **Prod secrets** (env vars, same convention as `Paystack:SecretKey` / `Flutterwave:*` — NOT committed):
   - `Stripe:SecretKey` = live `sk_live_…`
   - `Stripe:PublishableKey` = live `pk_live_…`
   - `Stripe:WebhookSecret` = the signing secret from step 2 (`whsec_…`)
2. **Stripe Dashboard → Developers → Webhooks** → add endpoint:
   - URL: `https://api.ojunai.com/api/subscription/webhook/stripe`
   - Events: `checkout.session.completed`, `invoice.paid`, `invoice.payment_failed`,
     `customer.subscription.deleted`, `customer.subscription.updated`
   - Copy its **signing secret** into `Stripe:WebhookSecret`.
3. **Stripe Tax** — gated behind config `Stripe:AutomaticTax` (**default OFF**). To enable: Dashboard →
   **Settings → Tax** → set the head-office / origin address (pulls from Settings → Business details) +
   registrations where you're liable, THEN set env `Stripe:AutomaticTax=true`. Keep it OFF until Tax is
   configured — with it on but no address, checkout errors (*"You must have a valid head-office
   address…"*). Amount validation uses the pre-tax subtotal either way, so flipping it is safe.
   **Test/sandbox:** leave it OFF and hosted checkout works with no address setup.
4. **Deploy**: `./scripts/deploy-api.sh` (the `AddStripeFields` migration auto-applies at startup via
   `db.Database.MigrateAsync()`) then `./scripts/deploy-dashboard.sh`. Rollback: `rollback-api.sh` /
   `rollback-dashboard.sh` (the migration is additive-only — two nullable columns — so a rollback of the
   app is safe without dropping them).
5. **Post-deploy smoke test**: pick a paid plan in **Settings** with **CAD** → confirms `provider:"stripe"`
   + redirect to Stripe → complete with test card `4242 4242 4242 4242` → back to `?subscribed=true`, plan
   active, `StripeSubscriptionId` set, one `payment.success` BillingEvent. Then a **NGN** (Paystack) and a
   **GHS** (Flutterwave) checkout to confirm no routing regression.

## Local dev / testing
- Test keys live in **gitignored** `Ojunai.API/appsettings.Development.json` (`Stripe:SecretKey/PublishableKey`);
  `Stripe:WebhookSecret` is blank there — fill it from `stripe listen`.
- Forward webhooks: `stripe listen --forward-to http://localhost:5001/api/subscription/webhook/stripe`,
  paste the printed `whsec_…` into `Stripe:WebhookSecret`.
- No Stripe CLI? You can drive the webhook with hand-signed events (HMAC-SHA256 over `"<t>.<body>"` with the
  webhook secret; header `Stripe-Signature: t=<t>,v1=<hex>`). Set the event's `api_version` to the SDK's
  pinned version (`Stripe.StripeConfiguration.ApiVersion`, `2026-06-24.dahlia` for Stripe.net 52.1.1) or
  `ConstructEvent` throws on version mismatch.

## Troubleshooting — dev DB drift (`column b.<X> does not exist`)
The local dev `ojunai` DB can fall behind the EF model when migrations were applied out of band (its
`__EFMigrationsHistory` marks a migration applied but the column isn't there). Symptom: 500s on any query
that loads `Business` (webhooks, reconciliation job) with `42703: column b.<X> does not exist`.

- **Found 2026-07-25:** `Business.PricingV2Enabled` was missing locally (also crashing
  `PaymentReconciliationJobService`). Fixed with:
  `ALTER TABLE "Businesses" ADD COLUMN IF NOT EXISTS "PricingV2Enabled" boolean NOT NULL DEFAULT false;`
- **General fix:** diff the model's expected columns (from `Migrations/AppDbContextModelSnapshot.cs`,
  the `Business` entity's `b.Property<T>("…")` list) against
  `information_schema.columns WHERE table_name='Businesses'`, then `ALTER TABLE … ADD COLUMN IF NOT EXISTS`
  the missing ones (nullable, or a sensible default for NOT NULL). This is a **local dev** concern only —
  prod applies migrations cleanly on deploy.
