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
