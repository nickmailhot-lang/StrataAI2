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
[PRD-01 audit](prd-01-acceptance.md). Invitation routing retains an
Organization-name snapshot captured on publication (legacy rows received their
label in migration 016) and needs its own clock audit. Mutable
operational counters, leases and sweeps remain in scope; this migration does
not resolve the recipient revision-counter historical delivery-clock gap.

## Preserve discovery authorization refusals

The original mandatory restricted routing test exposed a schema-117 regression:
a discovery-only insert remained denied, but the BEFORE clock trigger raised
23514 for its RLS-hidden canonical source before PostgreSQL checked the route's
write policy. The existing security contract requires authorization SQLSTATE
42501. The test was not relaxed. A fresh normalized forensic reproduction
captured 23514; a separate private forensic attempt stopped before SQL because
Windows line endings were copied into its Linux script. Failed evidence remains
outside the repository.

[Migration 118](../../db/migrations/118_entity_route_clock_admission.sql) replaces
the clock function in a forward upgrade. If the source is unavailable while
route RLS applies and the caller lacks the matching tenant context, it raises
42501. An owning-context or administrative missing source still raises 23514.
Canonical clocks, historical migration 117, table grants, RLS policies and
discovery reads are unchanged. API and Worker require ledger 118.

The original restricted route gate passes after the repair, including widened
reads, wrong/missing/malformed lookup context, cross-tenant isolation, denied
discovery writes and transaction-local context reset. A fresh final invocation
also passes denied List/Card/Label inserts alongside the original Board insert,
requiring the same authorization error for all four clocked projections.
The locked Release build, complete migration gate through 118 and restricted
persistence/full required ledger readiness contracts pass. Fixture-only
migration numbers are now 119–121. Current immutable-image CI and deployed
upgrade proof remain required.

## Invitation routing clocks

[Migration 119](../../db/migrations/119_invitation_route_clocks.sql) adds
creation/update clocks to invitation routes from the matching canonical
invitation identity, Organization and token hash. Migration 099 already owns
canonical invitation revision clocks. The current synchronization writer is
`sync_invitation_route` from migration 025; it updates invitation state but
retains the Organization-name publication snapshot introduced in migration 016.
Organization renames do not write this route or create an invitation mutation.

The historical backfill locks canonical invitations before their projections.
It copies persisted clock facts, preserving every previous route field. An
unmatched source or non-finite/contradictory clock refuses the entire upgrade.
The BEFORE guard maintains canonical clocks under caller tenant RLS, retains
42501 for discovery-only writes and rejects invented explicit clock values with
23514. No runtime grant or SECURITY DEFINER lookup is added. Startup requires
ledger 119; public routing payloads and bearer/recipient semantics are unchanged.

The complete local migration gate passes through 119: an intentionally orphaned
route refuses the upgrade with the whole route fingerprint unchanged and both
new columns and ledger absent, followed by successful repair/repeat application.
All historical non-clock fields and source clocks are compared, and explicit
clock replacement and no-op stability are checked in a rolled-back transaction.
All prior migration, clock, serialization and failure/unrecorded assertions
remain. Fixture-only migration numbers advance to 120–122.

The restricted invitation metadata contract now compares route clocks with
canonical creation/acceptance/revocation, refused repeats and owning rollback.
An administrative fixture rename separately verifies that the retained
Organization label and invitation clocks do not change. Current compiled-source
execution passes this expanded contract, restricted authority/readiness and all
five route types' discovery-write denial checks. The locked Release
build passes with zero warnings/errors, as do 179 integration/build metadata
source checks. Current immutable-image CI/deployed verification still governs
acceptance. Other mutable operational records and recipient revision-counter
historical delivery clocks remain in the foundation audit.
