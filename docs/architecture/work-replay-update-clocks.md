# Work receipt update clocks

PRD-01 FOUND-FR-009 applies to successful Work-command receipt completion as
well as canonical entity changes. The Work unit of work inserts a pending
receipt and updates its `result_json` in the same owning transaction. Earlier
schemas recorded only receipt creation and expiry; atomic commit did not
record a historical completion timestamp.

[Migration 128](../../db/migrations/128_work_replay_update_clocks.sql) records
`updated_at` for new receipts and future payload updates. Insertion uses the
original finite creation time. A changed payload gets a finite database time
at least as late as its creation and any previously recorded update. Exact
no-ops preserve the existing clock, including an unknown legacy clock;
caller-supplied update times cannot restamp it. Rollback discards both the
payload change and its clock. The existing unit-of-work transaction, admission,
original receipt identity, expiry and replay behavior are preserved. No
runtime grants change.

Legacy `updated_at` stays **null/unknown**. Migration does not reconstruct
completion from receipt creation, expiry, an unrelated entity timestamp or
migration time, and does not rewrite results as pending. A later actual payload
change records that change's time without claiming to recover an earlier
completion. Nonfinite legacy creation clocks refuse migration atomically.
Consequently this is prospective timestamp coverage; the original all-record
legacy clock requirement and the other five mutable candidate repairs remain
open. PRD-01 is not complete.

The [current SQL gate](../../scripts/ci/test-work-replay-update-clocks.sql)
uses the actual restricted API role to check creation, completion, malicious
clock overrides, no-op preservation, rollback and nonfinite admission. The
complete [migration runner](../../scripts/ci/test-migration-runner.sh) includes
original completed and pending legacy receipts, exact payload preservation,
unknown clocks, actual later updates, invalid-history refusal/rollback,
repeat application and its original concurrent-runner/failure checks.
The runtime requires migration 128 and the original readiness contract
individually checks every required ledger entry for API and Worker.

The locked full solution build passes with zero warnings/errors. The original
archive-history persistence mode passes against the new compiled schema-128
artifacts, including all 128 independent readiness refusal/restoration checks.
The new SQL gate and unchanged tenant-schema/RLS/runtime-role gates pass.
The first complete migration-runner attempts stop at the new upgrade fixture's
file-style directive, incompatible with the runner's SQL-command interface;
those failed attempts remain retained. The fixture format is corrected without
changing assertions. Fresh complete SQL/migration/security verification then
passes all five gates, including the entire migration runner through schema
128, invalid-history refusal, original receipt preservation, repeat upgrade,
serialized runners, failed-migration rollback and missing-ledger refusal.
Owned containers and environment files are independently absent. A fresh full
Linux API invocation uses the new compiled artifacts with the original pinned
runtime, all original cases, read-only source mapping and private reports;
its terminal outcome remains required. These local checks do not establish
current immutable build-once release acceptance.

The separate full schema-127 Board browser run has now passed all **32** actual
cases across the original 14 files, with 32 single attempts, zero skips,
unexpected/flaky results or report errors, in 1,407.21 seconds. It uses the
recorded frontend filter-focus repair and lifecycle source-epoch fixture
admission. Its frozen files match source; owned containers, database and env
files are independently absent. It predates this schema-128 change and does
not certify the later migration or release images.

See the [candidate audit](remaining-clock-candidates.md) and
[source verification record](source-test-results.md) for the remaining scope.
