# Canonical entity route clocks

[Migration 117](../../db/migrations/117_entity_route_clocks.sql) adds creation
clocks to Board, List and Card routing projections, and creation/update clocks
to Label routing projections. Each clock is copied from the matching canonical
entity in the same Organization and parent scope. These are canonical entity
history facts, not measurements of when a projection row was written.

The historical backfill locks canonical tables before their projections and
uses persisted canonical timestamps. It preserves routing identity, parent,
visibility and lifecycle fields. Missing or mismatched sources leave a null
creation clock and refuse the entire transaction, including column additions
and ledger publication. There is no migration-time timestamp fallback.

BEFORE INSERT/UPDATE triggers maintain those clocks without changing the
existing canonical synchronization triggers. New routes inherit source clocks;
Label status changes also refresh the canonical update clock. Supplied clocks
that contradict canonical history are rejected. Canonical updates may supply
their newly synchronized update clock, while an unchanged route clock is
replaced with the currently owning source clock. A no-op with unchanged source
history retains both timestamps. Trigger lookup uses the caller's tenant RLS
context; there is no SECURITY DEFINER lookup or new runtime permission grant.

API and Worker startup require ledger 117. The readiness contract removes and
restores its entry alongside every previously required version. Existing route
read payloads and entity authorization remain unchanged.

The full migration runner retains clean/repeat/populated-forward-upgrade,
issuer lease drain/refusal, authority page history, concurrent serialization,
failure rollback and unrecorded-migration refusal. Its new fixtures compare
all four populated projections' pre-upgrade non-clock fields and exact
canonical clocks, reject explicit creation/update-clock replacement, check
no-op stability and canonical-source update propagation in a rolled-back
transaction. Fixture-only migration numbers advance to 118–120.

The locked Release build passes with zero warnings and errors. Fresh restricted
issuer authority, recipient replay/discovery and complete API/Worker required
ledger readiness contracts pass against schema 117. Their containers and
credential files were independently confirmed removed, with the original three
services preserved. These contracts do not establish complete entity-route
product acceptance or immutable-image release proof.

The first private migration invocation stopped before SQL because copied
Windows shell line endings were invalid in Linux. The normalized invocation
then refused an incomplete test fixture: earlier storage tests had rolled back
the rows required to cover all four projections. Both reports are retained.
After explicit historical hierarchy fixtures were added, the complete
clean/repeat/forward/clock/serialization/failure gate passed, including all four
source propagation, no-op and clock-tamper checks. Its container and credential
file cleanup were independently confirmed. A fresh complete gate also passed
with explicit unmatched-source refusal: the historical Board-route fingerprint
was unchanged and all four new creation columns and ledger 117 remained absent
after rollback. Removing only the deliberately orphaned fixture route allowed
the upgrade, repeated application and all original gate checks to pass.
Private evidence resides outside the repository in the
`entity-route-clock-*-native-20261009` fixture directories.

This repairs four projection clock gaps in the
[PRD-01 audit](prd-01-acceptance.md). Invitation routing also projects
Organization metadata and needs its own writer/history audit. Mutable
operational counters, leases and sweeps remain in scope; this migration does
not resolve the recipient revision-counter historical delivery-clock gap.
