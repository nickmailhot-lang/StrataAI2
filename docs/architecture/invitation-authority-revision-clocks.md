# Recipient authority revision clocks

[Migration 132](../../db/migrations/132_invitation_authority_revision_clocks.sql)
adds managed creation/update timestamps to recipient-authority revision counters
for [PRD-01 FOUND-FR-009](prd-01-acceptance.md). It preserves the original
private source/effect publication, paging, lease checks and global deduplication.

## Publication and update times

A newly inserted counter captures one database timestamp for both `created_at`
and `updated_at`. This is counter publication time, not the earlier source
event time. Caller-supplied audit timestamps are ignored. Counter identity
(`email_normalized`) cannot be reassigned.

An actual revision change preserves creation time and captures the greatest
of database time and the previous known clocks. Exact no-ops, clock-only edits
and duplicate delivery preserve the original clocks. Refused counter changes
roll back their attempted clock effects with the original transaction.

The original publisher still increments only after an admitted tenant/source/
email effect. User deactivation has the original additional global source/email
deduplication step: the same issuer event can affect multiple Organizations
while incrementing a recipient's global revision only once. Those deduplicated
tenant effects do not restamp the global counter. No publisher signature,
capability grant, page bound, lock order, lease fence or reader return shape
changes. The API retains SELECT of only normalized email/revision; the new
clocks do not widen its disclosure. PUBLIC cannot execute the clock trigger.

## Legacy provenance

Legacy counters retain their exact identity and revision, with both clock
fields NULL (unknown). Their first/last publication times cannot be recovered
from a maximum source timestamp or by counting every tenant effect as a global
revision. A no-op leaves unknown clocks unknown. A later actual change records
its new update time while preserving unknown creation time. No migration,
lease or unrelated source time is backfilled as historical evidence.

This is a prospective repair. Legacy provenance remains part of the original
open acceptance requirement; six prospectively repaired candidates do not
mean the complete timestamp/PRD acceptance scope is finished.

## Executed checks

The locked solution build passes with zero warnings/errors. A fresh schema-132
PostgreSQL/pgvector invocation passes **15 complete gates**, including the full
migration runner, original tenant/RLS/runtime-role gates, all five new clock
gates, full mail scope, all three recipient-authority SQL fixtures and the
original membership-source routing fixture. Upgrade/repeat/serialization/fault
checks preserve the original test scope and budgets. Staged DB/scripts match
current source after normalizing both sides' line endings.

[The new clock SQL gate](../../scripts/ci/test-invitation-authority-revision-clocks.sql)
retains the complete original three-family membership-source routing fixture.
It additionally checks first publication, subsequent increments, exact
duplicate/no-op clocks, creation identity, key refusal, invalid revision
rollback and unchanged recipient/Worker capabilities. Legacy upgrade fixtures
prove original counter preservation, unknown clocks, no-op handling and an
actual subsequent update without inventing creation history.

The original compiled `--schema-readiness-only` mode individually refuses and
restores all **132** required ledger entries for API and Worker. The original
`--invitation-issuer-authority-only` mode also passes: 205-Organization routing
in 100/100/5 pages, four independently leased concurrent first-effect lanes,
global cross-tenant deduplication, committed duplicate recovery, late-lease
rollback and original exhaustion/history/capability checks. Its original ten
final assertions remain intact; an additional assertion verifies that the two
globally deduplicated counters retain finite first-publication clocks after
all original concurrent and duplicate deliveries.

Private reports: `authority-revision-clocks-schema132-ci-native-20261009` and
`authority-revision-schema132-readiness-issuer-native-20261009`. Their owned
containers and credential environments are independently confirmed absent.
These are local database/compiled-runtime checks, not browser, external
provider or immutable-image release certification.

## Broader verification remains open

The full schema-129 default persistence executable completed successfully;
the older schema-128 Linux API invocation completed with **697/697 actual TRX
rows passing**, matching declared counters and unique execution IDs. Their
original scopes and version boundaries are recorded in
[source test results](source-test-results.md).

The later schema-130 full default invocation and independent CI both timed
out in the original 100,002-Card deletion fixture seed. Schema-131 full default
verification remains live with private thresholded nested-query plans for
diagnosis. The captured seed completes in that invocation, but a live partial
plan is not a complete suite pass or a fix for the independently reproduced
CI failure. No deadline, cardinality, assertion, page budget, filter or case
retry is changed. Current build-once CI still needs verified completion.

All six classified mutable candidates now have prospective clock repairs;
legacy provenance, other entities and the full original PRD/architecture
acceptance scope remain open. PRD-01 remains open at **34% estimated work
remaining** (planning estimate).
