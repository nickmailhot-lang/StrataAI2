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
and indexes current watcher tuples. Runtime startup requires all 33 migrations.
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

This implements the subscription foundation. MUI watch controls, recipient
selection/fan-out for relevant activity, event-time List/Board scope rules,
mentions/reminders and private notification realtime events remain required.
PRD-17 stays open until all functional and acceptance requirements are proven.
