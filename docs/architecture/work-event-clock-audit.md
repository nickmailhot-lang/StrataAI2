# Work event delivery clocks and legacy provenance

[PRD-01 FOUND-FR-009](prd-01-acceptance.md) requires audit timestamps for
mutable entities. [Migration 130](../../db/migrations/130_work_event_update_clocks.sql)
records `work_events.updated_at` for new sources and every subsequent admitted
readiness change. Historical activity remains immutable.

## Source history and delivery

The original `created_at` is the event's admitted source time. New insertion
initializes `updated_at` to that time, ignoring caller-supplied update clocks.
A real `ready_at` change records the greatest of the database clock, source
creation time and prior known update time. Replacing or clearing readiness
also records an update. Exact no-ops and attempts to change only the audit
clock preserve the previous clock.

The original AFTER history trigger remains AFTER UPDATE, so generated target
references are materialized before comparison. Its comparison excludes only
readiness and the separately managed update clock. Identity, actor caption,
metadata, source time, sequence and generated references remain protected;
a refused payload mutation rolls back its clock and readiness effects too.
The clock function has a fixed search path and no PUBLIC execute capability.

[PostgresWorkEventDeliveryStore](../../src/StrataAI.Infrastructure/WorkManagement/PostgresWorkEventDeliveryStore.cs)
continues to lock the exact live job/worker/lease claim, update only a pending
source and check the lease again before committing. Worker privileges remain
`UPDATE(ready_at)`; no audit-clock or payload write permission is added.

## Legacy records

Legacy update clocks remain NULL, meaning unknown. A pending source can have
been published and reset by an admitted administrative update. A ready source
can have had its readiness replaced. Neither `COALESCE(ready_at,created_at)`
nor migration time establishes the last historical mutation time.

The upgrade preserves every original field for pending, ready and previously
reset sources. A no-op leaves their unknown clocks unknown. A subsequent real
mutation records that new update without claiming earlier missing history.
Legacy provenance remains an unresolved part of the full timestamp requirement;
this prospective repair does not satisfy all of PRD-01.

## Verification

The locked solution build passes with zero warnings and errors. The original
`--activity-source-only` mode passes against real PostgreSQL/pgvector using
the compiled schema-130 contracts and restricted API/Worker accounts. It also
individually removes and restores all 130 required ledger entries for both
API and Worker readiness. Original activity pagination, discovery, source
identity and privileged publish/reset barriers remain in scope. This is local
compiled-runtime evidence, not immutable-image release acceptance.

[The clock SQL gate](../../scripts/ci/test-work-event-update-clocks.sql) checks
new source clocks, actual column-restricted Worker readiness, no-op/tamper
handling, reset timestamps and atomic rollback after forbidden history edits.
[The original attribution gate](../../scripts/ci/test-activity-attribution.sql)
retains every original mutation, tenant, attribution and generated-reference
assertion. Payload fingerprints exclude both delivery fields; the dedicated
clock gate verifies their separate invariants. Copy/move rollback fingerprints
likewise exclude the two asynchronous delivery fields while retaining the
complete historical payload.

Upgrade fixtures cover pending, published and reset legacy records. The full
migration runner, fresh tenant/RLS/runtime-role gates and the new SQL gate
passed together (eight complete gates) against a fresh schema-130 database.
Private reports are outside the repository in
`work-event-clocks-schema130-upgrade-complete-native-20261009`. The first disposable
staging attempt retained Windows shell line endings, and the second omitted
`scripts/migration-stream.sh`; neither failed the product clock checks. Both
owned test environments were removed, and verification uses a complete staged
source tree with normalized line endings.

The complete default schema-130 persistence executable ran without mode
arguments, filters or case retries and failed with an Npgsql read timeout
while preparing `OrganizationDeletionCandidatesContract`'s original 100,002
Card fixture. It did not reach a complete default-suite pass. The failed
report is retained in `work-event-schema130-full-persistence-native-20261009`;
its owned containers and environment files were removed. This unresolved
failure is also reproduced independently by CI run `37998254598` and
[the schema-131 diagnostic invocation](invitation-mail-update-clocks.md) is
collecting private slow nested-query plans; neither the focused activity pass nor the SQL
gates substitute for the complete suite. No deadline, seed cardinality,
assertion or case retry was changed. Existing full schema-129 persistence and
schema-128 API runs remain active with their separate version boundaries.

CI run `37996791819` on schema-129 head `a3023bff` passed its full migration
runner but failed the new sweep SQL gate with permission denied: that gate was
scheduled before `test-runtime-roles.sh` provisioned Worker capabilities. The
workflow now runs all three clock gates after the unchanged provisioning and
runtime-role checks. Clean local verification following this actual CI order
without pre-provisioning roles passed all eight complete gates, including
the full migration runner through 130 and all three clock gates. Reports:
`work-event-schema130-ci-order-native-20261009`. Current build-once CI must pass before release
claims can be made.

The prior schema-127 Board browser invocation finished with all 32 original
cases passing in one attempt each; it does not certify schema 130. PRD-01
remains open at **34% estimated work remaining** (planning estimate).
