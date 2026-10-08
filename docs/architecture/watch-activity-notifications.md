# Watch activity notifications (PRD-17)

Configured Card changes now create recipient notifications from the originating
mutation transaction. The producer captures the exact post-mutation Card revision
and current List/Board. Direct Card watches follow movement; List watches use the
destination List at event time; Board watches apply across the current Board.
Creation includes current List and Board watchers. The activity matrix is Card
create/update/move/archive/restore, member add/remove, and label add/remove.
Watch/unwatch and other entities never recursively fan out.

The existing Board command gate serializes activity with watch changes and Board
access changes. After acquiring that gate, the producer selects every matching
candidate without an inbox page limit and checks active account, configured email
verification, active Organization membership, and current Board view admission.
Private Boards require an active Board grant or active Organization owner/admin
role; Organization/Public Boards admit active Organization members. Active
Organization/Board/List parents are required. Cleanup activity on an already
archived/deleted Card is excluded; a direct Card archive event can retain a private
notification that remains hidden by current inbox parent admission until restore.

Actors do not receive their own actions. Overlapping watch tuples select one user.
Assignment wins when the target also watches: one `CARD_ASSIGNED` notification is
created for that event-recipient tuple. Other watchers receive the configured
activity type. Replaying the original command returns its receipt without making
another intent. Card state, audit, event, Worker readiness job, notifications and
command receipt use the same PostgreSQL transaction; any insertion failure rolls
all of them back. Demo remains an explicitly non-durable adapter.

`CardNotification` generalizes the internal record. Migration 035 expands the
existing `card_assignment_notifications` table's allowed type values; its legacy
name, forced RLS, tenant references, event-recipient uniqueness and runtime grants
are preserved. A generated source type maps assignment to `CARD_MEMBER_ADDED` and
other types to their originating event name. A tenant/Board/event/type foreign key
rejects an activity notification attached to the wrong event type. API can insert
intents and update only `read_at`; Worker still cannot access notification rows.
Runtime readiness requires all 35 migrations, and the migration runner exercises
clean/repeated/upgraded application and failure rollback.

The inbox applies the existing fresh recipient/content admission before paging
and disclosure. It returns the stored notification type with the canonical Card
link. The MUI center uses a fixed label for each supported type, with the existing
keyboard read/selection controls, mobile layout, retry keys and recovery polling.
Unknown types and watch transition names are rejected by the browser parser.
No recipient, content, email or private payload is added to telemetry; existing
bounded mutation/inbox operation metrics remain the observation surface.

Validation added: activity store identity/replay/self-suppression cases for all
nine activity types; host movement/creation/overlap/assignment precedence and
fresh access-loss cases; storage type/event mismatch rejection; browser parser and
render cases; and an exact-image fixture that tests notification insertion rollback,
76 recipients, actor suppression, exact command replay and recipient revocation
during an observed Board lock wait. Local strict build, web typecheck/lint/build,
and 58 notification/watch tests passed before the additional render cases. Linux
host/PostgreSQL and exact-image runtime execution for this producer are pending.
The 76-recipient fixture is correctness coverage, not a production performance
benchmark. Larger-scale fan-out currently performs per-candidate eligibility checks
and writes; load/performance acceptance remains unproven.

Mentions, due reminders, recipient-private notification realtime events, later
Card activity producers and full end-to-end/performance acceptance still require
work. PRD-17 remains open; no completed ticket is claimed by this producer slice.

Native producer-to-inbox continuation: watch-notifications.spec.ts uses real authenticated issuer/recipient contexts and keyboard MUI controls to overlap Board, List and Card watches. A peer edits the Card through MUI after revision admission; desktop and 390px recipient inboxes must recover exactly one typed notification, the canonical Card link and a shared read change. The fixture then checks actor self-suppression, removal of all three watches, no later notification, an empty issuer inbox and accessibility on both recipient widths. All three notification/watch browser files collect successfully; actual native execution remains pending. A standalone TypeScript check cannot resolve the repository's absent Node declaration package for existing shared browser helpers, so collection is not claimed as static typechecking. Required recipient-private notification realtime events and fan-out/performance acceptance remain unfinished.


## Transaction-owned recipient batches

Watch activity retains its event-time Card/List/Board scope and the originating
mutation's Board gate. The producer still checks each candidate's current account,
verified-email policy, Organization membership and private Board membership before
collecting eligible recipients. Actor suppression and assignment precedence remain
in that selection. It then publishes the recipient set through
`IWorkNotificationStore.AppendCardActivitiesAsync` in the same owning transaction.

The PostgreSQL adapter shares the existing mention batch implementation: one
`INSERT ... SELECT` from the recipient array, followed by a fresh statement checking
all expected rows against the exact immutable source event. Recipients are inserted
in database UUID order to keep recipient journal lock order consistent. The
transaction, forced tenant RLS, source foreign keys, notification/journal triggers
and event-recipient uniqueness remain authoritative. An exact replay retains the
first notification ID and read timestamp; mismatched source identity is refused.
No Worker notification-row access or additional service is introduced.

Both batch methods reject empty or repeated recipient IDs, suppress the actor,
validate even an empty recipient set, and require the originating transaction.
Demo validates all conflicting identities before adding any notification or journal
entry, so a later conflicting recipient cannot leave an earlier partial insertion.
Cancellation is checked before publishing the in-memory batch.

Validation on 2026-10-07: 32 focused domain tests and 11 focused API host tests
passed. The domain batch cases exercise 76 recipients plus the suppressed actor,
reversed replay order, invalid recipients/source type, cancellation, a late identity
conflict without partial effects, and full notification/journal rollback. A local
Linux contract against PostgreSQL 17/pgvector schema 110 and the restricted API
login passed the synthetic eligible roster's batch activity source/replay,
recipient journal, self-suppression and rollback assertions, together with the
existing mention roster/history checks. Run the contract executable with
`--notification-batches-only` for this focused storage check; the default mandatory
CI executable also runs these assertions. This fixture deliberately uses synthetic
admission and does not prove authenticated HTTP or production latency acceptance.

The write-query count no longer grows with recipient count, but candidate eligibility
reads still do. Large-scale producer latency, complete immutable-image browser and
accessibility acceptance, and the full PRD definition of done remain unproven.
Estimated remaining work for PRD-17: **24% (planning estimate)**. This storage
improvement does not establish a new completion percentage or justify issue closure.


## Executed browser delivery after recipient batching

The local 21-case Checklist/watch invocation initially failed watch setup before
subscribed Board state appeared, so it did not prove notification delivery. The
fixture now establishes enabled keyboard focus before each one-time activation
and requires the actual HTTP 200 subscription acknowledgment, watching=true and
version 1 for all three scopes. The Card title lookup is scoped to the named Card
details dialog. Existing notification count/type/link, read recovery, self-action,
unwatch, canonical revision, keyboard/focus and accessibility assertions remain.

The complete three-case targeted invocation then passed in 2.3 minutes, including
both Checklist collaboration widths and the watch producer-to-inbox scenario.
The latter uses real authenticated issuer/recipient contexts, overlapping Board,
List and Card watches, a peer MUI Card edit, actual durable Worker delivery,
desktop and 390px recipient inboxes containing exactly one CARD_UPDATED
notification, the canonical link and shared read change. It also verifies actor
self-suppression, all three actual unwatch commands, no later notification and an
empty issuer inbox. Both recipient accessibility checks pass.

This uses the readonly locally compiled Production API from `5e7e1f5a`, current
production web code, restricted schema-110 PostgreSQL, Nginx/CSP and a separate
existing compiled Worker restricted to the test Organization, with optional email
verification matching the CI browser phase. It does not establish retained-current-
image acceptance, fan-out latency or every PRD scenario. All temporary owned
services were removed after terminal checks. Browser TypeScript passes; at the
last inspection, exact-commit `5e7e1f5a` web-quality and PostgreSQL CI jobs passed
and API host tests were still live in run 37730364654.
Estimated PRD-17 work remaining stays **24%** (planning estimate); the issue stays
open pending complete acceptance.


## Batched recipient eligibility and authority retention

`ICardWatchRecipientStore` now owns internal recipient selection. The producer
still validates the exact triggering Card revision and its current Card/List/Board/
Organization lifecycle under the originating Board gate, then suppresses the
actor and direct assignment target before publishing the notification batch.

PostgreSQL selects the complete eligible watched roster in one query. An EXISTS
predicate unions matching Card, current List and Board watches without duplicate
rows. Tenant and canonical Card/List/Board joins prevent scope widening. Accounts
must be active, email verified when configured, and active Organization members.
Private Boards additionally require a current Board grant or an Organization
Owner/Admin role; Organization/Public visibility still requires Organization
membership. Archived Cards remain eligible only through the producer's existing
CARD_ARCHIVED capture rule. Deleted Cards, archived/deleted Lists and inactive
Boards/Organizations cannot supply recipient scope.

The query locks eligible account and Organization membership rows FOR SHARE in
UUID order until the originating transaction ends. The existing Board gate
serializes Board-grant and watch changes. A concurrent status/email/role update
cannot invalidate selected recipient authority while publication commits. Demo
retains the same policy through its existing transaction gate and adapters;
Production resolves the new PostgreSQL implementation, checked by runtime
composition tests. Recipient IDs remain internal; there is no discovery endpoint,
new grant, schema migration, datastore, framework or deployable service.

Executed local validation on 2026-10-07: the strict full solution build passes
with zero warnings/errors; 53 selected domain, 11 watch API host and six runtime
composition cases pass. Actual Linux/restricted PostgreSQL storage checks pass for
complete overlap union, active/inactive grants and accounts, both email policies,
Organization/Public visibility, private Owner/Admin bypass of missing/removed
Board grants, canonical parent and shared-account tenant refusal, cancellation,
unwatch, and transaction rollback. Independent account and Organization-grant
updates actually hit lock timeout while selected authority is retained. These
storage transitions use synthetic admission rather than authenticated HTTP.

The complete watch producer-to-browser case also passes in 48.4 seconds against
the newly compiled readonly Production API, current production web code, actual
restricted PostgreSQL 17/pgvector/schema 110, Nginx/CSP and the existing separate
compiled Worker scoped to the fixture Organization. Optional email verification
matches the CI functional browser phase. Three canonical watch acknowledgments,
a peer MUI Card change, one typed notification in desktop/390px inboxes, shared
read state, both accessibility checks, self-suppression, actual unwatch and no
later notification remain enforced. Temporary test services are removed afterward;
original containers and data are preserved.

This supersedes the earlier per-candidate database eligibility reads described
above. SQL selection and batch write query counts no longer grow per recipient,
but result size, row locking and notification/journal inserts still do. No
large-scale producer p95 or current immutable-image acceptance is established by
these checks. Estimated PRD-17 work remaining stays **24%** (planning estimate);
full capacity/latency and release/PRD-wide acceptance are still required.


## Required large-Board producer fan-out benchmark

`test-watch-fanout-capacity.sh` is now called at the end of the existing shared
capacity fixture, after earlier consumers/copy/move/attachment checks. It retains
its own fixed-scope `artifacts/capacity/watch-fanout.json` through an always-run CI
upload step. All commands use the already authenticated issuer through the actual
API and Nginx; the 510 recipient accounts, grants and three overlapping watches
per candidate are explicitly synthetic scale setup. This does not represent 500
authenticated recipient browser sessions.

Prerequisites are the actual 200 Lists, 5,000 active Cards and 100,000 archived
Cards. Twenty serial, separately keyed HTTP Card edits must each advance the
canonical revision and produce 500 distinct eligible notifications; five
suspended and five deactivated candidates and the issuer's own watch are excluded.
Every original key/body is replayed after commit, with identical acknowledgment
and unchanged Card/notification/journal fingerprints and source/audit/job/receipt
counts. Final assertions require 20 source events/audits/jobs/receipts, 10,000
notifications, 10,000 distinct creation events and one event per notification,
plus sequence 20 for all 500 eligible recipient streams. Source type, actor,
Card/Board, version and timestamps must match the immutable triggering event.

The report contains only immutable revision, fixed topology/conditions/sizes,
verified booleans/counts and the 20 curl total-time samples plus nearest-rank p95.
It excludes account/entity IDs, bodies, cookies, keys, email and raw URLs. Exact
release images are the CI topology; a fixed local-compiled-runtime option keeps
native execution distinct. The original strict mutation p95 <500 ms budget is
preserved. A failed timing report is written before that mandatory assertion, and
stale reports are removed before a new fixture begins.

Local execution against the readonly compiled `5342630e` API, current Nginx/CSP and
actual restricted PostgreSQL 17/pgvector/schema 110 completed every correctness
assertion and all twenty samples, but **failed** the timing gate: p95 **555.548 ms**
against <500 ms. Samples range from 470.657 to 634.755 ms. The retained JSON has
status failed and topology local-compiled-runtime; sample count, p95 calculation,
complete verified effects and absence of private identifiers were checked. The
new test is not considered passing from shell syntax or authored workflow wiring.
CI's actual immutable-image execution remains required.

This exposes an unresolved producer performance gap. No budget, sample count,
recipient count, request policy or production authorization was relaxed. The
separate Worker/browser evidence above remains functional delivery evidence; this
producer timing fixture measures HTTP command acknowledgment and persisted private
journal creation, without claiming delivery to 500 browser sessions. Estimated
PRD-17 work remaining stays **24%** (planning estimate). Full release-wide evidence
and the measured capacity failure still prevent closure.

### Restricted database component profiling

Five `EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON)` samples of the actual recipient
query, executed as `strataai_api_runtime` with transaction-local tenant context,
returned exactly 501 eligible accounts (including the subsequently suppressed
issuer) in 3.755–5.720 ms execution time. Planning took 7.251–9.490 ms. This
isolated query does not explain the whole HTTP acknowledgment failure.

Five separate rollback-only inserts used the real Card's existing creation event
and the same 500 eligible synthetic recipient memberships. They exercised the
actual notification constraints, source guard and private journal triggers under
the restricted API role. Each inserted exactly 500 rows before rollback and took
201.749–219.414 ms execution time. The source guard accounted for 79.897–88.608 ms
and the journal trigger for 73.916–83.054 ms. Notification and journal counts were
verified unchanged after every profiling transaction had rolled back. No runtime
role, trigger, constraint, authorization policy or production logging setting was
changed to obtain these measurements.

These are local database component measurements of historical source publication,
not twenty HTTP edits or immutable-release acceptance. They identify repeated
source validation and journal work as concrete optimization candidates; reducing
those costs must retain historical Card attribution, tenant isolation, exact
recipient sequences, transaction rollback and concurrent deadlock prevention.
The failed HTTP benchmark and the 24% remaining-work planning estimate remain
unchanged.

### Batch source guard optimization (schema 111)

Migration `111_notification_batch_source_guard` replaces insertion-time per-row
source validation with an invoker statement trigger over PostgreSQL's inserted
transition table. One set query rejects any notification whose immutable source
does not identify its historical Card, or whose Reminder source does not identify
that Card. The existing typed source/tenant/Board foreign keys, row update guard,
private journal trigger and tenant journal serialization remain enforced.
Statement rejection rolls back the complete insert and every already-generated
recipient journal/sequence effect. API and Worker readiness now require schema
111; older schemas cannot silently retain the previous insertion behavior.

The migration-runner regression includes restricted valid batches, exact conflict
replay with an empty transition table, a late invalid same-Board Card in a mixed
batch with unchanged notification/journal/stream effects, forbidden API source
updates, privileged invalid historical updates, and valid Reminder attribution.
Clean/repeat/forward migrations, concurrent runner serialization and deliberate
failed/unrecorded migration rollback passed locally on PostgreSQL 17/pgvector.
The strict full solution build passed with zero warnings or errors.

A separate disposable database copied the existing local fixture without changing
the running database. After applying schema 111, five rollback-only 500-recipient
inserts took 132.491, 133.498, 209.200, 167.125 and 188.261 ms. The statement source
guard took 2.840–3.944 ms, compared with the earlier row guard's 79.897–88.608 ms.
All inserts produced 500 rows before rollback; retained notification/journal
counts stayed unchanged. The disposable database was removed after profiling.
These component measurements identify a reduced source-validation cost, but the
unchanged twenty-command HTTP benchmark and current release CI still must verify
end-to-end acceptance. PRD-17 remains open with 24% estimated work remaining.

### Full HTTP fan-out after schema 111

The unchanged `test-watch-fanout-capacity.sh` passed locally against the API
published from `a9d107f7`, schema 111, restricted API credentials, current Nginx/CSP
and the existing frozen web bundle. Its database was a separate disposable copy;
the three original containers and their running database were preserved. A fresh
HTTP issuer/Organization/Board/Card and the full synthetic scale fixture were
created in that copy. No Worker ran in this producer benchmark.

All twenty actual HTTP edits and original-key/body retries completed with the
required canonical revisions, exact 500-recipient deduplication, actor/inactive
suppression, 10,000 notifications, 10,000 distinct creation journal events, source
audit/job/receipt counts and unchanged replay effects. The strict p95 gate passed
at **477.964 ms**; samples ranged from 377.907 to 530.658 ms. The report explicitly
identifies `local-compiled-runtime`, source revision and all original fixture
sizes/conditions. Twenty finite samples, nearest-rank p95, fixed verified counts
and booleans, and absence of private identifiers/URLs were independently checked.
The prior 555.548 ms local failure is retained as before-change evidence.

The API publish passed, and the owned API/web containers and copied database were
removed after the benchmark's terminal success. This establishes the local
producer HTTP target under the documented serial conditions; it does not prove
500 authenticated recipient clients, Worker delivery at that scale, or current
immutable-image release acceptance. The exact `a9d107f7` CI run remains live in
.NET/API and PostgreSQL checks after web quality passed. Estimated PRD-17 work
remaining is now **22%** (planning estimate); full PRD/release acceptance still
prevents issue closure.

## Executed native integration

The [five-case watch/inbox/reminder invocation](prd-17-acceptance.md#executed-native-watch-inbox-and-reminder-recovery)
passes unchanged against local compiled Production API, separate Worker, current
MUI bundle, restricted PostgreSQL 17/pgvector and Nginx. It includes actual private
notification events and reconnect, shared reads, original-key response-loss
recovery, overlapping watch deduplication/self-suppression, unwatch, direct Card
watch movement/parent withdrawal and desktop/phone reminder recovery. See the
linked record for exact assertions and runtime/policy limits. Local compiled
evidence supersedes the earlier local-execution gap; strict email policy, due
reminder fire, full capacity/concurrency and current immutable-image acceptance
remain separate. PRD-17 stays open at **22% estimated work remaining**.

## Executed native watched-List creation and movement

The [complete native List-boundary case](prd-17-acceptance.md#executed-native-current-list-notification-boundaries)
passes its existing overlap/unwatch behavior and new current-List matrix in one
invocation. Desktop and phone inboxes receive exactly the watched creation and
move into the watched List. Unwatched creation, move out and outside edit add no
notification. The issuer inbox stays empty; recipient typed attribution,
canonical move link, automatic UI recovery and tagged Axe checks pass.
Foreground admitted-focus single activation and bounded keyboard calls repair
the earlier fixture timeout without repeating mutations or extending deadlines.
These are local compiled results with optional verification. Current immutable
images, cross-Board/concurrent transitions, capacity and complete PRD acceptance
remain. PRD-17 stays open at **20% estimated work remaining** (planning estimate).

## Executed private transport for current-List boundaries

The [private watched-List frame invocation](prd-17-acceptance.md#executed-private-live-watched-list-identities)
passes the complete existing native case with actual recipient-private snapshots
and events on desktop and phone. Canonical creation/read/watched-creation/move-in
identities match the persisted journal. The continuously subscribed phone sees
exactly four frames; desktop recovery keeps canonical identity across navigation.
Waiting for real private-feed readmission before the next command repairs an
initial observation race without changing producer behavior or effect counts.
Current immutable-image, strict-policy, cross-Board/concurrent/capacity and full
PRD acceptance remain. PRD-17 stays open at **20% estimated work remaining**.

## Executed cross-Board native watch matrix

The [cross-Board recipient case](prd-17-acceptance.md#executed-native-cross-board-watch-relationships)
passes source/destination watch relationships, source Board/List overlap,
direct Card/destination Board overlap and direct-only activity after unwatch.
Actual keyboard watch controls and real peer HTTP edits/moves yield exactly
four desktop/phone inbox notifications and matching private journal identities.
Direct Card subscription ID/version survive movement; all links recover the
final Board. Actor suppression, body-free private envelopes, Axe/overflow and
invocation cleanup pass. The recipient is an Organization owner; complete
ordinary-member/role/visibility and concurrent transitions remain separate.
Read-only Check/focus admission repairs the initial fixture activation failures
without repeating mutations. Current immutable-image, strict-policy, capacity
and complete acceptance still govern closure. PRD-17 stays open at **19%
estimated work remaining** (planning estimate).

## Executed ordinary-member moved-Card eligibility

Both [Owner and ordinary-Member cross-Board cases](prd-17-acceptance.md#executed-ordinary-member-cross-board-withdrawal)
pass in one invocation. Real invitations and explicit private-Board grants
establish the Member boundary. After four exact native/private deliveries, actual
destination grant withdrawal hides both inboxes and denies direct Card watch
reads. A later admitted peer edit produces no recipient notification/event;
complete persisted notification/journal fingerprints and four/four counts remain
unchanged. The read-only SQL comparison verifies history without seeding behavior.
This supersedes the earlier Owner-only local execution limit. Complete role and
visibility, strict policy, concurrency/capacity, current retained-image and full
acceptance remain. PRD-17 stays open at **18% estimated work remaining**.

## Executed strict verified-account native watches

The [strict-policy cross-Board matrix](prd-17-acceptance.md#executed-strict-verified-account-cross-board-watches)
passes both Owner and ordinary-Member cases together. Actual pending-account
login denial precedes disposable fixture verification; API and scoped Worker
both use strict verification. Existing four-delivery identities, movement and
overlap boundaries, direct subscription continuity, native desktop/phone
withdrawal and retained-history comparisons pass unchanged. Provider delivery
is a separate identity-mail contract. Build-once CI runs this matrix before the
optional-account full suite, reusing the loaded images. Local compiled execution
does not establish current retained-image acceptance, full role/visibility,
concurrency or capacity. PRD-17 remains open at **17% estimated work remaining**.
