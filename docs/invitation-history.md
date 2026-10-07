# Administrator invitation history

## Account preference recovery

Open Organization/Portal and Board invitation history follows account preference
delivery and visible ten-second, focus, online and visibility recovery. Each check
uses the existing bounded account/history/account admission; Board history also
rechecks current administration and scope. It retains the selected continuation.
A queued authority invalidation takes precedence over a quiet preference check.
Signals received during a protected read coalesce into one subsequent check.

Quiet recovery preserves an open revocation dialog only while its exact invitation
and Board name remain unchanged. It does not submit revocation. Changed recipient
state, authority withdrawal and expiry still retire consent; denied history stops
the account listener, timer and recovery listeners. Unmount retires them as well.

All 75 focused invitation-history/comment/identity cases pass, including 45 history
cases. Web typechecking/lint and browser typechecking pass. Both desktop/phone
Organization invitation-history scenarios pass against the local Production API,
restricted schema-110 PostgreSQL runtime and current Vite source. A second session
changes Honolulu to Tokyo while consent is open; expiry display recovers without
manual refresh or reload, Cancel retains keyboard focus and stored history stays
identical. The existing lost-response scenario then proves exactly one DELETE and
canonical recovery. The complete invocation exits 0 with two cases in 15.3 seconds;
this timing is not a performance benchmark.

The first native attempt opened consent before the initial authority refresh
finished. The fixture now awaits the actual checked-history notice before review;
all original keyboard, privacy and lost-response assertions remain. The local API
is the immutable `6044227e` build, with backend source unchanged at this increment.
The strengthened Board native fixture requires the same other-session display,
consent and immutable-history behavior; its execution remains pending. Current
exact retained-image CI, broader accessibility and full PRD acceptance are still
required. Source and local browser results do not justify ticket closure.

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

## Complete review and revocation deadlines

Each history review now has one 15-second deadline covering its initial account
check, any Board administration read, the history response/body and final account
check. Each revocation has one separate deadline covering both account checks and
the actual DELETE. The subsequent canonical history refresh is an independently
admitted read. Abort checks before transport and after body decoding fence late
responses, including adapters that ignore cancellation.

A pre-command failure says no revocation was sent and requires fresh review.
Post-submission uncertainty preserves the exact invitation ID and withholds the
acknowledgment until canonical history confirms its state. It does not repeat
DELETE. Existing live epochs, membership/account checks, expiry withdrawal and
private-state cleanup remain in force.

All 36 focused component cases pass, including Organization and Board reads and
revocations where an eight-second first account check is followed by stalled JSON
and the whole operation aborts at 15 seconds. Late bodies cannot restore rows or
publish an acknowledgment. Web/browser type checks and lint pass. Four mandatory
desktop/phone native scenarios use real registration, invitations and committed
revocation while controlled profile transport and browser-clock advancement
verify the aggregate client deadline. Current-history recovery compares the
actual stored revoked row and requires exactly one DELETE without a document
reload; keyboard and WCAG 2.2 AA checks remain. These clock-driven client deadline
checks do not prove server latency or actual cookie expiry. Native runtime
execution against the exact release images remains pending.

## Board bootstrap and unchanged heartbeats

Board history captures the first admitted live head (or an explicit degraded
transport result) before reading Board administration and invitation history.
The bootstrap wait belongs to the same 15-second operation deadline. Review
controls cannot appear before that boundary, and a late head cannot revive a
timed-out read.

Unchanged live heartbeats still recheck administrative permission, which is
stronger than Board read access. These background checks preserve a reviewed
dialog only while the admitted Board name and exact invitation remain unchanged.
Permission loss, changed history, actual sources, transport recovery and expiry
withdraw consent. Explicit refresh or revocation cancels an in-flight background
check and performs its own fresh admission; late background responses are fenced.

All 38 history component cases pass, including initial-head withholding,
administration withdrawal on an unchanged heartbeat and an explicit revocation
that supersedes a held background response. Web/browser type checks and lint
pass. The native expiry fixtures resume the browser clock only after expiry,
privacy and mutation assertions, so Axe's timers can complete without weakening
those assertions. Demo native verification and exact-image release verification
are separate evidence scopes.

All six history expiry scenarios executed successfully against the real local
Development Demo API and Vite app at desktop/phone widths, including normal
Internal, Portal and Board issuance, keyboard review, unchanged stored history,
no revocation, expiry consent withdrawal and the full Axe scan. The companion
six recipient expiry/recovery cases also pass. These are actual Demo browser
results; the corresponding exact-image release scenarios remain pending CI.
