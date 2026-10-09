# Organization deletion checkpoint clocks

## Current verified result

All seven original restricted deletion contracts and complete runtime readiness
now pass together against schema 125. This includes the checkpoint one day ahead
of wall time, late rollback, completion and replay, the original 100,002-Card
candidate traversal, graph-page recovery, full 5,000-active/100,000-archived-Card
scale graph and restricted discovery. Independent SQL verifies every retained
checkpoint's creation equals its immutable request, finite ordered clocks and
the completed checkpoint's update/completion equality. Owned containers and
credential files are absent; original services remain running.

The [Card route lookup repair and complete measurements](card-route-clock-lookup.md)
resolve the unchanged large seed failure recorded below. The complete extended
migration gate and original routing-isolation gate also pass. These local results
do not establish current immutable-image CI or complete foundation acceptance;
PRD-01 remains open at **34% estimated work remaining** (planning estimate).

## Retained investigation

This repair addresses a confirmed mutable-record gap in
[PRD-01 foundation acceptance](prd-01-acceptance.md). The checkpoint already
records `updated_at`; its immutable `(tenant_id, request_id)` deletion request
provides the original `created_at`. The
[publisher](../../src/StrataAI.Infrastructure/Organizations/PostgresOrganizationDeletionJobPublisher.cs)
inserts both records in the owning transaction. Page and terminal capabilities
maintain the checkpoint update time; a job lease does not define creation.

[Migration 124](../../db/migrations/124_organization_deletion_progress_clocks.sql)
locks the request/checkpoint sources, refuses missing or contradictory legacy
history and backfills only creation time from the matching accepted request.
It preserves every existing checkpoint field and update timestamp. New inserts
derive creation from that same source. A tenant-scoped invoker trigger rejects
creation replacement and backward update time; a constraint requires finite,
ordered clocks. A missing source without owning tenant context retains the RLS
authorization refusal. No runtime grants, policies, checkpoint phase rules,
publication ordering, Worker leases or durable job payloads change.

The whole migration gate includes populated history with microsecond precision,
transactional refusal of contradictory update history, unchanged-row/absent-
column/absent-ledger proof after refusal, repeated upgrade, creation tampering,
backward/infinite update refusal, unchanged no-op clocks and source-derived
future inserts. All existing clean/forward/serialization/failure/ledger checks
remain. Restricted initial and terminal contracts additionally compare creation
against the immutable request in PostgreSQL, without timestamp truncation.

The initial draft's full migration gate passes, including transactional contradictory-history
refusal, untouched legacy fields and repeated forward upgrade. Independent
checks match all four exercised source files with the staged gate and confirm
zero owned gate containers or credential files. The locked current solution
build passes with zero warnings/errors. The Windows Python run reports 51
passes and one existing host symbolic-link capability skip out of 52 cases.
All 234 Node source cases pass, with zero failed or skipped cases. The Python
skip remains distinct from the required Linux execution.

The native invocation of all seven existing Organization deletion contracts
passes readiness, initial progress, publication and terminal completion, then
fails with an Npgsql timeout while seeding the candidate fixture's original
100,002 Cards. That seed precedes checkpoint publication and does not touch the
repaired table. The complete failed invocation remains private in
`deletion-progress-clock-contract-native-20261009`. It is not an aggregate pass;
no rows, assertions or deadlines are reduced or enlarged to recover it.
Independent cleanup confirms zero owned contract containers and credential
files, with all three original services running. The full unchanged group needs
another completed result before this repair is committed. This draft does not
certify current build-once release acceptance. Other mutable-clock candidates and all remaining
foundation acceptance requirements remain; PRD-01 stays open at **34% estimated
work remaining** (planning estimate).

A fresh unchanged full seven-contract invocation is queued in
`deletion-progress-clock-contract-quiet-native-20261009`. Its live controller
checks Docker for removal of the specific prior Linux API and browser-owned
containers before starting the same current build, source schema and restricted
roles. It does not stop or restart those runs. This changes scheduling only;
the original 100,002-card seed, traversal assertions, other deletion contracts
and all deadlines remain. The first failed invocation is retained separately.
Container removal is the readiness condition, not a presumed elapsed duration;
this queue is not execution or passing evidence.

Further source review found that the existing terminal capability used
`GREATEST(clock_timestamp(), parent.updated_at)` without the locked checkpoint's
update time. After clock correction, a retained checkpoint ahead of wall time
would be rejected by the new monotonic guard. The revised forward migration
retains the complete existing capability, its search path, lease fencing,
atomic effects and grants, adding `progress.updated_at` to that terminal clock.
The existing terminal contract now seeds a checkpoint one day ahead before its
late-expiry/rollback, completion and replay checks. No seed size or deadline
changes. A fresh full-group invocation against the unrepaired capability and
a fresh complete migration gate for the repaired body are staged separately.
The initial gate/build evidence above does not verify these later edits;
current edge-regression build and revised gate results are pending. The queued
full group must use the successfully built revised source before execution.

The edge-regression build now passes with zero warnings/errors. The full-group
invocation against the unrepaired terminal writer fails at completion with
23514 from the checkpoint history guard, proving the new future-checkpoint
case. Its failed report is retained and owned containers/credential files are
removed. The complete revised migration gate also passes. The queued full group
is independently confirmed unstarted before replacing its staged migration
with the exact revised source and selecting the successful edge-regression
build. Its full repaired result remains pending; no group pass is claimed.

The original browser invocation subsequently passes all 32 cases and removes
its owned fixtures. The quiet full deletion group then starts with the revised
source/build, passes the future-checkpoint terminal contract and again times
out while seeding the candidate fixture's 100,002 Cards. The same failure after
the earlier heavy runs finish does not support attributing it solely to their
CPU load. Its failed report is retained separately, and independent checks
confirm zero owned quiet-group containers or credential files. The queued full
current Linux API controller consequently exits without starting its suite.
The remaining candidate-seed performance diagnosis must preserve the original
fixture size, assertions and deadline; no repaired aggregate pass is claimed.
