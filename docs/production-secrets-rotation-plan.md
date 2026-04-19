# BizPilot AI — Production Secrets Rotation Plan

**Date:** April 2026  
**Status:** Pre-production  
**Purpose:** Guide for rotating all test/development secrets to production-ready values before go-live.

---

## Overview

BizPilot AI uses environment variables for all secrets in production. The `appsettings.json` file contains empty placeholders. Development secrets in `appsettings.Development.json` are gitignored and never committed.

Before go-live, all test keys must be replaced with live/production keys from each provider.

---

## Step 1: Generate New Secrets

| Secret | Source | Action |
|--------|--------|--------|
| `Jwt:Secret` | Self-generated | Run `openssl rand -base64 64` to create a new 64-byte signing key |
| `Claude:ApiKey` | Anthropic Console | Create a new production API key at console.anthropic.com |
| `Twilio:AccountSid` | Twilio Console | Same SID (does not rotate). Rotate the AuthToken instead. |
| `Twilio:AuthToken` | Twilio Console | Settings > Rotate Auth Token |
| `Paystack:SecretKey` | Paystack Dashboard | Settings > API Keys > Copy live secret key (`sk_live_...`) |
| `Flutterwave:PublicKey` | Flutterwave Dashboard | API Keys > Copy live public key (`FLWPUBK-...`) |
| `Flutterwave:SecretKey` | Flutterwave Dashboard | API Keys > Copy live secret key (`FLWSECK-...`) |
| `Flutterwave:EncryptionKey` | Flutterwave Dashboard | API Keys > Copy encryption key |
| `Flutterwave:ClientId` | Flutterwave Dashboard | OAuth credentials (live) |
| `Flutterwave:ClientSecret` | Flutterwave Dashboard | OAuth credentials (live) |
| `Flutterwave:WebhookSecret` | Self-generated | Run `openssl rand -hex 32` and set in Flutterwave webhook settings |
| Database password | PostgreSQL | `ALTER USER bizpilot_user WITH PASSWORD 'new-strong-password';` |

---

## Step 2: Set Environment Variables on Production Server

Edit your environment file:

```bash
sudo nano /etc/bizpilot/api.env
```

Set all secrets using double-underscore notation for nested keys:

```env
# Database
ConnectionStrings__DefaultConnection=Host=your-db-host;Port=5432;Database=bizpilot;Username=bizpilot_user;Password=NEW_PASSWORD

# JWT
Jwt__Secret=NEW_64_BYTE_BASE64_KEY
Jwt__Issuer=bizpilot-api
Jwt__Audience=bizpilot-dashboard
Jwt__ExpiryHours=24

# Claude AI
Claude__ApiKey=sk-ant-NEW_KEY
Claude__Model=claude-sonnet-4-6
Claude__MaxTokens=1024

# Twilio (WhatsApp)
Twilio__AccountSid=AC_YOUR_SID
Twilio__AuthToken=NEW_AUTH_TOKEN
Twilio__WhatsAppFrom=whatsapp:+YOUR_PRODUCTION_NUMBER

# Paystack (NGN payments)
Paystack__SecretKey=sk_live_YOUR_KEY

# Flutterwave (non-NGN payments)
Flutterwave__PublicKey=FLWPUBK-YOUR_LIVE_KEY
Flutterwave__SecretKey=FLWSECK-YOUR_LIVE_KEY
Flutterwave__EncryptionKey=YOUR_LIVE_ENCRYPTION_KEY
Flutterwave__ClientId=YOUR_LIVE_CLIENT_ID
Flutterwave__ClientSecret=YOUR_LIVE_CLIENT_SECRET
Flutterwave__ApiBaseUrl=https://api.flutterwave.com
Flutterwave__WebhookSecret=NEW_HEX_HASH
Flutterwave__CallbackUrl=https://app.bizpilot-ai.com/settings

# CORS
Cors__AllowedOrigins=https://app.bizpilot-ai.com
```

---

## Step 3: Update Webhook URLs in Provider Dashboards

| Provider | Dashboard Location | Webhook URL |
|----------|--------------------|-------------|
| Paystack | Settings > Webhooks | `https://api.bizpilot-ai.com/api/subscription/webhook` |
| Flutterwave | Settings > Webhooks | `https://api.bizpilot-ai.com/api/subscription/webhook/flutterwave` |
| Twilio | Phone Numbers > WhatsApp | `https://api.bizpilot-ai.com/api/webhooks/whatsapp` |

For Flutterwave, also set the **Secret Hash** to the same value as `Flutterwave__WebhookSecret`.

---

## Step 4: Deploy and Verify

```bash
# 1. Restart the API server
sudo systemctl restart bizpilot-api

# 2. Check health
curl https://api.bizpilot-ai.com/health
# Expected: {"status":"ok","database":"connected"}

# 3. Check pricing endpoint (public, no auth)
curl https://api.bizpilot-ai.com/api/subscription/pricing
# Expected: JSON with plan pricing

# 4. Test WhatsApp
# Send "hello" to your production WhatsApp number
# Expected: Greeting message with AI disclaimer

# 5. Test Paystack (NGN)
# On dashboard, select NGN currency, click Subscribe
# Expected: Redirect to Paystack checkout

# 6. Test Flutterwave (non-NGN)
# On dashboard, select GHS currency, click Subscribe
# Expected: Payment method picker, then Flutterwave modal
```

---

## Step 5: Revoke Old Test Keys

After confirming production works:

| Provider | Action |
|----------|--------|
| Anthropic | Console > API Keys > Delete old key |
| Twilio | Auth token already rotated (old one invalid) |
| Paystack | Deactivate test keys if separate account |
| Flutterwave | Dashboard > Deactivate sandbox keys |

---

## Security Notes

- **Never commit secrets to git.** `appsettings.Development.json` is gitignored.
- **Production secrets live only in environment variables** on the server.
- **Startup validation** will fail fast if any required key is missing.
- **Rotate the JWT secret** to invalidate all existing tokens. Users will need to re-login.
- **Database password rotation** requires updating the connection string on the server.

---

## Emergency: If Secrets Are Compromised

1. Immediately rotate the compromised key at the provider
2. Update the environment variable on the server
3. Restart the API: `sudo systemctl restart bizpilot-api`
4. If JWT secret was compromised: all users are force-logged-out (tokens invalid)
5. If payment key was compromised: check provider dashboard for unauthorized transactions
6. Monitor logs for 24 hours after rotation

---

*This document should be stored securely and not committed to version control.*
