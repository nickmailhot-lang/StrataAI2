# Attachment sweep checkpoint audit clocks

The preview backfill and scan recovery checkpoints are mutable internal
records under PRD-01 FOUND-FR-009. Their original `cursor_created_at` orders
candidate attachments/jobs. It is not checkpoint creation or mutation time,
and resetting a cursor to null loses no valid audit clock because earlier
schemas never recorded one.

[Migration 129](../../db/migrations/129_attachment_sweep_audit_clocks.sql)
records finite database `created_at`/`updated_at` for new checkpoints. A
meaningful cursor change, including reset, records a database update time
that cannot precede a known creation or previous update. Exact no-ops preserve
clocks. Caller-supplied audit fields cannot change creation or restamp an
unchanged cursor; checkpoint tenant identity cannot change. Failed updates
roll back the cursor and tentative clock together.

Existing checkpoints retain their exact tenant/cursor payload and both audit
times remain null/unknown. A future real cursor change records its own update
time without claiming to recover checkpoint creation or previous resets. No
timestamp is copied from the candidate seek key or migration time. This
provides prospective coverage; complete legacy timestamp acceptance remains
open, as for [Work receipts](work-replay-update-clocks.md).

The original current migration-051 preview and migration-052 scan capabilities
are unchanged: tenant admission, parent/checkpoint locks, SKIP LOCKED behavior,
32-candidate page limits, retry identities, lease/source fences and durable
publication/recovery effects remain. Runtime table grants remain denied; the
Worker calls the existing private capabilities. Audit triggers execute inside
those owning transactions. No object-provider or scanner behavior changes.

The complete [new SQL regression](../../scripts/ci/test-attachment-sweep-clocks.sql)
fails against schema 128 at missing new checkpoint creation clocks, and passes
on fresh schema 129. It invokes both actual restricted Worker capabilities,
then checks meaningful changes, original creation preservation, rejection of
caller/candidate audit clocks, no-op preservation, incomplete cursor rollback
and actual Worker reset. The failed baseline is retained; owned baseline
containers/environment files are independently absent.

Fresh schema 129 passes all six complete gates: sweep clocks, Work receipt
clocks, original tenant schema, RLS, runtime roles and the entire migration
runner. The runner retains every original upgrade/refusal and serialized/fault
scenario and now also proves exact four-row legacy sweep preservation, unknown
audit clocks, actual restricted Worker resets and no-op refusal to manufacture
legacy times. Staged sources match after normalizing both sides' newlines.
Owned verification resources are independently absent. The locked full solution
build passes with zero warnings/errors.

A fresh original archive-history/readiness mode passes against its own
schema-129 PostgreSQL/pgvector fixture and new compiled artifacts, including
all 129 independent required-ledger refusal/restoration checks for API and
Worker. Its owned containers/environment files are independently absent.
The fresh **unfiltered** default schema-129 persistence executable subsequently
passed with its own separate fixture and compiled artifacts, original complete
default path, nested preview/recovery/Worker contracts and original concurrency/
scale budgets. It ran with no mode arguments, filters or case retries. Its 68
passing summary lines are not an independently collected test-case count.
Owned containers and credential environments are independently absent. Special
mode-only branches, provider/native browser acceptance and later schema versions
have separate verification requirements. The earlier full Linux API invocation
also finished with all 697 actual results passing; its compiled payload is
schema 128 and cannot certify this later migration. See
[source test results](source-test-results.md).
Current immutable
build-once release acceptance is outstanding.

All six [classified mutable clock candidates](remaining-clock-candidates.md)
now have prospective repairs in migrations 128–132. Legacy provenance and the
full original acceptance scope remain unresolved. PRD-01 remains open at
**34% estimated work remaining** (planning estimate).
