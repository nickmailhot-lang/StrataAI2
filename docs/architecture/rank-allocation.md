# Work rank allocation

Ranks remain 30 decimal digits and sort lexicographically. Interior moves use
midpoints. Default appends and prepends use a stride of 10^18 where space permits,
with midpoint fallback near an existing boundary. This avoids the previous
100-append exhaustion on a newly created group. Unit tests cover 200 and 5,000
consecutive operations in both directions, retaining space between neighbors.

Default creation passes an absent rank into the store. PostgreSQL locks the
parent row, then reads only the last active sibling rank using the existing
tenant/parent/rank index. The separate read statement obtains a fresh READ
COMMITTED snapshot after waiting for another creator. The allocation, insertion,
audit, durable event, delivery job, and retry acknowledgment share the command
transaction. Demo storage performs allocation and insertion under its store lock.
Explicitly supplied ranks retain their previous validation and behavior.

`POST /cards/{id}/move` also accepts an absent/null `rank` for append. The current
source/destination must still be active on the same Board, and current server-side
move permission and expected card version remain mandatory. After admission, a
known stale version returns `version_conflict` before allocation. PostgreSQL locks
the destination list and reads its current active tail in a separate statement,
excluding the moving card itself. The existing tenant/Board command transaction
contains allocation, compare-and-update, audit, event/job and retry receipt. Demo
allocation occurs under the store lock. Empty strings are invalid explicit ranks.

The retry fingerprint represents append with null rank rather than the generated
rank. A successful retry returns the originally allocated rank/version without
another allocation or reapplying a later move. Changing destination/version starts
a different command and cannot reuse that completed key. This adds no cross-Board
move authorization or client drag/drop behavior; those PRD requirements remain.

An API-host case covers append order, explicit-rank compatibility, stale/invalid
input, self-tail exclusion and old-receipt replay after a subsequent move. The
required exact-image rank fixture adds 16 concurrent null-rank moves into the
existing 5,000-card destination, checks unique sequential ranks and verifies a
durable receipt does not restore a former destination after a later move. Build
and shell syntax checks pass locally; Linux API-host and PostgreSQL execution are
pending. This fixture is not a throughput or initial-render latency benchmark.

The exact-image CI check issues independent concurrent commands for 128 lists
and 128 cards, checks rank uniqueness, and appends to a database fixture containing
5,000 cards with descriptions. The latter checks allocation at the scale boundary;
it is not an API throughput or board rendering benchmark. API host tests exercise
200 concurrent list creates, card creates, and exclusion of archived siblings.

Automatic transactional rebalance remains pending. An existing exhausted edge
returns HTTP 409 with `rank_space_exhausted`, before insertion, rather than an
unhandled exception. Concurrent explicitly ranked moves, list movement, keyboard
movement, virtualization, and full performance acceptance remain outstanding.
The PRD-06/07/08 tickets must remain open until their complete criteria are met.
