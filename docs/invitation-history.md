# Administrator invitation history

## Expiry while reviewing

Organization, Portal and Board invitation history schedules a bounded timer for
the nearest pending invitation's persisted expiry. At that time it withdraws
private rows and any open revocation consent, fences stale reads, and rereads
protected history with the current account and scope checks. Already accepted,
revoked or expired rows do not schedule another expiry refresh. Long delays are
chunked within the browser timer limit. Revocation also checks expiry after
account admission, so a delayed timer cannot submit expired consent. Expiry
never sends a mutation or implies acceptance, revocation, mail delivery or access.
An unresolved original revocation remains recoverable through canonical history.

Component checks cover all three invitation surfaces and a confirmation racing
the timer. `invitation-history-expiry.spec.ts` adds desktop/mobile scenarios using
normal API creation and the real persisted expiry, with only the browser clock
advanced. They require consent withdrawal, a protected reread, unchanged server
history, no write or document reload, and accessibility checks. Actual execution
remains pending exact-image CI; advancing browser time does not prove server
expiry policy or mail delivery.

## Temporary account uncertainty

An unavailable, malformed or interrupted account check before revocation clears
private history and consent without inventing an unknown mutation: the page
states that no revocation was sent and requires a fresh protected history read
before another review. If the DELETE was submitted, an unavailable subsequent
account check instead retains the original invitation ID in memory for read-only
canonical recovery. A 204 response alone is withheld until the original account
is reconfirmed. Failed history refreshes also withdraw previous success notices.
Neither path resubmits a DELETE automatically.

Component coverage exercises both Organization and Board histories before and
after submission, stale success withdrawal during refresh, and noncooperating
account responses arriving after the request deadline. Late responses cannot
send a previously unsent command or publish an unverified receipt. The required
`invitation-history-account.spec.ts` adds desktop/mobile normal API scenarios
with a one-time unavailable profile response, unchanged history before an
unsent command, actual committed revocation afterward, and read-only recovery
with exactly one DELETE, no document reload and accessibility checks. Native
execution remains pending exact-image CI.

`GET /organizations/{organizationId}/invitations?after={uuid}` returns at most
50 issued invitations with an optional next cursor. Only a currently active
Organization Owner/Admin may read it. The command rechecks the actor after
parent/membership lock waits and after the read, using the existing account and
session authorization boundary. Foreign, ordinary member and portal accounts
receive the same safe Organization-not-found response. Migration 024 adds the
tenant/cursor index and required schema readiness advances through 024.

Each row exposes ID, recipient email, surface, target role, creation/expiry and
accepted/revoked timestamps. It never exposes bearer/hash, signing keys, provider
receipt/account, job metadata, or provider error text. Delivery state is separate
from invitation lifecycle: PENDING, SENT, CANCELLED, FAILED, or RETRY_EXHAUSTED
when the generic job exhausted retries while its ledger is still pending. A
null delivery state means no mail intent was recorded (including Demo and
feature-disabled creation), not a delivered message. SENT means the configured
provider acknowledged the message; it does not prove inbox receipt or grant
access. A SENT invitation can subsequently be revoked or expire.

The endpoint is read-only and paginates all lifecycle states, so historical
revoked/accepted invitations remain reviewable. The MUI history at
`/app/{organizationId}/invitations` links from membership and invitation creation.
It validates bounded ordered cursor pages, shows lifecycle separately from mail
state, formats timestamps using account preferences, and requires an explicit
keyboard-accessible revocation confirmation. Lost/ambiguous revocation responses
hide stale rows and recover through read-only current history before another
action. Only a 204 acknowledgment or the exact invitation's canonical revoked
timestamp confirms revocation; absent rows are not assumed revoked. Permission
loss clears protected data. Navigation/unmount aborts outstanding requests, and
the deadline bounds response body parsing too. Pending recovery is memory-only;
the page stores no recipient data or proof in browser storage.

No issue is complete on the strength of this increment. Host tests cover paging, lifecycle,
secret exclusion and current/removed administrative access; exact-image mail
tests check actual SENT reads and denied recipient access. Local web checks include
220 passing tests and desktop/mobile browser keyboard cancellation and lost
revocation acknowledgment recovery. Linux source evidence for the preceding
backend commit confirms 114 domain and 138 API cases; full release evidence for
the UI commit remains required. Board-specific invitations and other onboarding
acceptance criteria remain open.

## Board administration

The Board history screen at `/app/{organizationId}/boards/{boardId}/invitations`
uses the scoped Board history and revocation endpoints. It checks the current
Board administration capability and exact Organization/Board identity before
loading rows, then validates every row as an Internal Organization Member
invitation bound to that Board with Admin or Member access. The Board name is
shown only after the complete page has passed validation. Ordinary Organization
history rejects Board targets.

The confirmation dialog identifies the Board, recipient and intended role.
Current access denial clears protected metadata. An uncertain revocation clears
stale rows and can recover from canonical history without repeating the write;
a missing or accepted invitation does not imply successful revocation. Sender
and Board pages link to this history. Component coverage includes malformed
bindings, admission denial, revoked authority and lost acknowledgment recovery.
Desktop/mobile Board sender browser evidence and full exact-image CI remain
required before the corresponding issues can close.

`board-invitation-administration.spec.ts` adds required release-browser scenarios
at 1280px and 390px. They create their own accounts and Board through the public
API, exercise Admin and Member issuance with keyboard controls, drop a successful
creation acknowledgment and recover the same UUID/key/body after reload, then
cancel and revoke through Board history. The successful revocation response is
also dropped: a read-only refresh must confirm the canonical revoked timestamp
with exactly one DELETE. The scenarios verify one bound invitation and no
horizontal overflow. Local collection passes for both cases; execution is pending
the exact-image container CI job. No local collection result proves browser
acceptance or actual email delivery.

The required invitation-registration release fixture now holds the real Board
row lock while history and revocation requests wait. It commits an archive before
releasing the lock, requires the stable `board_not_found` denial with no recipient
email, and compares invitation/audit/event/stream/job state before and after each
request. This supplements rollback and natural-ID retry assertions. Shell syntax
and diff checks pass locally; restricted PostgreSQL execution remains pending CI.

## Live Organization history and reviewed account

Organization history subscribes to the production metadata stream after its
account-bound history read succeeds. An event, reset or unavailable stream clears
private rows and retires revocation consent, then queues a fresh first-page read.
An epoch fences obsolete reads. A currently running revocation keeps its exact
target; live invalidation never sends another DELETE or erases an unresolved
natural-ID recovery reference. A missing recovery row leaves revocation unconfirmed
and blocks new consent until canonical review resolves it.

History reads check `/me` before and after disclosure. Revocation checks the
reviewed account before sending and after acknowledgment. Both Organization and
Board DELETE endpoints accept `expectedActorId`; a mismatch or empty UUID returns
the neutral `session_unavailable` refusal before the command can change state.
Confirmed account replacement withdraws private rows and recovery context.

The required desktop/mobile member-live browser scenario now includes a separate
Organization invitation-history observer. It must show newly issued invitations
from canonical live sources without manual reload and pass accessibility checks.
This native execution remains pending. Board history retains its separate Board
surface; applicable Board and recipient stream integration and additional
invitation lifecycle event coverage remain outstanding.

Local validation passed the Release solution build with zero warnings/errors,
20 history-component cases and two API-host cases covering reviewed-account
refusal and successful same-account revocation on both surfaces. Native container
fixtures also require neutral reviewed-account refusal with unchanged invitation,
routing, audit, source/counter/proof and queue state. Their runtime execution and
the new two-client browser observer still await current-image CI.
