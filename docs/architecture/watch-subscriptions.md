# Personal watch subscriptions (PRD-17)

`GET /watch/{entityType}/{entityId}` returns the authenticated user's state for
`CARD`, `LIST` or `BOARD`. It accepts no recipient override. The caller must be
an active internal Organization member with current view access to the active
Board and active target. Card admission also checks its current active List.
An archived Organization permits this read but freezes mutations. Missing,
inaccessible, archived/deleted targets and unknown types share `watch_not_found`.

`PUT` watches and `DELETE` unwatches, with `?version=N` and an optional UUID
`Idempotency-Key`. Revision zero represents no existing subscription. A matching
revision with the requested state already present is a no-op; it creates no
row/event/job/audit and does not increment a revision. A stale revision returns
`version_conflict`. Invalid revisions are rejected only after entity admission.
Successful state changes return the subscription ID, current revision and dates,
authenticated user and canonical entity/Organization/Board scope.
Reads also return authoritative `canChange`, which is false for an archived
Organization. The UI uses it to disable personal changes while showing the
retained subscription state.

Unwatch retains the original row and creation time, toggles `watching`, and
increments its revision. Rewatch restores that same identity. Unique
`(tenant,user,type,entity)` tuples prevent duplicate subscriptions. Typed nullable
references and tenant composite foreign keys reject type/entity mismatches and
cross-Organization references. Direct Card subscriptions bind to its identity,
not its current List; movement within the Organization does not delete them.
Lifecycle or membership changes hide a subscription until current access is
restored. Retention does not imply eligibility to receive or display activity.

The service resolves an admission hint, enters one tenant command transaction,
locks the Organization/member and Board gate before actor/session locks, then
re-resolves the target after any wait. A concurrent movement that changes this
admission scope rejects the in-flight operation; a fresh read uses the new scope.
Every keyed receipt replay reauthorizes the current target and actor and checks
the receipt's user/type/entity/Organization/Board scope. Cached acknowledgment
state can precede a later watch/unwatch, so clients must reconcile through a
fresh GET rather than treating a receipt as the latest canonical state.

One state change, its append-only audit, `WATCH_CREATED`/`WATCH_REMOVED` event,
existing durable Worker delivery job and retry receipt commit together.
The event references the subscription's ID/revision and carries no private
entity title, watch configuration or content. Existing Board replay retains
its conservative handling of unknown/private entity references; this change
does not expose a subscription directory to other Board participants.
Fixed native telemetry operations are `watch_read`, `watch_create`, and
`watch_remove`, with stable error codes and no identifiers as metric labels.

Migration 033 enables and forces tenant RLS, defines typed reference integrity
and indexes current watcher tuples. Migration 034 admits the explicit
`WatchSubscription` work-event type, restricts its event names and requires a
tenant-bound subscription reference. Runtime startup now requires all 35 migrations,
including the [watch activity notification producer](watch-activity-notifications.md).
The API receives SELECT/INSERT and only UPDATE of watching/update time/revision;
the Worker receives no watch-table access. Demo retains host-lifetime rows and
uses the existing serialized in-memory command boundary; it does not claim
PostgreSQL durability or transactional rollback behavior.

Three real host scenarios cover all types, personal state, versions, no-op and
keyed replay, changed-intent rejection, watch/unwatch/rewatch history, events,
access loss/restoration, anonymous/outsider rejection, active parent admission,
and Card movement/current List archive/restore. PostgreSQL storage/role/migration
fixtures cover RLS, typed references, unique tuples, revisions and timestamp
constraints. The exact-image fixture uses actual release web/API routes and
checks audit/event/job/column-grant failure rollback with original-key retries,
movement retention, archived Organization reads/frozen writes, and observed Board
waits during membership and session revocation. Local strict build and Bash
syntax checks passed; Linux host/storage/exact-image runtime evidence is pending.

## Watch controls

The Board header, unfiltered List columns and Card detail provide named MUI
watch controls. Personal state loads on demand in a keyboard-accessible dialog,
then refreshes every ten seconds while visible and on focus. Writes first check
the current account and submit the displayed personal revision with a stable UUID
retry key. An uncertain acknowledgment retains that recipient/revision/method/key
even after newer canonical state is loaded; the dialog offers exact-intent retry
and prevents accidental dismissal. Confirmed acknowledgments reconcile through
fresh GET. Known conflicts require a fresh check. Permission, account and entity
scope changes retire private state and fence late responses. An archived
Organization read retires unresolved changes and disables mutation. Focus returns
to a stable dialog action after refresh/retry and to the source control (or the
parent fallback) after closing. List controls are available on the unfiltered
Board so a changing filter result cannot erase a List's active dialog intent.

Twenty-one component cases cover all typed controls, key/revision binding, lost
responses with newer canonical state, unwatch, malformed scope/acknowledgment,
account/permission loss, read-only Organization state, conflicts, periodic
recovery and keyboard focus. The existing 25 Board screen tests pass with the
controls. A real release browser scenario adds desktop/390px phone watch/unwatch
and automatic cross-client recovery for all three types, a committed Card watch
with a lost response and exact replay, movement retention and current List archive
admission. Browser collection passed; runtime execution is pending.

The initial subscription commit 9f46c85 passed Linux host tests, PostgreSQL
storage/migration/role checks, web quality and the source gate on run 37063617467.
Its full build-once release gate failed in the exact-image watch fixture described below.

The subsequent exact-image watch fixture on run 37063617467 failed its first
successful watch assertion (HTTP 200), after the rollback probes passed. The
existing `work_events_entity_type_check` admitted only Board/List/Card/Label, so
the new WatchSubscription event could not publish and the command rolled back.
Forward migration 034 repairs that publication contract without editing migration
033 or weakening the success/rollback assertions. Storage tests require a valid
watch event and reject cross-tenant watch references, reserved watch names on
other entity types, unrelated watch event names and unknown entity types.
Runtime rerun evidence is pending; the initial subscription full gate was failed.

Configured Card activity fan-out and event-time scopes are implemented in the
[watch activity producer](watch-activity-notifications.md), with full runtime
verification pending. Mentions/reminders and private notification realtime events
remain required.
PRD-17 stays open until all functional and acceptance requirements are proven.

## Card activity selection contract

`CardWatchActivity.Capture` binds the triggering Card event to the exact persisted
Card revision after mutation. Configured activity is Card create, update, move,
archive, restore, member addition/removal and label addition/removal. Deletion,
other entities, unknown activity and watch changes are excluded. Archive requires
the archived Card revision; other configured events require its active revision.
This is the current application policy, not a user-configurable settings surface.

A Card move uses its destination List when the event is recorded. A source List
watch alone does not receive that move; a direct Card watch remains applicable,
and a destination List or current Board watch applies. Card creation uses its
created List and Board. Overlapping subscriptions select each user once. Selection
has no inbox-style 50-recipient cap: every matching candidate must be considered.

The internal store query selects candidates only. It exposes no endpoint and is
not authorization evidence. PostgreSQL requires the owning command transaction;
the caller must hold the current Board gate and recheck active account, email
policy, Organization membership and Board view access before notification intent
creation. Self-suppression, assignment/watch dedupe, event persistence and intents
share that transaction through the [watch activity producer](watch-activity-notifications.md).
Domain tests cover revision mismatch, relevant event
selection, movement, overlap, unwatch, tenant isolation and 75 additional watchers;
The candidate contract passed Linux domain/host/source checks on run 37067080773.
The subsequent notification producer's runtime evidence remains pending.

## Shared Board replay privacy

Watch transitions occupy the Board's durable stream but are personal state.
Replay explicitly accepts only the two supported watch transition names and
always projects them to `BOARD_INVALIDATED`, even if an adapter incorrectly
reports the subscription as visible. Subscription identity, revision, actor and
watch/unwatch type remain private; the Board cursor can advance to later shared
events. PostgreSQL and Demo adapters both treat these entities as hidden. Unknown
entity types remain rejected. This does not implement recipient-private realtime
notification delivery, which is still required separately.

Unit and host tests cover privacy for both the subscriber and Board administrator.
The exact-image fixture waits for the real Worker to publish readiness and checks
both clients' replay and cursor recovery. These checks were added after finding
that the prior replay entity allowlist rejected WatchSubscription entirely.

The a102259 exact-image run failed the readiness assertion at fixture line 88:
its newly created Organization was absent from the Worker's explicit processing
scope. The fixture now recreates the same release Worker image using the existing
isolated event-test override, then restores the default Worker during cleanup.
The readiness, contiguous cursor and private projection assertions remain intact.
Fresh CI is required to verify this repair and the subsequent fan-out checks.

Watch timestamp admission now uses the inbox's strict canonical UTC parser and preserves PostgreSQL microseconds/.NET ticks when comparing createdAt and updatedAt. Invalid calendar dates, ambiguous local dates, non-UTC offsets and precisely reversed updates are withheld instead of being accepted by permissive millisecond Date.parse. Four negative cases and a valid microsecond ordering case cover disclosure/mutation-control admission. All 62 watch/inbox parser component tests, SPA/full browser typechecks and lint pass. Original subscription identity, version, same-key retry and server authorization rules remain enforced. Actual native release acceptance and recipient-private notification event delivery remain pending.

## Executed native access restoration

The [extended five-case cross-Board run](prd-17-acceptance.md#executed-native-watch-re-admission-and-retained-history)
passes under strict verification. The four non-Owner cases regain access through
actual ADMIN/MEMBER re-grants or Organization/Public visibility restoration.
Their direct Card watches retain exact subscription ID/version/creation/update
clocks and become readable at the current destination. The keyboard watching
dialog reports the retained state without another watch mutation. Original
notification rows/private journal fingerprints remain unchanged; both native
desktop/phone inboxes recover the four retained articles. A subsequent eligible
edit adds exactly one new notification/private event to each client, preserving
all original HTTP rows and canonical journal envelopes. Activity while access
was unavailable stays unnotified. No stored intent/history is fabricated.
All five cases pass together in 6.1 minutes, including the unchanged Owner
control; tagged Axe/overflow and browser typechecking pass. Current retained
release-image/full CI and complete PRD acceptance remain required. PRD-17 stays
open at **16% estimated work remaining**.

Personal watch-state disclosure also rechecks the active account after the bounded watch read. Until that check succeeds, neither personal state nor mutation controls are published. A changed account retires previous state and uncertain intent; malformed or unavailable post-read admission withholds state and private diagnostics. Held-response tests exercise all three outcomes, while updated command/retry/focus/reopen fixtures preserve their original assertions. All 31 watch component tests, SPA typechecking and lint pass locally. Server admission remains authoritative; actual native/immutable-image acceptance remains pending.

An open admitted watch dialog now also refreshes on the browser online event. Offline failure withdraws personal state and mutation controls; recovery follows the same pre/post account checks and authoritative watch read. The listener is removed with the dialog effect, and a component regression verifies fresh recovery plus no reads after unmount. All 32 watch component tests, SPA typechecking and lint pass locally. Actual native recovery remains pending immutable-image CI.

## Preserve dismissal focus during a protected read

If a protected read starts while Check has focus and the user then moves to an
enabled Done button, read completion preserves Done focus. Unknown commands still
disable dismissal and retain the original Retry focus recovery. Personal-state
admission, server authorization, watch revision and same-key retry are unchanged.
The held-read keyboard regression fails before the fix and passes afterward.
All 86 Organization Home, watch-control and surface-admission component cases pass;
web/browser typechecking, targeted lint and production build also pass. The build
retains its existing large-bundle advisory. Current immutable release and full
notification delivery acceptance remain required; PRD-17 stays open with **24%**
estimated work remaining (planning estimate).

The unchanged watch browser scenario passes in the final three-case native run
(all three pass, 2.7 minutes), using the compiled web, actual Production API,
restricted PostgreSQL and scoped Worker. It verifies Board/List cross-client
watch/unwatch, keyboard dismissal/focus, Card lost-response same-key/body recovery,
direct-watch continuity after movement, and archived-parent withdrawal/404. The
baseline watch scenario also passed; the deterministic held-read component case
is the failing-before evidence for the focus repair. See
[full invocation scope](web-spa-boundary.md#current-surface-admission-and-watch-keyboard-execution).

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

## Actual watch and activity lock ordering

The [six actual command-order scenarios](prd-17-acceptance.md#executed-actual-watch-and-activity-command-ordering)
pass CARD/LIST/BOARD watch eligibility in both orders under strict account policy.
Two independent API clients are observed waiting on the real Board gate, with the
queued blocker checked before release. Unwatch-first suppresses the source's
notification; activity-first retains exactly one. Watch identity/creation clocks
survive re-enablement, and exact original-key replays preserve all recorded effects.
Final inbox/source/journal attribution matches independently stored records.
The complete case passes in 1.1 minutes; this is HTTP/PostgreSQL correctness,
not a new native, transport, rollback or capacity claim. Current retained-image
and full acceptance remain required. PRD-17 stays open at **15% estimated remaining**.
