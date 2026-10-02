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
Organization job scope and verification policy. The API still publishes no
CARD_REMINDER jobs: configuration endpoints and Card lifecycle/date scheduling
integration are the next work, followed by MUI controls and exact-image browser
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
