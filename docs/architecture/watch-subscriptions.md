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
