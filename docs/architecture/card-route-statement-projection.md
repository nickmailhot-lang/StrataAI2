# Card route statement projection

[Migration 133](../../db/migrations/133_card_route_statement_projection.sql)
batches Card route INSERT and UPDATE projection through PostgreSQL statement
transition tables. Each source statement submits one ordered route upsert,
instead of submitting one SQL upsert from every Card row trigger. Per-route
canonical clock admission still executes for each route. The existing row
DELETE function remains unchanged.

The new functions use invoker security, a fixed `pg_catalog,public` search path
and revoked PUBLIC execution. Existing grants, forced tenant RLS, canonical
identity/ownership checks, route clocks and source mutation semantics remain.
The migration takes the existing source/projection tables under an exclusive
lock while replacing hooks; it rewrites no Card or route body or historical
clock. API and Worker readiness require ledger 133.

## Verification scope

The exact private prototype completed the original schema-132 readiness mode
and all seven original deletion contracts, without changing counts, command
deadlines, assertions, page budgets or retry behavior. The scale graph contains
5,000 active Cards, 100,000 archived Cards and 200 Lists. Actual restricted
Workers complete 826 bounded mutation jobs, 105,201 ready work events and one
ready terminal in 1,202,775 ms; maximum leased page time is 1,044 ms. This is
compiled-source deletion evidence, not HTTP mutation p95, browser acceptance
or an immutable release image. Owned containers and credential files are
independently absent. Private report:
`card-route-batch-prototype-deletion-native-20261009`.

Five original tenant/RLS/role/routing/route-clock companion checks pass. The
[statement gate](../../scripts/ci/test-card-route-statement-semantics.sql) passes
with both the current row hook and private batching prototype. It checks
returning-CTE inserts, multi-row updates, conflict updates, mixed insert/update
statements, zero-row writes, deletes and rollback. The mixed statement must
return all six expected Cards. These are after-statement projection checks;
restricted-role authorization remains covered by its separate original gates.
Private report: `card-route-mixed-conflict-semantics-native-20261009`.

A separate pair of fresh schema-132 databases executes the complete original
100,002-Card candidate seed SQL, with parameter values supplied for isolated
fixtures. Client process elapsed time is 17,373.876 ms for row projection and
15,697.173 ms for batching; both preserve 100,002 Cards and routes. There is
one observation per implementation, sequentially under local concurrent test
load. This is a preliminary local comparison, not repeatability, an Npgsql
command deadline test, production latency certification or proof that CI's
previous timeout is repaired. Private report:
`card-route-batch-original-seed-measurement-native-20261009`.

The production migration adds populated before/after full-body preservation,
unchanged canonical clock/deletion function checks, security/hook assertions
and the complete original migration-runner regression scope. The locked schema-133
solution build passes with zero warnings/errors. All 18 complete migration,
security, attribution, clock, mail/authority, routing and statement gates pass,
including populated upgrade preservation, repeat application, serialized runners
and failure/unrecorded-migration refusal. All 334 staged files match normalized
current source; owned gate containers and credential files are independently
absent. The initial private staging run failed because shell files had CRLF
bytes; its report is retained, and the corrected LF staging completes the same
unchanged gates. Report: `card-route-batch-schema133-ci-lf-native-20261009`.

Current API/Worker readiness passes all 133 individually missing/restored ledger
entries using the locked current compiled payload. All 133 migrations plus
role source match normalized current source. Its original complete unfiltered
default persistence invocation subsequently passes, without new case filters,
retries or budget changes. It reports 68 passing summary markers, not 68
independently collected cases, with no unhandled exception. The actual scale
phase completes 826 bounded mutation jobs, 105,201 ready work events and one
ready terminal for 5,000 active + 100,000 archived Cards and 200 Lists, taking
1,362,836 ms with maximum leased page time 941 ms. All 134 staged migration/role
files match normalized current source; owned containers and credential files
are independently absent. Report:
`card-route-batch-schema133-readiness-full-native-20261009`.
Immutable build-once CI and the full PRD acceptance matrices remain required.
PRD-01 remains open at **34% estimated work remaining** (planning estimate).

## Complete schema-133 specialized persistence modes

The later compiled audit-backend payload executes all **24/24 declared specialized
`--…-only` modes**, each once in source declaration order, with exit zero in every
mode. The terminal report's 24 unique modes exactly match current
`StrataAI.Persistence.Contracts/Program.cs`; this is 24 complete mode invocations,
not 24 independently collected cases or a substitute for the separate default
invocation above. Total mode execution time is **2,321,781.034 ms**.

Coverage includes event/delivery clocks, Board-member events, invitation audit
metadata and authority/concurrency/lifecycle, identity lifecycle/expiry/recovery/
registration, all readiness requirements, archive history, attachment publication
and preview lifecycle, Organization creation timestamps and deletion clocks/pages/
terminal behavior, activity source, notification batches and comment mentions.
The original large deletion graph, assertions, concurrency, deadline and retry
policies remain. No failing case is skipped or rerun to erase evidence.

Every mode gets a fresh database with all 133 migrations and restricted API/Worker
roles. All **134/134 staged migration/role SQL files** independently match
normalized current source. The runtime is pinned to .NET 10.0.12 image digest
`sha256:82e38976e7e8d2321a9cd4609e3bca84e229c1815bd91d0d0999816110f6e55d`.
Owned PostgreSQL/contract containers and all credential environments are
independently absent after terminal completion. Private terminal evidence:
`schema133-all-special-contracts-native-20261010/outcome.json`.
This strengthens current compiled-source persistence proof; complete API/frontend
runs, immutable tested-image CI, unresolved historical attribution and full PRD
acceptance remain separately required. PRD-01 stays open at **34% estimated work
remaining**; ARCH-03's missing module and execution coverage remains unchanged.


## Later CI failure in the original large candidate fixture

CI run [38026680592](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/38026680592)
for commit `36b9427c` failed PostgreSQL job `114142443195` in the complete default
C# persistence invocation. Migration/security/isolation SQL steps through that
point passed. The failure is `NpgsqlException` wrapping a read `TimeoutException`
at the first `OrganizationDeletionCandidatesContract` fixture batch, before
candidate traversal: `OrganizationDeletionCandidatesContract.cs:23/25` and that
revision's `Program.cs:252`. The batch includes the original **100,002 Cards**
and **200 Lists**. The stack does not identify which statement within the batch
consumed the deadline; a specific trigger or host cause is not yet established.

The later complete schema-134 local invocation passed that same full candidate
traversal with **100,002 Cards / 100,000 archived**, original 128-row page bounds
and **55,545 ms** traversal time. This does not erase or explain the CI failure.
The read-boundary candidate's complete unfiltered persistence executable is now
running with the original graph, assertions and deadlines, separately from its
focused invitation and browser proofs. No timeout increase, graph reduction or
retry has been introduced. Current exact-commit immutable-image CI and the full
PRD acceptance criteria remain required; PRD-01's estimated remaining work stays
**34%**.


A subsequent private copy of the **complete original candidate contract**, with
only initial-batch timing added, now passes using the same Npgsql client path.
Removing that instrumentation reproduces the original source exactly; its SQL,
assertions, graph, parameter handling and lease/deadline budgets are unchanged.
The initial fixture batch takes **22,403.856 ms** with the actual configured
Npgsql command timeout still **30 seconds**. The remaining original contract
passes with **100,002 Cards / 100,000 archived**, the same **128-row** page bound,
including private-free references, role/lease/identity fences and parent traversal.
Independent checks confirm zero owned containers or credential environments.
Report: `organization-deletion-candidate-seed-profile-native-20261010/safe-summary.json`.
This is one fresh local observation, not a production latency distribution or
proof that the CI timeout is repaired. The failed CI run and the separately live
complete unfiltered candidate invocation remain required evidence.


## Cached small-graph plan regression and function-scoped repair

A second distinct CI invocation, run `38028556097` / PostgreSQL job
`114151279068` on commit `634b3a2f`, fails at the same original candidate fixture
batch with an Npgsql read timeout. A fresh private probe now reproduces it after
eight small real Card writes warm the same admin backend: the untouched original
100,002-Card batch fails after **30,110.368 ms**, with its configured **30-second**
command timeout. Its original assertions and graph are not reduced or retried.

The paired fresh prototype differs only by a function-scoped index preference
on `maintain_entity_route_clocks()`. It completes the same original batch in
**13,869.111 ms** and the full original contract with **100,002 Cards / 100,000
archived**, 128-row pages and **11,839 ms** traversal time. The failed baseline
and passing prototype remain separate private reports:
`organization-deletion-candidate-warm-plan-baseline-native-20261010` and
`organization-deletion-candidate-warm-plan-index-native-20261010`.
This supports a cache-sensitive lookup problem; it is not general production
latency certification.

The production repair adds migration 135 to discourage sequential plans only
inside the canonical clock function. It retains the exact function body,
invoker security, fixed search path, grants, RLS and historical values. A new
mandatory persistence prerequisite warms the backend before the existing full
candidate graph and checks exact projected clocks and restoration of the
caller's planner setting. The source-linked production helper plus the complete
original candidate contract already pass on fresh schema 135.

All original 18 migration/security/routing/clock gates now pass,
including populated body/guard/ACL preservation through actual migrations 134
and 135, the unchanged original clock-tampering checks, repeat upgrades and the
original serialized/failure/unrecorded migration checks. Synthetic fixture
versions move to 136/137/138 to avoid the real migrations; their sleeps/assertions
remain. The initial private gate failed before upgrades because its staging
omitted the root migration helper. The next invocation reached the added checks
but failed because the pre-117 fixture was incorrectly reused and reseeded
existing Board IDs. A third preparation attempt retained a fixture-name collision left by the
original rollback test. All three failed preparations remain private evidence.
The final fixture snapshots current projections, uses a distinct upgrade fixture
name and keeps the original refusal assertions. The passing staged source matches
331 current migration/script files after line-ending normalization. The locked
solution build has zero warnings/errors and all 787 Domain tests pass.
The complete API and unfiltered persistence suites on schema 135 and the
exact-commit pipeline remain separate pending gates. PRD-01 remains open at
**34% estimated work remaining**; exact-commit immutable-image CI and complete
acceptance remain required.
