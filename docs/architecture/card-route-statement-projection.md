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
