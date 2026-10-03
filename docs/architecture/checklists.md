# Checklists and items (PRD-13)

This implements the persistence/domain foundation and authenticated checklist
reads/creation. PRD-13 is not complete.
PRD-13 depends on the existing Card and audit contracts (PRD-08/22) within the
canonical dependency cycle. Remaining API commands, MUI interactions,
authorized realtime delivery and complete lifecycle acceptance remain required.

Checklist identity, Organization and Card parent IDs are immutable. Item identity,
Organization and Checklist parent IDs are immutable. Both have canonical
30-digit persistent ranks compatible with the existing RankToken allocator,
UTC creation/update timestamps and positive revisions. Titles normalize outer
whitespace and are bounded to 160 UTF-16 units; item text is bounded to 2000.
NUL/blank content is rejected. No item assignee or due-date fields are introduced.

Completion records its original actor/time and clears both on uncompletion.
Completing an already completed item does not replace attribution or advance
revision. Reorder/edit/delete no-ops retain revision. Validation and timestamp
checks precede all field mutation, so a failed backwards update leaves the
complete prior entity intact. Deletion creates a tombstone and preserves content
and attribution for later audit/retention; edits to tombstones are rejected.
The eventual delete command must tombstone the Checklist and all its active items
and publish audit/events atomically. That aggregate command is not implemented yet.

Progress is derived from active item counts: completed / total, and zero when
empty. The domain calculation rejects foreign-tenant/parent or duplicate items.
Paged API responses must use canonical aggregate counts, not the current page's
subset. Item completion is separate from Card due completion.

Migration 040 adds forced-RLS tables with tenant-composite Card/Checklist parent
FKs, a tenant-composite completion-attribution FK to retained Organization
membership, bounded text/rank/revision/timestamp checks and unique active ranks
per parent. Board/List scope follows the current parent Card, avoiding redundant
Board IDs that would become stale during a same-Organization Card move. This
does not waive current parent/member/actor authorization in future commands.
The API role can read/insert/update; physical child deletion and Worker content
reads remain denied. No new framework, store or deployable service is added.

The production schema guard now requires migrations 039 and 040 in addition to
the previous 38. The exact-image security fixture removes each independently,
requires safe 503 responses from both processes/protected routes and restores
before proceeding. This also repairs omitted readiness coverage for the already
adopted Board timezone column in 039.

Fifteen Domain cases cover canonical identity/ranks, completion attribution,
no-ops, zero/deleted-item progress, foreign/duplicate children, invalid bounds,
backwards mutation atomicity, tombstones and UTC normalization. A mandatory
PostgreSQL fixture uses a non-superuser/no-bypass role and tests cross-tenant
reads/writes/parents/completers, missing scope, active rank collisions, completion
field shape, timestamps/revisions, tombstone progress and rank reuse. The generic
tenant-schema guard includes both new tables. Strict local solution build and
shell syntax checks pass; Linux execution of these new cases is pending CI.

Remaining PRD-13 work includes authorized/CAS/idempotent CRUD and position
commands with current parent/actor rechecks, bounded ordered reads and counts,
transactional audit/outbox, Card copy/move/delete integration, MUI recovery and
confirmation/focus, live/reconnect/access-loss scenarios, telemetry and full
normal/large-data performance evidence. No acceptance checkbox or issue closure
is implied by this foundation.

The first PostgreSQL run for 0d392f0 (37091711154) failed because migration 039
never inserted its version ledger row. Its timezone column existed, but the
stream's `\quit 3` ignored the argument and returned success before migration
040 ran. Migration 039 now records its version and supports the known existing
unrecorded column/constraint state, preserving policy values while checking the
column shape and rebuilding its constraint transactionally. The stream raises
a SQL exception on a missing ledger entry under ON_ERROR_STOP, so later migrations
cannot silently be skipped. The migration-runner fixture now exercises 039/040
upgrade/repeat, recovery of an existing Honolulu policy with a missing 039 ledger,
serialized runners, transactional SQL failure and explicit rejection of an
unrecorded migration. Shell syntax and emitted SQL inspection pass; corrected
PostgreSQL execution remains pending.

Authenticated GET/POST `/cards/{cardId}/checklists` now admit the current
Organization, Board, List, Card and actor inside the owning transaction. Reads
allow archived parents with `canEdit=false`, return at most 50 checklists with a
Card-bound rank/id cursor, and compute counts over all active items. Public
visitor read support is still required. Creation checks the Card revision,
allocates ordering space before mutation, advances the Card revision without
changing its content/dates, inserts the Checklist, records its audit identity,
and appends a content-free Card aggregate invalidation plus retry receipt in the
same transaction. Replay must pass fresh edit admission, so archived or revoked
access cannot reveal an old acknowledgement. Rank exhaustion is a stable conflict
before any write. The returned child is the persisted PostgreSQL row, including
its canonical timestamp precision.

Two API cases cover rejected input, private reads/writes, Card CAS, retry keys,
ordered 50/13 pages, empty progress, archived reads and access removal. A new
mandatory exact-image fixture additionally exercises INSERT failures both before
the child and after Card/child/audit writes, whole-command rollback, same-key
recovery, SQL-backed full aggregate counts/deleted-item exclusion, rank exhaustion
and lifecycle/replay admission. These new API/container cases await Linux CI.
The bf2ab79 PostgreSQL job passed the checklist storage, migration recovery and
runtime-role fixtures; the strict local solution build has zero warnings/errors.
Item editing/completion, checklist editing/reorder/delete, public views, copy and
retention integration, MUI and full performance evidence remain unfinished.

Checklist rename is exposed as PATCH `/cards/{cardId}/checklists/{checklistId}`.
Both Card and Checklist revisions must match. Normalized no-op titles preserve
both rows and emit no audit/event; a changed title advances both revisions and
publishes CHECKLIST_UPDATED as a Card aggregate invalidation atomically. Child
lookup is scoped to the admitted tenant and Card; wrong-parent IDs receive the
same stable unavailable response. Retry receipts recheck current parent, child
and edit access. A source API case exercises no-op preservation, stale individual
revisions, concurrent clients (one winner), wrong-parent IDs and revoked replay.
The exact-image fixture forces the event insert to fail after both updates,
checks whole-command rollback, retries the original key and verifies replay and
no-op event/audit counts. Linux execution remains pending for this increment.
