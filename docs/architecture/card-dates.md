# Card dates foundation — PRD-12

`PATCH /cards/{id}/dates` accepts `startAt`, `dueAt`, `dueTimezone`,
`dueHasTime`, `dueComplete` and the current Card `version`. A UUID retry key uses
the existing command receipt store. The response contains `{ card, changed }`.
Both dates are optional. Clearing both dates clears context; completion and the
timed flag require a due date. Start may not follow due.

Explicit times must be UTC ISO instants. Calendar dates use the supplied IANA
timezone (`UTC` is also supported). A date-only start is the first valid instant
of that local day; a date-only due is the final PostgreSQL microsecond of that
day. DST therefore produces 23-, 24- or 25-hour days. Entirely skipped local
dates are rejected. Stored date-only UTC values may be roundtripped unchanged
when changing completion. Other timed instants require `dueHasTime: true`.

Migration 036 adds fields to canonical Cards, retaining forced tenant RLS and
existing identity references. Snapshot, archive, filter, assignment, label and
mutation readers retain dates; List copies copy dates into new Cards. Personal
reminders are not part of these fields and must not be copied as personal state.

The owning Organization and Board locks precede current account, membership,
Board, List and Card admission. Current Card revision is checked even for a
no-op. A canonical no-op leaves Card revision and publication untouched. A
changed date update advances the Card once, emits `CARD_DATE_CHANGED` and/or
`CARD_DUE_COMPLETED` / `CARD_DUE_REOPENED`, and publishes relevant current watcher
intents in the same transaction as audit, event readiness jobs and retry receipt.
Completion leaves lifecycle state independent. Clearing a completed due emits
the date change without inventing a reopening of an absent date. Replay requires
current active parent scope and current editing authority, preserving privacy
after archive or membership removal.

Tests cover UTC precision, calendar dates, DST, invalid timezone/order/flags,
skipped dates, no-op, completion/reopening, clearing, private replay and watcher
publication. The PostgreSQL storage fixture checks date constraints and RLS.
The exact-image fixture additionally revokes notification insertion to verify
whole-command rollback, retries the original key, and checks date retention
through unrelated edits and archive/restore. This fixture is mandatory in CI.

Local validation: strict .NET build, web typecheck/lint and 29 notification-parser
tests passed. Local .NET test execution is unavailable under Windows Application
Control; Linux CI executes the host and domain suites. Database/container checks
require CI on the exact release images. These checks are pending for this commit.

PRD-12 remains open. This dependency slice does not yet provide the MUI date
editor, viewing-timezone/Board override behavior, reminder
interval selection, scheduling/rescheduling/cancellation or delivery. Those
requirements, two-client browser evidence and performance/accessibility checks
are still required before closure. PRD-17 also remains open.

Initial Linux source CI found two fixture issues: the disposable storage role
needed SELECT plus UPDATE on the five date columns, and a 2026 Vancouver fallback
assertion was outdated. B.C. adopted permanent daylight time after March 8, 2026
(https://news.gov.bc.ca/releases/2026AG0013-000209). The suite now checks the historic
2025 25-hour day and the current 2026 24-hour November day. Production timezone
conversion already followed the runtime's current timezone database correctly.
Only the disposable role receives the additional column permissions, inside the
fixture rollback transaction. Fresh CI remains necessary.

The date storage write also invokes the existing canonical Card route trigger.
A subsequent CI pass exposed the fixture role's missing card_routes permissions.
The disposable role now has SELECT/INSERT/UPDATE for that routing table within
its rollback transaction, matching the trigger's ordinary runtime requirements.
The Linux domain and host suites passed on d595cbd; storage verification remained
failed until this additional fixture correction. Production grants are unchanged.

## Card detail display

The MUI Card detail panel now shows optional start/due values and textual/icon
urgency: UPCOMING, DUE_SOON (within 24 hours), DUE_TODAY (viewer calendar day),
OVERDUE, or COMPLETE. Completion takes precedence; actual expiry precedes the
calendar-day label. UTC comparisons retain microseconds. The bounded current
profile read supplies the configured viewing timezone and locale; the stored
timezone context is shown separately. Date-only due display omits the time.
Profile checks refresh while visible, on focus and every 30 seconds. Date scope
loss, account changes and malformed profile/date data hide dates; late profile
responses are fenced by component scope. Cards without dates add no profile read.

The date model and display have 16 passing local tests; typecheck/lint/build
passed. Two browser scenarios (1280px and 390px) are collected for exact-release
CI: separate authenticated sessions, canonical completion, configured timezone
changes and clearing dates. They explicitly refresh canonical state and do not
prove realtime notification delivery. Runtime browser evidence is pending.

Board timezone override, reminders and wider acceptance remain open.

## Card date editor

The MUI detail editor supports optional UTC start, date-only or timed UTC due,
IANA timezone context, independent completion/reopening and clearing dates.
Date-only editing uses the stored calendar context; untouched timed values
retain their PostgreSQL microsecond precision. Drafts remain tied to their base
Card revision. A newer snapshot preserves a dirty draft and disables submission
until explicit discard and fresh review. Scope/account admission is rechecked
before submission and all responses must match the expected Card, parent scope,
revision, date values and flags. Stable errors do not render provider details.

An unconfirmed save freezes its original payload, actor, Card revision and UUID
key. Canonical snapshots cannot replace this intent. Only the same save may be
retried; receipt acknowledgment then refreshes canonical state. Permission/scope
loss disables or hides editing. Recovery contributes to Board-wide action gating
while the date editor retains its own recovery action. Focus returns to an enabled
action after acknowledgement/recovery. Explicit discard starts a fresh profile
review rather than reusing a former actor.

Local validation: 36 date/editor/detail tests passed, including frozen-key lost
acknowledgment, incoming revision conflict and account/scope loss; web typecheck,
lint and build passed. The desktop/phone exact-image browser scenarios now create
dates through the actual editor, discard a real committed response, retry with
identical payload/key and check canonical two-session completion and timezone
display. These runtime scenarios remain pending in CI.

## Reminder scheduling foundation

The existing Organization queue publisher now accepts optional future availability
in the same owning transaction. Unspecified availability uses the database clock
as before; explicit availability normalizes to UTC. Duplicate publication retains
the original job and trigger. The existing claim function already gates availability.
Its mandatory PostgreSQL fixture now verifies no early claim, duplicate trigger
preservation, availability transition and ordinary lease acknowledgment.

The application policy offers at-due, 5-minute, 1-hour and 1-day intervals only
when the resulting canonical UTC trigger is in the future and the Card is active,
uncompleted and has a due date. It preserves UTC microseconds and handles skipped
intervals or minimum-date underflow. Date-only/DST deadlines use the stored expiry
instant. This policy is not recipient authorization or scheduling persistence.

Strict .NET compilation passed. Seven interval tests and the queue SQL fixture
require Linux CI execution. Personal Reminder records/API, transaction-bound
rescheduling/cancellation, generation and lease fencing, current recipient eligibility,
Worker delivery, reminder notifications and the MUI selector remain required.
No reminder is scheduled or fired by the interval policy alone.

## Personal Reminder persistence

Migration 037 adds one stable Reminder per Organization/user/Card, with tenant-safe
membership and Card identity references, forced RLS, constrained interval/status
shape, generation and revision, immutable identity columns under API grants and
monotonic timestamps. A Card's List/Board are not identity columns, so movement
can retain personal interval intent without copying it to new Cards.

Demo and PostgreSQL stores preserve identity/creation on rescheduling, no-op
unchanged canonical plans, and advance generation/revision for changed plans.
Plans are SCHEDULED for valid future triggers, SUSPENDED for missing/shortened due,
completion or inactive Card, and CANCELLED on explicit disable. Suspension retains
interval intent for later reactivation. UTC interval constraints use elapsed hours,
including the one-day interval, independent of the SQL session's DST timezone.

This is internal storage and planning. There is no network Reminder endpoint or
new job producer/Worker handler yet. A store call cannot authorize a recipient,
schedule a job, reschedule existing jobs or deliver a notification. The remaining
service must bind current scope and recipient eligibility, publish each generation
in the same transaction, and have the Worker reject superseded/cancelled/currently
ineligible attempts before any event or notification effect. No unsupported job
type is being exposed to the running Worker by this persistence change.

Strict .NET build passed. Six planning/store tests cover 76 users, exact personal
lookup, stable identity across movement, no-op/stale revisions and generation
changes. Linux CI must execute them and the extended PostgreSQL fixture, which
checks forced tenant isolation, composite identities, uniqueness, invalid interval
and generation/trigger/status constraints, cancellation and actual DST elapsed-hour
arithmetic. Clean/repeat/forward migrations and runtime readiness now require 037.
PRD-12 and PRD-17 remain open.

## Reminder job and command participant contracts

The internal generation publisher now verifies the exact persisted Reminder
identity, user/Card, revision, enabled state, interval and UTC due/trigger before
publishing into the owning Card transaction. Jobs use a stable Reminder/generation
key and exact trigger availability; queue metadata contains only reminderId and
generation. Duplicate publication retains the original committed queue entry.

The internal rescheduling participant advances every enabled personal plan for
due/completion/lifecycle changes, republishes valid new generations, and suspends
cleared, completed or inactive deadlines without losing interval intent. Start,
timezone-context, title and movement-only changes retain an already queued attempt,
even after its original trigger. Current recipient eligibility is a delivery check.

The handler contract rejects malformed metadata, unsupported job/service identity
and missing claim identifiers; obsolete generations are acknowledged without an
effect, while lost leases cannot be acknowledged as success. Its delivery adapter
must revalidate the locked live claim, canonical generation/deadline/lifecycle and
current recipient access before atomically committing notification, private event
and FIRED transition.

These are internal components, not yet registered or invoked by Card commands or
Worker composition. No new CARD_REMINDER job is published by the running API, and
no public Reminder endpoint is available yet. Database delivery and its SQL/lease/
rollback tests must be implemented before activation. Thirty-two additional domain
cases cover payload/claim boundaries, stable generation keys, inactive plans,
76-recipient rescheduling, completion/reopen/clear/archive and unrelated-edit
preservation. Strict compilation passed; Linux CI must execute the tests.

CI for 7b83643 passed web and PostgreSQL migration/storage checks but failed two
Demo store tests because the Reminder store registration was missing. Commit
0a3c962 registers both runtime adapters; its Linux test result remains pending.
PRD-12 and PRD-17 remain open.

## Restricted Worker delivery

Migration 038 implements the private delivery transaction as a SECURITY DEFINER
capability with explicit tenant, canonical reference and live claim fences.
PUBLIC execution is revoked; the Worker receives only function execution and
retains no general access to Cards, accounts, memberships, Reminder rows or
notification writes. Claims must match the persisted job type/service, actor,
Worker/lease, exact reference metadata and Reminder/generation key before scope
reads, then are locked and revalidated after current Board admission locks.

Delivery checks the current active Organization, account and verified-email
policy, Organization/private Board membership, Board/List/Card lifecycle, exact
due instant, completion, enabled generation and reached trigger. Obsolete or
currently ineligible attempts create no effect. A moved Card whose original hint
is stale requires a retry under its current Board. Fired generations replay
without creating another effect, including after another Worker recovers the job.

A successful delivery atomically allocates one Board stream sequence, creates a
ready REMINDER_FIRED event, one recipient notification and append-only audit event,
and transitions the personal row to FIRED with one revision increment. It retains
the generation and Card revision. A final lease fence raises on expiry, rolling
back every tentative effect and sequence allocation. Queue acknowledgment stays
with the existing background job processor.

Reminder events use a typed tenant-safe Reminder identity reference. Shared Board
replay projects them only as Board invalidations even if an adapter incorrectly
claims visibility; Reminder identity, actor, private type/revision and metadata
are withheld. The inbox supports a text-labeled due reminder and self reminders
only for REMINDER_FIRED; ordinary self activity remains suppressed.

The production Worker composes the handler/store using the existing explicit
Organization job scope and verification policy. Personal configuration endpoints
and Card lifecycle scheduling integration are the next work, followed by MUI controls and exact-image browser
acceptance. This is not full PRD-12/17 completion.

The mandatory PostgreSQL fixture uses a non-bypass restricted role and exercises
missing/cross-tenant scope, wrong/lost leases, forged metadata, 13 current-state
changes (including removed/missing private membership), future triggers,
notification failure rollback, lease expiry during an effect, self delivery,
duplicate effects and recovery by a different Worker. Runtime readiness and
clean/repeat/forward/failure migrations require 038. Strict build and front-end
checks plus 31 inbox tests passed locally; Linux must execute database and six
new private replay cases before their success is claimed.

CI repair: dc37639's PostgreSQL stage stopped in the existing notification storage
fixture: the auto-named CHECK selected for replacement was the read-time check,
not actor inequality. Migration 038 now locates the exact existing actor inequality
from the catalog and preserves the read-time constraint. The existing fixture's
rejection of read_at before created_at remains mandatory; new Reminder delivery
checks must still execute after it passes. The failed commit produced no accepted
release bundle or proven database delivery result.

Date commands now invoke Reminder rescheduling inside their owning transaction
after the canonical Card date update. Production publication verifies the exact
persisted personal generation and borrows the same PostgreSQL session. Changes
to due time reschedule every enabled choice; completion, a cleared due time or
an unavailable interval suspend the choice and supersede old jobs. Reopening
with a future available interval publishes a new generation. No-op and receipt
replay do not publish another job. Start/context-only edits preserve an already
queued due attempt. Demo publication keeps a validated host-local queue and never
uses production providers; it does not yet execute Reminder delivery.

The API regression seeds an existing explicit personal choice and exercises
reschedule/replay/no-op/completion/reopen/clear. The mandatory exact-image date
fixture additionally forces background-job publication to fail and checks that
Card, Reminder generation, events, jobs, notifications and receipt all roll back,
then recovers with the original key and verifies canonical future job metadata.
Strict solution build and shell syntax passed locally. Linux source/API and
exact-image execution remain required before these new checks are called passed.
Container archive/restore integration and MUI controls are still incomplete. PRD-12/17
remain open.

Personal configuration now has authenticated GET/POST/DELETE endpoints at
`/cards/{cardId}/reminders`. Every read, mutation and replay reacquires current
Organization membership, Board access and active Card/List scope; the authenticated
actor alone selects the personal row. A public viewer outside the Organization
cannot configure a choice. Writes use both Card and Reminder revisions and an
explicit currently available interval. Cancellation preserves identity and
advances the generation, while cancelling an absent choice and reapplying an
unchanged plan are no-ops. No Card revision is changed by a personal choice.

Personal changes and date rescheduling share private Reminder audit/event
publication in the command transaction. Shared Board synchronization exposes
only Board invalidation. The API regression covers personal isolation, unavailable
and invalid intervals, version conflicts, replay/no-op, stable cancellation and
re-enabling, private synchronization and revoked access. The exact-image date
fixture now creates the choice through its real personal API and checks both
date rescheduling and personal cancellation/privacy/revoked replay. It no longer
seeds a personal choice directly in PostgreSQL. These new API and container tests
must pass Linux CI before release acceptance is claimed; no ticket is closed.

Card archive, restore and deletion commands now run the same scheduling
participant after their canonical lifecycle change, inside the existing command
transaction. Archiving suspends an enabled choice and advances its generation;
restoring publishes a fresh future generation when the retained interval remains
available. Explicitly cancelled choices stay cancelled across both operations.
The API regression and exact-image fixture cover archive/restore, private receipt
admission and unchanged effects on archive replay. List/Board/Organization
container transitions, MUI configuration and full release acceptance remain open.

Personal configuration's first Linux source run (37082134090) passed Domain and
PostgreSQL checks but failed one API assertion: the new test expected 200 for
the existing Board member removal endpoint, whose contract returns 204. All
preceding personal configuration, cancellation/recovery and private-event checks
passed before that assertion. The expectation is corrected to NoContent; the
following revoked-read/replay checks and new Card lifecycle case still require
the next Linux run. The failed commit built no accepted images or release bundle.

The corrected run 37082537910 passed all 231 Domain and 220 API cases, including
revoked private Reminder replay and Card lifecycle renewal, plus web/PostgreSQL,
image build and security checks. Its exact-image container acceptance remains
pending, so this does not establish full release or browser completion.
Run 37081536276's exact-image date fixture passed its real date-command generation,
publication rollback, future job, replay/no-op and completion/reopen/clear checks.
That earlier fixture seeded a personal choice; later personal-configuration and
container-lifecycle acceptance must still run against their own exact images.

The canonical configuration route now follows PRD-12 section 13:
`POST/DELETE /cards/{cardId}/reminders`, with a personal GET on the same path.
The initial singular PUT route has been replaced; tests, telemetry and the
container fixture use the specified route/method.

List and Board archive/restore/deletion commands now also reschedule their
children inside the owning command. PostgreSQL obtains distinct chosen Card IDs
with an uncapped tenant/Board/List query; it does not reuse a UI directory page.
The planner receives actual parent eligibility independently of the Card's own
archive state. Restoring a Board leaves an archived List or Card suspended and
never re-enables an explicitly cancelled choice. The source API regression covers
mixed contexts and replay; the mandatory exact-image fixture includes 76 chosen
Cards, private cancellation, selective renewal and a forced publication failure
that must roll back the List and all Reminder changes. Local strict build and
shell syntax passed; the new source/container cases need their Linux run.
Organization lifecycle commands and MUI configuration remain outstanding.

The Card detail now includes explicit personal Reminder configuration in MUI.
It reads the current account and private choice on demand, checks both Card and
Reminder revisions, and retains the original body and idempotency key after an
uncertain response. Account changes, access revocation and live invalidations
force fresh admission; an expired interval cannot be submitted. Personal live
updates are re-read even when the Card revision has not changed. Local validation
passed 25 Reminder tests, including expiry, conflict and revoked-access recovery.

Mandatory desktop and phone keyboard scenarios now exercise two live clients,
lost acknowledgment recovery and cancellation without reloading. The date
container fixture also provisions a verified disposable recipient, configures a
future Reminder through the API, and checks actual delivery by the unchanged
release Worker image, one self notification, canonical FIRED revision and
post-delivery receipt replay. These new browser and Worker scenarios require
their own exact-image CI result. Organization lifecycle, Board timezone policy
and full PRD-12 acceptance remain outstanding; the ticket remains open.

Run 37080060407 completed with 67 browser cases passed, one intentionally skipped
identity-mail case (run separately), and six failures. Both date failures were
strict-locator ambiguity between `Card dates` and `Edit Card dates`; date display
assertions now use the exact region name. Browser traces for the other four
failures show the intended last keyboard action did not start its corresponding
request: archive retry, label removal, assignee option discovery or copy discovery.
Those actions now use locator-scoped Enter after enabled admission, removing the
separate asynchronous focus/global-keyboard gap. Existing focus, authoritative
state, live delivery and exact retry assertions are retained. The nine affected
scenarios parse and list locally; actual release execution is still required.
The same run passed Watch scenarios and its normal Board/movement performance
cases, but the overall required release gate failed and no acceptance is inferred
for the repaired scenarios.

DATE-FR-006 now has an explicit Board policy representation:
`Board.dateTimezoneOverride`, nullable with the default null. An internal Board
administrator can PATCH `/boards/{id}/date-policy` with `{timezone, version}` and
an idempotency key. Non-null values must be recognized IANA timezone IDs (or UTC);
null clears the policy. The existing Board revision, Organization/Board gates,
current administrator admission and command receipt machinery protect changes.
An effective change emits the existing BOARD_UPDATED audit/event, so subscribed
clients recover the policy through their canonical snapshot. A no-op changes no
revision. Replay returns the original acknowledgment only after current admission.

The policy overrides the viewer timezone for both displayed Card start/due dates
and derived due state. It does not change the user's profile, Card UTC values,
date-entry timezone context, or personal Reminder generations/triggers. This is
the recorded PRD-05/12 policy choice; it adds no deployable component. Migration
039 adds the tenant-owned nullable field; existing tenant RLS remains applicable.
Telemetry uses bounded operation `board_date_policy` and stable validation code
`invalid_board_date_policy`, without timezone/body values as dimensions.

Local validation passed 43 date/display/Reminder tests, strict solution build,
web typecheck/lint/production build and shell syntax. The new API regression covers
administrator-only admission, invalid zones, CAS conflict, no-op, clearing,
original receipt replay and archived-parent replay denial. The mandatory release
fixture forces an event-publication failure to prove full policy rollback and
unchanged UTC Card/Reminder state. Desktop/phone browser scenarios check applied
policy and clearing against persisted API state. Those new API/database/browser
cases require Linux exact-image results; administrator MUI configuration and
dedicated live-policy/accessibility scenarios remain outstanding. No ticket closes.

The first actual release-Worker fixture (run 37085562302) failed its existing
60-second successful-job check. The fixture queues the lifecycle events for 76
chosen Cards before the new due Reminder. The Organization Worker previously
processed only one job per Organization before a one-second sleep, imposing a
delay on each ready event in that backlog. It now drains at most 32 jobs or 250 ms
per Organization per pass before proceeding to the next Organization. Every job
retains independent claim/lease/acknowledgment and store-level retry backoff; the
existing one-second idle/pass delay remains. The fixture's 60-second check is
unchanged. Safe queue/status aggregates are captured on failure before cleanup,
without personal IDs or metadata. The throughput change and actual delivery need
the next exact-image result; no successful fire is claimed from the failed run.

Board administrators now reach the MUI timezone settings page from the Board.
The page loads fresh profile/Board admission, validates the scoped active Board
and revision, and rechecks the same actor plus current administration before
every command or retry. Unknown results hide replacement settings and navigation,
preserving the original body/key/revision. An acknowledged older receipt is
followed by a new canonical read, so it cannot overwrite a newer policy. Live
invalidation removes stale drafts and rechecks policy; conflicts and access loss
require fresh admission. Bounded reads and abort fences retire late chains.

Sixteen policy unit cases and the existing Board/date-display regressions passed
locally (48 cases together), covering loss/recovery, changed actor, revoked access,
conflict, clearing, invalid scope/zone, live drafts, stalled reads and retirement.
Desktop/phone release scenarios now exercise keyboard save/retry, another client's
policy update, live date display without reload, clearing and unchanged UTC/profile
state. Both policy and Reminder browser cases enforce axe WCAG 2.2 AA tagged
checks, using a pinned test-only dependency. The first local desktop axe check
found links directly inside the application's navigation list. MUI ListItem
wrappers correct that shared semantic structure while retaining the existing
layout. A production web build served locally with mocked admission passed zero
tagged axe violations, keyboard recovery/focus and no horizontal overflow at
1280 and 390 px. This diagnostic does not prove release API/Worker or live delivery;
the mandatory exact-image scenarios still need CI. Organization lifecycle and
remaining PRD-wide acceptance stay open.

Run 37086355617 passed Linux source, PostgreSQL, images and security, but its date
fixture failed because the new Board policy check referenced the Worker Card
before that Card was created. The fixture is corrected to run the policy check
after the actual Worker delivery/replay checks. This setup failure is separate
from run 37085562302's earlier queue-draining failure. Neither establishes successful
Worker fire or policy rollback acceptance; the corrected exact-image run is needed.

The existing Organization deletion-request command now locks all Boards with
enabled Reminder choices in stable order before changing the Organization status.
Its internal PostgreSQL candidate query is uncapped and tenant-scoped; it does
not reuse the visible Board directory. The shared planner also checks current
Organization status, and the command suspends choices/private scheduling events
inside its owning transaction after marking DELETING. Cancelled choices, Card
UTC fields/revisions and earlier notification/audit history remain intact.
The new source regression spans 52 Boards and a separate tenant; the mandatory
date fixture forces event-publication failure and verifies whole Organization,
Reminder and outbox rollback before checking successful suspension of all 76
chosen Cards. Strict local build and shell syntax pass; these new cases require
Linux CI. This extends the existing deletion request, not a claim of completed
Organization archive/restore or final deletion/retention behavior. Those lifecycle
features and remaining ticket acceptance are still open.

Keyboard-visible MUI ButtonBase focus now has a persistent two-pixel outline
using the control's current text color, with a three-pixel offset. This preserves
the inherited white focus color on the dark application bar and dark focus color
on light surfaces. The policy release scenario checks the actual computed outline
after keyboard acknowledgment recovery in addition to active-element focus and
axe checks. These checks cover an indicator that a DOM focus assertion alone
cannot establish.

Board canvas Cards now show due-state badges with text and icons and expose the
status as the Card link's accessible description, preserving its title as the
accessible name. One bounded viewer-profile read is shared across the Board;
Boards without due dates add no profile read. Effective display uses Board policy
or the signed-in account's configured timezone. Failed/invalid profile reads,
invalid policy and stale Board admission withhold status rather than guessing a
timezone. Anonymous date-status display is not claimed by this implementation.
An account switch requests fresh Board admission before showing status again.
Profile changes are checked on focus/visibility and a 30-second heartbeat.

The display clock belongs to the date context, avoiding Board/DnD parent clock
updates. It wakes at due, 24-hour and local-midnight boundaries, with a bounded
heartbeat for DST and wall-clock changes. Canonical UTC dates, completion and
Reminder scheduling remain unchanged. Unit cases cover shared reads, effective
calendar policy, status descriptions, failed/revoked/stalled reads, account
changes, retirement and idle clock boundaries. Desktop/phone release scenarios
now assert a fourth client's live canvas status under policy changes, completion
and clearing without reload and enforce tagged axe checks on that canvas.
The browser's fixed viewer clock leaves server/Worker timers real. Its setup
uses an explicitly timed UTC instant, matching server normalization; completion
submits the full canonical date payload. Exact-image browser execution and broad
Board virtualization/performance acceptance still require evidence.
Card detail uses the same boundary clock. Wake calculations retain canonical
fractional ticks when determining the first representable display millisecond;
the 24-hour boundary cannot fire early after rounding a sub-millisecond due time.

Runs 37088203149 and 37088527593 passed source/PostgreSQL/image/security gates
and exercised successful production Worker Reminder delivery within the unchanged
60-second deadline. Private GET/inbox, self-notification and original receipt
checks passed before a fixture queried the nonexistent `ready` column. The
assertion now checks the schema's `ready_at IS NOT NULL`. Those failed runs prove
that delivery slice, not the later policy/Organization rollback checks or the
complete release gate; the corrected run must finish for those claims.

Local verification for the canvas/boundary change passed 36 date/badge/display
unit cases, the focused Board live-completion/clear integration case and the
existing 25 Board regressions. Typecheck, lint, production build and release
browser scenario parsing pass. The local production preview with mocked reads
passed zero tagged axe violations, keyboard Card descriptions, policy recheck
and no page overflow at 1280 and 390 px; the phone layout was inspected visually.
This diagnostic does not prove real release API/Worker/live acceptance. The
mandatory browser scenarios and the full exact-image gate remain pending.

Corrected release run 37089366725 now reports success for the complete mandatory
date fixture: production Worker fire/readiness, private notification/replay,
Board-policy publication rollback/retry and Organization deletion-request
rollback/suspension of all 76 enabled choices. It continues through later checks;
this fixture success is scoped evidence, not a complete green release.
Canvas head 6e4f39b passed Linux source (718 web tests), PostgreSQL and source
quality gates; its image/runtime/browser checks remain pending.

A mandatory dated-Board performance scenario now measures readiness including
actual badges/profile admission, cached detail and 20 date commands using the
existing <1500/<200/p95 <500 ms budgets. It retains numeric samples in the
existing revision-bound performance artifact without personal content. See
`docs/kanban-performance.md` for fixture conditions and pending CI status.

The first exact-image dated-Board performance result (fc3360c, run 37090787467)
measured 974.45 ms usable readiness and 36.50 ms date mutation p95, both within
budget. Cached detail measured 221.42 ms and failed the unchanged 200 ms target.
The repair memoizes unchanged Card faces separately from drag availability so
opening detail does not recompute 50 dated faces, and removes the Card detail
fade for immediate cached interaction. Canonical Card/preview changes still
update the face; date-context changes reach badge consumers; focus restoration
and current admission rules remain enforced. Existing Board/date regression,
strict web checks and exact-image performance will verify the repair. This is
not a performance acceptance claim; the mandatory budgets remain unchanged.

### Open date preference recovery

Card date captions and Board canvas due badges now subscribe to the admitted
account's identity events. Bounded profile reads recover the viewing timezone
and locale without changing stored Card dates. Visible 30-second, focus, online
and visibility checks remain as fallback. Signals arriving during a read coalesce
into one follow-up; subscriptions retire on unmount, scope change, account change
or denied access. Denial stops background checks until explicit renewed admission.
Account changes require fresh parent Card/Board admission.

Board timezone policy retains precedence over the viewer's preference. Clearing
it uses the latest admitted preference. Existing due/midnight clocks, canonical
UTC dates, completion flags and Reminder scheduling remain unchanged.

On 2026-10-07, all 101 focused date/display/policy/Board source cases passed.
The earlier 40-case display/badge/identity run also passed. Both complete Card-date
browser cases passed in 1.2 minutes and both Board-policy cases in 1.5 minutes,
at 1280 and 390 pixels. Independent signed-in sessions changed the timezone;
already-open views recovered without manual reads, while whole canonical Board
responses remained identical. Existing lost-response, policy, completion, keyboard
and tagged accessibility assertions remain enforced.

The first Board-policy native run exposed a development StrictMode cleanup defect:
an aborted initial read remained pending and blocked replacement admission.
A regression failed before the fix; cleanup now retires the operation before
aborting it, and late results cannot replace the new read.

These native checks used the local Production API, restricted PostgreSQL and
Workers scoped only to newly created fixture Organizations. Both disposable
Workers were retired afterward. This is scoped local evidence, not retained
release-image acceptance or a dated-Board performance result. Full current CI,
capacity/lifecycle evidence and remaining PRD-12 acceptance still require review.

## Bounded formatter reuse and current native evidence

The normal 50-dated-Card workload previously constructed 300 timezone formatters.
Date helpers now retain at most 64 locale/timezone/purpose configurations using
LRU eviction. Classification still recomputes current time, precise instants,
completion and viewing timezone; no Card value, profile or computed result is
retained. A failing-before regression now passes along with all 82 focused date
cases, web typechecking, targeted lint and the isolated production build.

Both unchanged complete desktop/phone Card-date scenarios pass on the compiled
new web build with the Production API and restricted PostgreSQL. Persisted dates,
Board timezone policy, lost-response receipt recovery, completion, two-session
automatic preference recovery, unchanged canonical Board state and clearing dates
remain enforced. Temporary owned API/web containers were retired after execution.

The unchanged dated-Board performance case was also run on frozen before/after
builds. Both failed the original Board readiness and cached-detail budgets; date
mutation p95 passed. See the [numeric measurements and limitations](../kanban-performance.md#bounded-date-formatter-reuse).
Local functional proof and constructor reuse do not establish current immutable
release, performance/capacity, lifecycle or full PRD acceptance. PRD-12 remains
open at **25% estimated work remaining** (planning estimate).

The additional date editor/draft suites passed all 15 cases (116 total across
seven focused date/Board files). Web typechecking, lint, browser typechecking
and an isolated production web build also passed. The local build is outside
the checkout and is not a retained release artifact.

## Executed complete reminder firing and native delivery

The [complete HTTP and desktop/phone firing evidence](prd-17-acceptance.md#actual-worker-due-firing-and-native-inbox-recovery)
now passes locally. The unchanged full date fixture includes actual separate
Worker firing/readiness, original receipt recovery, UTC/DST and policy rollback,
all 76 chosen Cards, cancelled-choice preservation and Organization request
suspension/rollback. Two new mandatory native cases choose an actual near-future
reminder through MUI, recover its FIRED panel/inbox state without reload, preserve
the original scheduling acknowledgment and require one creation/read journal pair.
Keyboard, Axe and overflow checks pass at desktop and phone widths. The Worker
retains verified-email enforcement; fixture verification and local compiled
containers do not establish provider delivery or current immutable-image identity.
Complete release/capacity/concurrency acceptance remains required; estimated
PRD-12 work remaining is **24%** (planning estimate), and the ticket stays open.

## Executed two-inbox reminder live identities

The [two-inbox actual due-firing cases](prd-17-acceptance.md#executed-two-inbox-reminder-private-transport)
now independently inspect private SignalR frames at desktop and phone widths.
Both native consumers receive the same canonical creation/read identities as
the persisted journal, one real reminder and its Card link; keyboard read in
one updates both without reload. The real due clock, verified-email Worker,
FIRED revision/generation and unchanged original scheduling receipt remain
asserted. Card and both inboxes pass tagged Axe/overflow checks. A bounded
foreground menu/save activation repairs the initial missing-option fixture
wait without repeating mutations. This is local compiled evidence, not current
retained-image or provider delivery proof. Full release/concurrency/capacity
and Definition of Done acceptance remain; estimated PRD-12 work remaining stays
**24%** (planning estimate); the ticket remains open.

## Executed watched date activity integration

The [complete configured watch matrix](prd-17-acceptance.md#executed-all-thirteen-configured-watch-producers)
includes actual UTC due-date creation, completion and reopening by an admitted
Member. Each creates exactly one `CARD_DATE_CHANGED`, `CARD_DUE_COMPLETED` or
`CARD_DUE_REOPENED` notification for the other Board watcher, with matched stored
source actor/entity/revision/clock and exact inbox creation precision. Both native
desktop/phone inboxes show the distinct date captions and canonical Card links.
The issuer also watches the Board and has zero stored self-notifications. All
three original date keys/bodies replay unchanged as part of the thirteen-command
history comparison. Strict verified-email policy, separate Worker readiness,
tagged Axe/overflow and browser typechecking pass in the complete 1.0-minute case.
Personal due reminders retain their separate actual Worker due-fire acceptance.
Current retained-image/full CI and complete PRD-12 requirements remain required;
PRD-12 stays open at **24% estimated work remaining**.

## Complete persisted native reminder attribution

The [final desktop/phone producer-family checks](prd-17-acceptance.md#executed-complete-attribution-across-notification-producer-families)
compare actual fired notifications with their typed Reminder source, Card, fired
revision, actor and full significant creation clock. Both private clients receive
complete canonical creation/read envelopes with matching stored first-read clocks,
identities, versions and sequences. Actual due firing, scheduling receipt replay,
verified Worker account, keyboard reads and tagged Axe/overflow still pass. API
fixture verification is optional; current retained-image and complete acceptance
remain required. PRD-12 remains open at **24% estimated work remaining**.
