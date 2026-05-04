# Ojunai Design Principles

Six rules every code change in this repo is reviewed against. Each one exists because a specific class of bug appeared more than once. These aren't guidelines — they're invariants.

## 1. Claude parses intent and names entities. The server computes every derived value.

Claude's job is to turn natural language into a structured intent plus the entities the user named (product, quantity, customer, amount). Any value that can be **derived** from authoritative data — database state, arithmetic on user-supplied numbers, aggregations over rows — must be computed by server code, not inferred by Claude.

**Why.** LLMs are not deterministic. When asked to compute "half of 3 bottles of shampoo", Claude might return 1.5, or 1, or 0 (observed). The database knows exactly how many bottles exist right now. Claude does not. Never ask Claude a question whose answer is in the database.

**In practice.** If a user says "sell half my rice", Claude emits `sellHalfProduct: "rice"`. The server calls `FindProductAsync`, reads `CurrentStock`, and computes the quantity itself. Same for `sellAllProduct`, `clearAllDebts`, percentage discounts, reverse-calculated quantities from `totalAmount`, and any future "compute X from Y" pattern. Each one gets a named flag, and the server owns the math.

**Red flag in code review.** A handler consuming `GetDecimalOrNull("quantity")` when the quantity should have been derived from stock. A handler trusting `GetDecimalOrNull("totalAmount")` when it should have been summed from items. A handler that takes Claude's word for a date range instead of interpreting relative terms ("yesterday", "this week") server-side in the Africa/Lagos timezone.

## 2. Every user-visible side effect has an idempotency key or an explicit dedup record.

A side effect is anything the outside world observes: a WhatsApp message sent to a user, an alert fired, a database row created, a Paystack charge, a Hangfire job triggered. Every one of these must be safe to run twice. Either the operation is naturally idempotent, or there's a piece of state (DB flag, in-memory set, unique constraint) that prevents duplication.

**Why.** At scale, the same logical action can reach the handler multiple times for legitimate reasons: Twilio retries a webhook, Hangfire retries a failed job, a user sends the same command twice by accident, a background job runs on two servers. Without idempotency, users see the same alert fifteen times and don't trust the system.

**In practice.** Twilio message delivery is deduped by `WhatsAppMessageId`. Paystack webhooks are deduped by `event_id` in `PaystackEventLog`. Big-sale alerts are deduped in-memory by sale ID. Auto-created contacts use a `FindOrCreateContactAsync` helper. When adding any new side effect, the first question in PR review is: "what's the idempotency key?"

**Red flag in code review.** A new `await _whatsApp.SendMessageAsync(...)` without an upstream dedup check. A new background job that isn't safe to re-run. A new alert that fires based on "was there a recent X" without tracking which X has already been alerted.

## 3. Every boundary validates what crosses it. Raw exceptions never reach users.

A boundary is any place where data flows between layers: Claude's response into a handler, one service calling another, a controller returning to the frontend, a handler sending text to WhatsApp. Every boundary is a contract. Data crossing it is validated; errors crossing it are normalized.

**Why.** The "The connection is already in a transaction" error that surfaced verbatim to a customer was a boundary failure — an internal driver error bled into user-facing text. Boundaries aren't theoretical; every one of them is a place where an unchecked assumption becomes a user-visible bug.

**In practice.** Every `try/catch` that handles an exception destined for a user runs it through `FriendlyErrorMessage(ex)` first — business-logic exceptions (`InvalidOperationException`, `KeyNotFoundException`, `ArgumentException`) are surfaced; everything else becomes "something went wrong on our end". Every handler that reads Claude output uses `GetStringOrNull` / `GetDecimalOrNull` and validates presence and range before using values. Every controller uses the global exception handler in `Program.cs` to map exception types to status codes.

**Red flag in code review.** A `$"{ex.Message}"` anywhere that feeds into `SendMessageAsync` or `ApiResponse.Fail`. A handler that reads a Claude field without a null check. A service method that returns raw DB exceptions without translation.

## 4. Exactly one layer owns each atomic transaction boundary.

When two layers both call `BeginTransactionAsync` on the same `DbContext`, Npgsql rejects the nested call. Atomicity is a contract: if a service method opens a transaction internally, its callers must not open one around it. If callers need to compose multiple service operations atomically, either pass transaction context through or use a compensating-action pattern.

**Why.** We found this bug three times in one session — sale creation, stock-hold conversion, and almost a third time in the GDPR closure flow. It's not a one-off; it's an architectural gap that reappears whenever two layers independently decide they should own atomicity.

**In practice.** `SalesService.CreateAsync` owns its transaction — callers (WhatsApp handler, controller, Hangfire jobs) must not open one around it. `StockHoldService.ConvertToSaleAsync` can't nest because it calls `SalesService`, so it uses compensating actions: claim the hold, attempt the sale, if sale fails un-claim the hold. New service methods document in their XML doc whether they own a transaction.

**Red flag in code review.** An `await using var tx = await _db.Database.BeginTransactionAsync()` block that calls `_sales.CreateAsync`, `_holds.ConvertToSaleAsync`, or any service method whose doc says it owns a transaction. A new service method that opens a transaction without documenting the ownership contract.

## 5. Every new intent flag has at least 10 test cases before it ships.

When we add a new Claude flag (`sellHalfProduct`, `clearAllDebts`, `discountPercent`), we need realistic test coverage across phrasing variations, Pidgin, typos, follow-up patterns, and edge cases (empty stock, missing price, multiple products). The test corpus at `Ojunai.Tests/Corpus/conversation-corpus.yml` is where these live.

**Why.** Users don't write the canonical phrasing. They write "Sell half my shampoo", "half of the shampoo", "sell halfff my shampoo", "I wan sell half". If the new flag only works for the single phrasing the developer tested manually, half of production will misparse.

**In practice.** Every PR that adds a new intent flag includes corpus entries. Every PR that fixes a parsing bug adds a corpus entry for that specific failure so it can't regress. Target coverage: 10+ variations per flag, including at least one Pidgin form and one obvious typo.

**Red flag in code review.** A new flag handled in the system prompt or server code without a matching corpus entry.

## 6. Every production deploy runs against the conversation test corpus.

The corpus-runner is the regression net. Nothing ships without it passing. A failing corpus entry either gets fixed or gets documented as an intentional change with the test updated to match.

**Why.** Ken is not the regression test. The corpus is.

**In practice.** Currently run on demand (not deploy-gated yet — added when product maturity warrants). Once gated, deploy scripts will abort on corpus failure. Each ignored/skipped test is logged with reason so we know what we're knowingly accepting as broken.

**Red flag in code review.** Skipping a corpus test without a linked issue or justification.

---

## Review checklist (copy into every PR description)

- [ ] Any derived value read from Claude? If yes, is it computed server-side instead?
- [ ] Any new side effect? What's the idempotency key?
- [ ] Any new boundary? Is input validated and are exceptions filtered?
- [ ] Any new `BeginTransactionAsync`? Does it overlap with service-owned transactions?
- [ ] Any new intent flag? Corpus entries added?
- [ ] Corpus run locally and passing before merge?
