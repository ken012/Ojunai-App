# Ojunai Runbook — Diagnosing Real User Issues

When a customer complains that the bot did something wrong, follow this flow. It should take 5-10 minutes from "user complaint" to "fix identified."

## 0. Get the specifics

Ask for, at minimum:
- **Phone number** of the affected user (or a unique identifier)
- **Approximate time** of the problem (within 10 minutes is fine)
- **What they typed** (verbatim if possible) and **what the bot responded with**

Without those three, you're debugging blind.

## 1. Pull the message from the database

On the Hetzner box:

```bash
ssh ojunai@46.225.108.35
psql -h localhost -U <user> -d <db>
```

Then find the relevant logs. Phone numbers in the database are stored normalized (`+234...`), so match on the last 10 digits:

```sql
SELECT
  "Id", "Direction", "ProcessingStatus", "ParsedIntent",
  "ConfidenceScore", "RawMessage", "ParsedPayloadJson", "CreatedAtUtc"
FROM "MessageLogs"
WHERE "BusinessId" = (SELECT "BusinessId" FROM "Users" WHERE "PhoneNumber" LIKE '%<last10digits>')
  AND "CreatedAtUtc" BETWEEN '2026-04-16 13:00:00' AND '2026-04-16 14:00:00'
ORDER BY "CreatedAtUtc" DESC
LIMIT 30;
```

You're looking for the user's inbound message plus the bot's outbound reply. They'll be adjacent rows with the same `BusinessId`.

## 2. Categorize the failure

Look at `ProcessingStatus` and `ConfidenceScore` on the inbound row:

| Status | ConfidenceScore | What happened |
|---|---|---|
| `Executed` | ≥ 0.90 | Parsed cleanly, handler ran. If the user got wrong output, the bug is in the handler. |
| `Executed` | 0.75–0.90 | Parsed with medium confidence, handler ran with 🤔 note. Probably correct but re-check the parse. |
| `NeedsClarification` | < 0.75 | Parse was low-confidence; bot asked for clarification. If user thought they were clear, our intent detection is weak on that phrasing. |
| `Failed` | any | Handler threw. Check `ParsedPayloadJson` + server logs. |
| `Received` (not updated) | null | Something broke between logging the message and parsing it. Check server logs for exceptions around the `CreatedAtUtc`. |

## 3. Look at the parsed payload

The `ParsedPayloadJson` field shows exactly what Claude emitted. Common issues:

**Claude parsed the wrong intent.** The `ParsedIntent` doesn't match what the user meant. Example: user said "Sold 3 rice" and intent came back as `create_expense`. Fix: strengthen the trigger examples in `ClaudeParsingService.BuildSystemPrompt` for the right intent, and add a corpus entry for this exact phrasing.

**Claude got the intent right but missed a field.** Example: intent=`create_sale`, items=[{productName: "rice"}] but no quantity. Fix: check if the user actually supplied the missing field. If yes, fix the prompt; if no, this is working correctly (bot should have asked for clarification).

**Claude emitted a derived value that's wrong.** Example: `sellHalfProduct: "shampoo"` with `quantity: 0` (Claude tried to compute half and failed). Fix: verify the handler uses the server-side derivation (the `sellHalfProduct` flag should mean the server computes, not Claude).

**Claude misread a product name.** Example: user said "face primer" but Claude emitted `productName: "primer"`. Fix: in the prompt, strengthen the product-name guidance and add a corpus test.

## 4. Check server logs for execution-level bugs

If the parse was clean but the handler failed or returned wrong output:

```bash
sudo journalctl -u ojunai-api --since "1 hour ago" --no-pager | grep -i "Intent execution failed\|error\|exception" | head -50
```

Look for exceptions with the same timestamp as the message. The `Intent execution failed for {Intent}` line tells you which handler threw.

Common handler-level bugs:
- Stock insufficient (benign — user just sees the error message)
- Plan gate blocking (if user complains they should have access, check `plan` column on Businesses)
- Nested transaction (should be fixed but watch for "already in a transaction")
- FK violation (contact/product got deleted mid-flow)

## 5. Turn the fix into a permanent test

This is the step that prevents the bug from recurring. For every real failure:

1. Add a YAML entry to `Ojunai.Tests/Corpus/conversation-corpus.yml` with the exact message and expected outcome
2. Run the corpus runner locally; the new entry should fail
3. Make the fix (prompt, handler, flag, etc.)
4. Re-run; it should pass
5. Commit the fix and the corpus entry together

If you don't add the corpus entry, the same bug will come back eventually.

## 6. Reply to the user

Something like:

> Thanks for catching that. I've reproduced the issue, fixed the root cause, and added a test to prevent it from happening again. The fix will go live in the next deploy (usually within 24 hours). No action needed on your side.

If the fix requires data repair (e.g., their sale got recorded wrong), do the data fix at the same time as the code fix and tell them explicitly what was corrected.

## Cheat sheet — commands by scenario

**"The bot didn't respond at all."**
```bash
# Did we receive the message?
psql -c "SELECT * FROM \"MessageLogs\" WHERE \"RawMessage\" ILIKE '%<snippet>%' ORDER BY \"CreatedAtUtc\" DESC LIMIT 5;"
# If no row, Twilio webhook failed — check logs for 500s around that time
sudo journalctl -u ojunai-api --since "15 minutes ago" | grep -i webhook
```

**"The bot said something weird."**
```sql
-- Find the outbound message and correlate to the inbound one
SELECT * FROM "MessageLogs" WHERE "RawMessage" ILIKE '%<bot_reply_snippet>%' ORDER BY "CreatedAtUtc" DESC LIMIT 3;
```

**"It recorded a wrong sale / expense."**
```sql
-- Find the transaction
SELECT * FROM "Sales" WHERE "BusinessId" = '<id>' ORDER BY "CreatedAtUtc" DESC LIMIT 10;
-- Void if needed (sets IsDeleted=true, restores stock via InventoryTransaction if wired)
UPDATE "Sales" SET "IsDeleted" = true, "DeletedAtUtc" = now() WHERE "Id" = '<sale-id>';
```

**"I can't log in / my account is broken."**
```sql
SELECT "Id", "IsActive", "FailedLoginAttempts", "LockoutEndsAtUtc", "MustChangePassword", "TokenVersion"
FROM "Users" WHERE "PhoneNumber" = '<phone>';
-- Clear lockout
UPDATE "Users" SET "FailedLoginAttempts" = 0, "LockoutEndsAtUtc" = NULL WHERE "PhoneNumber" = '<phone>';
```

**"My subscription is wrong."**
```sql
SELECT "Plan", "SubscribedPlan", "TrialEndsAt", "SubscriptionEndsAt", "PaystackSubscriptionCode"
FROM "Businesses" WHERE "Id" = '<id>';
-- Check Paystack webhook history
SELECT "EventId", "EventType", "ReceivedAtUtc" FROM "PaystackEventLogs"
WHERE "ReceivedAtUtc" > now() - interval '7 days' ORDER BY "ReceivedAtUtc" DESC;
```

## When to escalate vs fix yourself

**Fix yourself:** parsing misfires, handler bugs, prompt improvements, corpus additions.

**Escalate (stop, think, get a second pair of eyes):**
- Data integrity issues (rows that don't add up, receivables that don't match sales)
- Cross-tenant data leaks
- Authentication/authorization failures
- Anything involving real money (Paystack charges, refunds, subscription mismatches)
- Anything the runbook doesn't cover

## Once a week

Check the telemetry dashboard (`app.ojunai.com/admin/telemetry`) for:
- Misparse rate trending up (alert bar fills red above 5%)
- Retry patterns (users sending repeat messages after a clarification)
- New top-failure clusters (phrasings the bot can't handle)

Each of these is a lead for a new corpus entry.
