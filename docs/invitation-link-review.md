# Recipient invitation links

The MUI `/invitation#token=...` screen captures one bounded fragment token in memory and replaces the address with `/invitation` before requesting a preview. Query tokens and duplicate/malformed fragment tokens are rejected and scrubbed. The bearer is neither rendered nor saved in browser storage. Reloading or leaving the screen requires reopening the original email or using verified-email invitation discovery.

Review is explicit. `POST /invitations/review` requires a current authenticated, verified account matching the invitation email. The routing lookup is only a hint: the Organization transaction rechecks parent lifecycle, invitation, issuer membership/account, role-grant authority, expiry and recipient/session after locks. Production freezes the invitation row inside that transaction; preview creates no membership, Portal grant or acceptance audit. Responses contain only the bounded recipient summary.

A missing session opens the existing authentication form inside the link screen so the scrubbed proof survives in memory. Successful sign-in requires another explicit review. Acceptance uses `/me/invitations/{id}/accept`; a lost acknowledgment retries the same ID without consuming a bearer again. A 401 hides protected metadata and retains only an in-memory unconfirmed attempt; fresh sign-in enables explicit acknowledgment recovery, with recipient authorization enforced again by the server. A matching acknowledgment exposes the correct Internal or Portal destination. Unknown or mismatched responses never show success. Deadlines include response parsing and transports that ignore abort; navigation and new fragments fence old responses.

Validation: solution warnings-as-errors build, web typecheck/lint, 210-case full web suite followed by all 14 link cases after the final recovery addition, and actual desktop/mobile keyboard browser scenarios pass locally. API-host lifecycle cases cover issuer removal, invitation revocation and expiry, alongside both-surface preview/acceptance cases. Local Windows Application Control prevents API test execution; Linux CI executes these cases. The mandatory restricted PostgreSQL fixture checks no preview acceptance mutation/audit and provisions isolated ephemeral tokens outside retained bundle/diagnostic paths for the two required release browser scenarios. Production tokens remain absent from creation responses. Required exact-image CI is pending for this commit.

Invitation-driven signup has subsequently been added through verified invitation admission; see [invitation registration](invitation-registration.md) for its separate validation and pending release evidence. Remaining onboarding work includes transactional invitation mail publication and separate Worker delivery, administrator invitation history/revocation UI, Board-specific access and remaining PRD acceptance criteria. Existing verification and self-registration policy remain authoritative. No PRD or architecture ticket is complete based on this increment.

## Reviewed account continuity

Link preview accepts an optional `expectedActorId` and neutrally refuses an
empty/mismatched actor before reading the bearer proof. The link UI confirms the
current account before and after preview and natural-ID acceptance, and binds
both requests to the reviewed actor. The existing 15-second operation deadline
covers all account, command and body IO. Every continuation checks the current
controller, so late profile reads after timeout, navigation or a new fragment
cannot issue a command or restore disclosure.

Account replacement or temporary account uncertainty clears cached Organization/
Board labels and acknowledgment links. A submitted acceptance retains only its
original process-local target for explicit same-ID acknowledgment recovery, with
fresh original-account checks. Inline sign-in preserves the scrubbed proof and
original account binding; another account cannot reuse consent. No proof or
acceptance response is stored in browser storage, and no POST retries automatically.

Required desktop/mobile link scenarios retain the original lost-response check
and add actual cookie replacement before submission, original-account sign-in and
fresh review, plus cookie replacement or account-read failure after a real receipt.
They require the original acknowledgment across manual retries, withheld private
labels, correct Board/Portal destinations, and accessibility checks. These new
native scenarios remain pending exact-image CI execution.
