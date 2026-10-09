# Board member administration

## Executed desktop and phone member recovery

Both unchanged `board-members.spec.ts` cases pass locally against Production
API, restricted schema-110 PostgreSQL, scoped separate Workers and the fresh
production web bundle behind current Nginx/CSP. Normal registered participants
join through an actual Organization invitation, then receive an explicit
private Board grant. The cases verify keyboard person-bound consent and
cancellation focus, a real competing promotion with reviewed-version conflict,
read-only recovery after lost demotion/removal responses, disappearance of the
removed profile, immediate recipient Board/edit denial and unchanged canonical
Organization membership. Overflow checks pass at 1280px and 390px.

Frozen read-only API/Worker assemblies in cached runtime images and the
ordinary unverified-account browser policy do not prove retained-current-image
release acceptance, full accessibility, concurrency/continuity matrices or
capacity. Direct administrative grants and role changes now emit the public
event names described in the [PRD-05 acceptance audit](architecture/prd-05-acceptance.md);
restricted audit/journal/outbox and replay contracts pass locally and are mandatory
in CI; retained-image consumer acceptance remains required.

## Member administration contract

The MUI screen at `/app/{organizationId}/boards/{boardId}/members` links from active
Boards with administration capability. It checks exact Organization/Board binding
and current Board administration before loading a bounded directory page. Every
row must bind to the requested Board, have a known role, positive safe membership
version and ordered unique UUID. Current Organization member profiles require a
name/email; former memberships require null profiles. Malformed rows/cursors are
rejected before any name or email is shown. Pages use the authoritative response
cursor and support previous/next navigation.

Administrators can promote/demote current Organization members and remove Board
membership. Every action opens a confirmation dialog with the Board and person,
with Cancel initially focused. Former Organization memberships expose only a
reference for identification and cleanup, and cannot be promoted from this screen.
The reviewed membership version is always sent as `If-Match` with a fresh UUID
retry key. The server's current authority, version and last-admin safeguards remain
the security boundary. Role acknowledgments require exact Board/user/role binding
and a newer safe version; removal requires 204. The screen reloads authorized
current state after acknowledgment.

Conflicts, safeguard rejection and uncertain responses clear stale private rows.
Read-only recovery does not repeat a write or infer that an absent row proves the
original operation succeeded. Access denial clears Board/profile details. Routes
abort pending work and the 15-second deadline bounds response parsing. No member
data or command intent is stored in browser storage. Confirmation closure restores
focus to the directory refresh control.

Four focused cases cover version-bound consent/role acknowledgment, foreign-row
rejection before disclosure, access loss during removal and lost-response recovery
without another write. Full web/source and exact-image browser evidence, broader
pagination/concurrency/accessibility/realtime checks and performance/telemetry
requirements remain necessary before PRD-05 can close.

`board-members.spec.ts` adds required 1280px and 390px release-browser scenarios.
Setup enrolls an existing account through Organization invitation acceptance,
then grants Board membership through the public API. Keyboard cancellation must
restore focus. A competing role change makes reviewed consent stale; the UI must
send the old `If-Match` and receive conflict. Fresh consent then uses the current
version. Successful role/removal responses are dropped and recovered by reads,
with no repeated mutation or unsupported success notice. After removal, the
recipient must lose private read/edit access while Organization membership remains
identical. Local collection passes for both cases; execution is pending CI. These
cases do not establish realtime delivery, inbox receipt or performance targets.

## Live directory invalidation

The screen subscribes through the existing Board live cursor only after an
authorized directory read. The subscription remains stable across page refreshes,
preserving event deduplication/recovery rather than resetting to cursor zero.
Events cause a fresh scoped read and cancel open consent; they never supply member
profile data. Events during a pending request coalesce into one follow-up read at
the current page position. Mutation acknowledgments reload explicitly; failed or
uncertain writes clear stale rows and retain the read-only recovery path. Transient
read failures automatically retry after ten seconds without another event.
Access denial stops the subscription and clears protected metadata. Connection
status and refresh notices are accessible text announcements.

Two additional component cases cover a role refresh with one stable subscription
and invalidation of open consent followed by private-data clearing/disposal after
revoked authority. Local full web suite passes 262 cases. Release-browser tests
now trigger their competing mutation after consent submission, since an earlier
live event correctly cancels the old review. Actual two-client/reconnect browser
evidence and broader acceptance evidence remain outstanding.

## Read recovery and release collaboration fixture

Live refreshes retain unresolved mutation warnings: a canonical read can recover
current rows without claiming that an uncertain write succeeded. Explicit refresh
or a newly reviewed command clears the old warning. Denied reads discard queued
invalidations, clear protected data and stop the subscription. Dialog closure
restores keyboard focus after a pending read completes, including when its refresh
button was disabled during the closing animation.

Additional component cases cover coalesced pending invalidations, automatic
read-only recovery without a new event, rejection of queued reads after denial,
and persistent uncertain-write warnings without another mutation. The release
fixture scopes the already-built Worker to each disposable Organization, restores
its prior configuration in cleanup, waits for actual durable readiness through the
public sync API, and observes the initial live snapshot refresh before consent.
No production limiter, authorization policy, retry assertion or image is weakened.

`board-admin-live.spec.ts` exercises separate desktop-owner and mobile-Board-admin
sessions. Both must observe acknowledged member changes; the phone socket is
closed and blocked, polling must recover a missed change, and a later update must
arrive after reconnection. An open mobile visibility confirmation must cancel on
an external visibility change without a phone write. Subsequent admin demotion
must clear protected visibility details and deny administration reads/writes.
Collection passes; exact-image execution and full PRD acceptance remain pending.
## Page recovery and bounded browser consumption

Explicit read-only recovery now retains the last requested cursor and previous-page
stack, including when moving to the next page failed before it could load. Live
refresh stays on that same selected page. Navigating back uses the prior cursor;
terminal pages disable the next action. Malformed response cursors are rejected
before Board names or member profiles are rendered. Three component cases verify
failed-page recovery, live refresh/back navigation and last-row cursor binding.

The required PostgreSQL continuity fixture exports ephemeral synthetic test
credentials and its stable 50/3 UUID pages to a mode-600 file under RUNNER_TEMP.
The file is excluded from release and failure artifacts. Missing preparation fails
release CI; the browser case skips only outside CI when the fixture is unavailable.
`board-member-directory.spec.ts` uses the actual restricted release API and tested
Worker image for desktop and phone keyboard pagination. It injects one failed Board
read after Next, checks protected rows clear, recovers the requested three-member
page without any Board mutation, compares current emails with the authorized
Organization directory, navigates back, and checks former-member profile details
remain hidden even though a different Organization membership is active. Both
cases collect locally; exact-image execution remains pending. This is bounded
paging evidence, not a large-Board latency/performance claim.

The older live-member run for 9f140eb failed both member browser cases while waiting
for `Live member updates connected.` (run 36922832577, container job 110574297300).
This matches its missing per-Organization Worker scope. The scope/readiness/bootstrap
fix is already on main in db3f477; its release execution remains in progress.
The older run must not be described as green or retried as evidence for the fix.

## Distinguishing member actions accessibly

Each member article has its profile heading as its accessible name and the visible
email (or former-member reference) and current Board role as its description.
Both review actions reference that same description, allowing keyboard/screen-reader
users to distinguish people who share a display name before opening consent.
Only already-authorized visible data supplies the descriptions; former profiles
remain unavailable. A component case checks two same-name profiles and a former
member independently. The focused 14-case member suite, lint and production build
pass locally. This does not establish complete accessibility acceptance.


## Complete strict member-removal keyboard admission

The current combined strict invocation's phone member workflow fails while
waiting for its uncertain-removal warning. The retained network trace contains
no member DELETE request; this does not establish a lost-response notice defect.
Removal opening now uses the existing enabled/focused admission helper before
one page-level Enter. The fixture observes the specifically named removal
consent dialog, then admits its own Confirm control before one further Enter.
Only focus checks may repeat; the removal command is never retried. The shared
keyboard helper, product behavior, server permission checks, original versions,
conflict/lost-response assertions and deadlines remain unchanged.

Both complete desktop/phone member workflows pass in 93.2 seconds with zero
skips, retries or flaky cases, against current compiled Production API/separate
Worker, frozen original-key recovery web and restricted schema-114
PostgreSQL17/pgvector. They retain genuine Worker updates, competing-version
conflict, lost role/removal responses, exactly two intercepted role requests and
one actual removal, current read recovery without assumed acknowledgment,
recipient access withdrawal, and unchanged Organization membership. Browser
TypeScript passes and owned containers/database were removed. This is scoped
native evidence; the original intermittent cause and combined/current immutable
acceptance remain unproven. Estimated PRD-05 work remaining stays **15%**.
