# Checklists and items (PRD-13)

This is the persistence/domain foundation, not a completed checklist feature.
PRD-13 depends on the existing Card and audit contracts (PRD-08/22) within the
canonical dependency cycle. API commands, typed stores, MUI interactions,
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
