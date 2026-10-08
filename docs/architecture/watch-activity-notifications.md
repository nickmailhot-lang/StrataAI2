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
