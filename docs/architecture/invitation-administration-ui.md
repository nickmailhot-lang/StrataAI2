# Administrator invitation creation

Current internal Owners and Admins can open `/app/{organizationId}/invite` from the member administration screen. Admission reads the current account and authorized exact-member review; no saved grant details or email are restored before current actor/Organization administration is confirmed. Direct Member, Portal-only, inactive Organization and revoked-session access is denied by the server.

The MUI form selects canonical internal or Portal roles. Internal Member is the default; only current Owners can select an internal Owner grant. Portal choices match the canonical service contract and do not create or downgrade internal membership. The server independently verifies every grant.

Before sending, the form retains a UUID and the exact normalized input in tab-scoped session storage, keyed by current account ID and Organization ID. No bearer token, password, provider secret or acknowledgment is stored. Storage failure prevents the first mutation. An unreadable retained request fails closed rather than silently replacing an unknown intent. Saved input is restored only after current authorized admission for that same scope. Confirmed sign-out and deactivation purge this namespace while preserving unrelated site data. A transient session/permission refusal clears the UI but keeps the scoped pending intent available for later freshly authorized recovery.

A missing acknowledgment, transport/body timeout, malformed success or throttling freezes email/surface/role and offers an explicit retry with the identical key and body. Reload and navigation preserve the retained intent. Duplicate pending activation is refused, request time is bounded to 15 seconds even when transport ignores abort, and route/unmount fences suppress late acknowledgments. Known validation rejection permits corrections; expired/reused keys stay blocked and reserved until existing invitations are reviewed. Full administrator invitation history/revoke UI remains required to support those reviews.

Only HTTP 201 with matching Organization, canonical input, valid invitation ID, token-free acknowledgment and explicit-offset expiry confirms creation. Expiry is shown using the current account's locale/timezone. The screen distinguishes historical creation acknowledgment from delivery and current recipient access; it does not claim email was sent, an invitation remains pending, or membership was granted. After acknowledgment, explicitly choosing “Create another invitation” clears the prior confirmed intent and starts new input with a new UUID.

Local validation includes the full web suite, typecheck/zero-warning lint, and actual desktop/mobile keyboard browser workflows. The browser tests commit creation while dropping its response, reload, retry the original key/body, verify exactly one pending invitation, accept it, create a separate Portal invitation and verify its acceptance leaves internal membership unchanged. Component cases cover storage boundaries, permission denial, key expiry/conflict, validation, throttling, malformed acknowledgments, timeout/duplicate/unmount behavior and draft cleanup on confirmed lifecycle commands. Mandatory exact-image execution remains pending for this UI commit.

This implements the creation controls; Worker invitation email delivery, invitation-driven registration with self-registration disabled, administrator history/revocation, invitation events and remaining PRD-03/60 acceptance are still outstanding. No broader issue completion is claimed.

## Complete-operation deadlines and original-request recovery

### Final confirmed expiry preferences

Both Organization and Board creation now validate and use locale/timezone from
the final account check after publication. Protected permission admission also
uses its final confirmed preferences. A timezone changed while the POST is in
flight cannot leave an acknowledged expiry formatted with pre-command settings.
The stored expiry and retained command remain unchanged; no preference update is
added to the invitation payload. Invalid final preferences withdraw display
authority through the existing account-uncertainty path and preserve the stored
original intent rather than falsely confirming creation.

Both new scope regressions fail before the fix. All 52 creation component cases
pass after it, including two original lost-response retries requiring identical
key/body and the newly confirmed Tokyo expiry display. Web typechecking/lint and
browser typechecking pass. The mandatory desktop/phone administrator native
fixture now changes the author's timezone from a separate authenticated session
after the actual first creation commits and before its reply is dropped. After
reload and original retry it requires the Tokyo caption for the actual stored
expiry and one pending invitation. Its original acceptance, metadata delivery and
Portal isolation assertions remain. Strengthened native execution remains pending
the Worker-backed release fixture; this source result does not prove full
two-client recovery of every preference consumer or current release acceptance.

Organization and Board invitation admission now share one 15-second deadline
across the initial account read, protected scope read, final account read and JSON
parsing. Creation/retry has its own single 15-second deadline across pre-command
account verification, POST and post-command account verification. Requests check
the child signal before transport and after body parsing, so an adapter that
ignores abort cannot publish a late permission result or acknowledgment.

At the deadline the page withdraws private fields and current submission authority.
The tab/account/Organization/Board-specific stored intent remains reserved. A fresh
permission check must admit the same account and scope before restoring the
original request; explicit recovery reuses its exact key and body. A prompt
transport failure retains the already reviewed locked request, while uncertain
account verification withdraws it. Neither path replaces an unknown command.

Component coverage includes aggregate admission and create deadlines at both
Organization and Board scope, late JSON suppression, and exact original recovery.
The native `invitation-creation-deadline.spec.ts` adds four desktop/mobile cases:
keyboard creation, an eight-second pre-command account delay plus a held real
committed POST response, deadline withdrawal, explicit same-key recovery, one
persisted invitation, no document reload and WCAG 2.2 AA checks. Browser-clock
advancement tests the client deadline; it does not measure server latency or
advance server cookie expiry. Exact-image native execution remains pending.

The focused invitation creation component suite passed all 48 cases after this
change, including the existing transport-loss, account replacement, live
invalidation, storage, permission, validation and exact-Board recovery cases.
