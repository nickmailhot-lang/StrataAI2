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

## Relative card positions

An optional `beforeCardId` on the move API inserts immediately before that current
active destination card. It is mutually exclusive with an explicit rank and may
not name the moving card or the empty UUID; admitted invalid combinations return
`invalid_move_position` (400). The moving card's expected version remains required.
Null/absent beforeCardId keeps existing append/explicit-rank behavior and retains
its historical receipt fingerprint, allowing old successful requests to replay
across this API extension. A non-null anchor is included in the fingerprint.

After current admission and version checks, PostgreSQL locks the active destination
list. Fresh scoped queries resolve the anchor and immediate predecessor, excluding
the moving card, using bounded-result queries with tenant/parent/rank predicates
rather than loading all sibling rows into the application.
The midpoint rank, update, audit, event/job and receipt remain in the owning tenant
command transaction. A missing/moved/archived or foreign-destination anchor returns
the generic `version_conflict` without disclosing its metadata. Duplicate/exhausted
local rank intervals return stable `rank_space_exhausted` (409), rather than an
ambiguous storage failure. Demo performs equivalent allocation under its store lock.

An API-host case checks relative insertion, prepend, self/mixed inputs, anchor-bound
key rejection, non-reapplying replay and a wrong-list anchor. The required exact
release PostgreSQL fixture adds sixteen concurrent relative moves before one
anchor on its 5,000-card destination and checks bounded interval/unique ranks and
unchanged sibling ordering. Build/shell syntax pass locally; Linux execution is
pending. Client positional review controls were subsequently added in f3f854b;
drag/drop and exhausted-interval rebalance remain unfinished. No full ticket or
latency acceptance claim is established.

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

An additional API-host boundary case attempts insertion before the lowest valid
rank twice with the same retry key. It requires rank_space_exhausted on both
responses and verifies that source placement, source rank, both card versions,
and the destination anchor remain unchanged. This case compiles locally;
execution remains pending Linux CI because Windows policy prevents local API
test-host execution. It verifies rejection safety, not automatic rebalance.

The required exact-image PostgreSQL rank fixture also checks two exhausted moves
with one retry key, inspecting persisted placement/ranks/versions and the absence
of CARD_MOVED audit records and events. Shell syntax validation passes locally;
these production transaction assertions remain pending Linux CI execution.
