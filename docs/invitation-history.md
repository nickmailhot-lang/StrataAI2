# Administrator invitation history

## Validated pending Board head

Board history now begins its protected read after the first validated stream
head, even when the head has pending delivery or requests a reset. A bare
connecting/recovering status is insufficient: `watchBoard` invokes the new
payload-free `initialHead` callback only after `BoardLiveCursor.accept` validates
the complete page. It emits that callback once per watcher, excludes malformed
and foreign-scope heads, and keeps pending/reset/reconnect cursor behavior.
Observer failure cannot break transport recovery.

Pending delivery does not imply lost read authority. After the head, history
still performs fresh account, current Board administration, history and final
same-account checks before disclosing rows or consent controls. The callback
carries no content or permission claim and grants no command authority. Existing
explicit degraded transport admission remains; invalidations, deadline fences,
permission loss and quiet-heartbeat checks are unchanged.

The two component regressions initially fail on missing pending-head admission.
After the change, authorized history appears only after its protected reads;
failed administration performs no history read and reveals no private row.
Transport regressions prove pending-head notification, once-only behavior,
continued recovering status and exclusion of malformed/foreign heads. The final
combined 121 stream/history/creation component cases pass. Web/browser TypeScript,
targeted lint and production build pass.

Both native desktop/phone Board invitation-administration scenarios pass in one
1.2-minute invocation against frozen production web, compiled Production API,
restricted PostgreSQL and Nginx. They recover actual committed creation with the
same key/body after lost acknowledgment and reload, then recover actual
revocation without duplicate mutation. The controlled fixture intentionally has
no scoped event Worker, so the Board journal remains pending: the fixed history
view performs an authorized read instead of waiting indefinitely for delivery.
Creation readiness waits for the actual initial live permission refresh before
keyboard activation; it uses the existing qualified Board-read tracker and
unchanged five-second deadline, not an arbitrary delay or a weaker assertion.

The initial local email-enabled fixture required verified login and returned 403
before these scenarios. It was corrected to match CI's explicit unverified-email
browser phase. The matched old bundle then failed on history admission at desktop
and a keyboard/background-refresh race at phone width. No production policy,
server authorization, limiter, command body or receipt semantics changed.

This is compiled-source local evidence. Historical exact-image run 37683742977
has 43 failed browser cases, 242 passed and two skipped. This slice repairs one
reproduced admission gap; it does not establish that the other failures are fixed
or that current immutable-image CI is green. Automatic event delivery, full
PRD-05/PRD-22 acceptance and current release verification remain required.

Both desktop/phone Board account-uncertainty scenarios also pass in one separate
1.0-minute invocation against this same frozen runtime. Unconfirmed account reads
before revocation send no mutation and withdraw private review; uncertainty after
a real committed revocation preserves explicit state-check recovery instead of
inventing completion or submitting a second revocation. All four native cases
are terminal before the two disposable API/web containers are removed. Original
services, images and volumes remain intact.

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

Board history captures the first validated head, including pending/reset, or an
explicit degraded transport result before reading administration and history.
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


## Production expiry consent admission

The native history expiry fixture now observes the actual scope/actor-bound
stream head and a protected history read started after it before keyboard review.
The first displayed row can precede watcher bootstrap, whose genuine reset must
withdraw any consent. Baseline current-source Production execution completed with
five passes and one phone Portal case failing before its review dialog appeared;
a protected history refresh started after keyboard focus. The fixture waits for
that admission boundary instead of treating transient first display as readiness.

All six corrected Internal/Portal/Board desktop/phone expiry cases pass against
the frozen Production API/web, Nginx and real restricted schema-110 PostgreSQL,
with a separate discovery Worker and provider sending disabled. They retain the
original persisted expiry, paused browser clock, keyboard dialog review, expiry
withdrawal, fresh protected read, zero revocations, exactly one document request,
unchanged canonical invitation, complete Axe scan and viewport overflow checks.
The existing five-second readiness deadline and all production policies remain.
No product code or command semantics changed. Browser TypeScript and diff checks
pass. This is browser-clock withdrawal proof, not server-clock expiry, provider
sending or current retained-image release proof. Full immutable CI remains
required before issue closure.
