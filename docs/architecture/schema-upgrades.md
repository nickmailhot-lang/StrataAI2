# Schema upgrades and compatibility

The deployment and CI migration helpers stream ordered SQL through one database
session. A session advisory lock serializes runners, including the first bootstrap.
The migration ledger determines which scripts to skip. Each migration owns its
transaction, including its ledger insertion: a failed transaction rolls back both
DDL and its version. Rerunning completes pending migrations without recreating
existing tables. Never edit applied migration files; introduce a new numbered
migration. This ledger does not authenticate historical file contents or detect
manual database modifications.

The release includes `migration-stream.sh` beside `apply-migrations.sh`. Run from
the release directory, migrate with the administrator, provision restricted roles,
and check readiness before routing traffic. Runtime processes never migrate schema.

The current API/general Worker connection factory requires all 149 named
migration versions, from `001_foundation` through
`149_card_reference_parent_indexes`. The authoritative list is in
[PostgresConnectionFactory](../../src/StrataAI.Infrastructure/Persistence/PostgresConnectionFactory.cs).
Missing
ledger permissions, missing ledger or incomplete baseline refuse database-backed
operations with a sanitized 503 and readiness failure. Additional forward migrations
are allowed only when they preserve this image's contract; incompatible changes
require a phased migration and an updated image compatibility requirement. Ledger
validation does not replace schema review or protect against manual column changes.

CI exercises clean bootstrap and sequential upgrades from eight through 149 versions,
populated membership/invitation integrity, repeated execution, concurrent runners
and rollback of failed DDL/ledger changes. Exact release images test refusal
and recovery with an incomplete baseline.

After migration, the catalog guard classifies every non-extension application
table in `public`. Explicitly classified global identity/ledger, actor-private
and recipient tables retain their separate identity/service contracts described
below. All remaining Organization-owned tables require a
non-null UUID `tenant_id`, or the root Organization's non-null UUID `id`, enabled
and forced RLS, and an explicit policy. The existing shared append-only
`audit_events` table also stores global identity events: its UUID Organization key
may be null for those global records, while its key type, forced RLS and policy
remain checked. This does not exempt tenant-only tables or routing tables.
Adding a global table requires deliberate
classification and its own security review; classification grants no privileges.
This guard checks catalog structure, not the correctness of policy predicates.
Cross-tenant query/write, missing-context and restricted-role fixtures remain
required to prove actual isolation behavior.

Actor-private global tables separately require a non-null UUID actor, forced
RLS and explicit subject read/write policy. Recipient streams and authority
revisions require their normalized recipient key, forced RLS and the specific
recipient-only read policy. These are reviewed identity scopes, not exemptions
for tenant-owned tables; the catalog guard checks their distinct contracts too.

The required PostgreSQL job runs deliberate catalog failures for nullable or
missing isolation keys, disabled or unforced RLS and absent policies. Each failure
must originate from the guard, roll back its disposable DDL and leave the valid
migrated catalog intact. [Hosted PostgreSQL job 116432558103](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/38732211465/job/116432558103)
at `ff1249f4` passes the catalog/refusal/rollback runner, RLS/pgvector, restricted
routing context/reset and append-only audit checks. Its catalog scripts and
migrations are unchanged through the server-version repair below. This resolves
the old first-Linux-execution note; it does not certify current immutable images.

## Minimum server version admission — 2026-10-15

ARCH-04-FR-001 requires PostgreSQL 17 or newer, including an externally configured
database. Compose selected 17 but the runtime factory previously admitted older
servers when role/schema checks succeeded. Both normal and readiness connections
now require the driver's reported major version to be at least 17 before running
the application role/schema queries. This uses handshake metadata without an
additional query. A refused connection is disposed and uses the existing
sanitized schema-incompatibility error/readiness path, without version details
or a Demo fallback. Existing transport, role and 149-entry ledger checks remain.

The [eight wire-handshake cases](../../tests/StrataAI.Api.Tests/PostgresServerVersionTests.cs)
use actual Npgsql against a bounded loopback protocol peer for both connection
paths. Before the repair all four advertised-15/16 refusal cases fail because
connections are admitted; the four advertised-17/18 cases pass. Afterward all
eight pass, with no application guard queries on refused servers and both
role/schema queries retained on accepted ones. The peer explicitly uses basic
built-in type loading and synthetic SQL answers; it does not certify actual
PostgreSQL 15/16/18 engines or change production type-loading defaults.

The complete solution builds with zero warnings/errors, and 14 existing HTTP
error-sanitization/diagnostic cases pass. The maintained bounded attachment/
Worker persistence invocation passes against actual **PostgreSQL 17.11** with
all **149 migrations**, normal type loading and restricted API/Worker logins in
**21.3 seconds**. All **820** frozen source/binary inputs match; owned cleanup
restores Docker inventory and removes **724,100** temporary bytes. No image is
built/downloaded/imported, original database used or large-load test run.
This is supporting current-assembly evidence in the retained Linux runtime;
current exact-SHA immutable CI and full ARCH-04 acceptance/DoD remain required.

## Runtime migration readiness

The current API/general Worker connections require every named migration through
`149_card_reference_parent_indexes`. The readiness query checks for missing
required ledger entries directly, avoiding a separately maintained numeric total.
Extra later migrations do not substitute for a missing required entry.

CI run `37407613926` exposed the prior mismatch: the required list contained
81 migrations while the connection check expected 77, so a fully migrated database
was rejected before restricted search traversal. The corrected check preserves
fail-closed handling for unavailable ledgers and missing privileges.
`RuntimeSchemaReadinessContract` runs in the mandatory PostgreSQL contract job
using both restricted API and Worker logins. It checks complete-ledger admission,
then individually hides every applied ledger entry, requires both logins to
refuse it, and checks recovery after each entry is restored. Fixture mutations are confined to the
disposable CI database and restored in `finally`. The PostgreSQL job for
[`c053b9d3`](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/38667987511/job/116204261658)
passes the current 149-entry refusal/recovery contract. That is scoped database
evidence; complete current release CI remains required.

## Complete staged upgrade coverage through 149

The [upgrade runner](../../scripts/ci/test-migration-runner.sh) previously
stopped at production migration 146 before its synthetic serialization/failure
fixtures. A clean 149-migration install did not prove the missing populated
upgrade paths. The runner now applies and repeats all three later migrations:

- 147 preserves an existing List with active/archived Cards, keeps ordinary
  parent checks immediate, and permits explicit transactional deferral while
  moving the complete List/Card parent graph.
- 148 preserves configuration/history/receipt JSON, releases a former intake
  List's live Board parent while retaining immutable source-time attribution,
  and still refuses moving the current intake List or deleting the historical
  List identity.
- 149 builds the valid, correctly ordered Card-label/member parent indexes
  over existing associations without changing their versions or JSON.

Before synthetic runner fixtures, the applied ledger's complete ordered names
must equal the production migration-file manifest. A future migration without
its upgrade stage therefore fails CI rather than hiding behind a numeric total
or a synthetic version. Serialization, rollback and missing-ledger fixtures use
version names after the actual manifest's latest version. Production migration
files are unchanged. These small fixtures do not establish capacity, previous
application-image rollback compatibility or managed-provider readiness.

The complete maintained runner passes in fresh, network-disabled PostgreSQL
with 253 matching captured SQL/runner inputs. An initial seed lacking the
destination Board membership is correctly rejected by the existing Card-member
FK; supplying that membership fixes the fixture without weakening production
constraints. Final staged upgrades, repeated application, serialization and
failure rollback all pass. Docker inventory is restored and 1,253,414 bytes
of temporary snapshot files are removed; the original database is unused.
Separate executions of the exact coverage guard accept the complete manifest
and reject the old 146-entry ledger, a same-count substituted migration name,
and a future file without its upgrade stage. These guard fixtures use synthetic
ledger output; the complete upgrade execution uses real PostgreSQL. Current
exact-commit CI remains required.


## Membership authority forward upgrade

Migration 113 retains current canonical history and adds the actual Board
membership producer names to recipient invalidation. The clean/repeat/forward
upgrade runner executes its three-family routing fixture, including restricted
Worker retry, then retains concurrent-runner serialization, injected migration
rollback and unrecorded-migration rejection. All pass locally on an owned
PostgreSQL17/pgvector database. The corresponding mandatory PostgreSQL CI step
also runs the routing fixture directly. Current restricted API/Worker readiness
checks pass, including refusal/recovery when 113 is hidden. See
[execution scope](browser-recovery-ci.md#current-board-membership-authority-and-actual-recipient-interruption).

## Issuer job clock forward upgrade

Migration 115 refuses historical RUNNING issuer-authority jobs because a lease
deadline does not prove their latest mutation time. Finish or exhaust those jobs
with the existing Worker, pause claiming, and retry the upgrade. Never clear a
lease or manufacture terminal state to bypass this check. The upgrade locks the
job table; refusal rolls back the schema, ledger and historical state. Creation
and recorded terminal facts supply the backfill, with no migration-time substitute.
See the [clock audit and executed verification](invitation-issuer-clock-audit.md#managed-job-clock-and-historical-upgrade)
for preserved rows, transactional clocks, current readiness and remaining release
scope. The new runtime requires the named 115 entry before admitting connections.

## Recipient authority page clocks

Migration 116 preserves the owning durable job's recorded creation time and the
page's first recorded completion time. The clock backfill and restored history
guard commit together under an exclusive page-table lock. Missing, non-finite
or contradictory clock facts refuse the upgrade; migration time is not substituted.
The new runtime also requires the named 116 entry. See the
[page lifecycle audit](invitation-recipient-authority.md#page-lifecycle-clocks-and-remaining-counter-audit)
for source ownership, exact retry/rollback checks and the separate unresolved
recipient-counter history.

## Canonical entity route clocks

Migration 117 projects persisted canonical creation/update timestamps into
Board/List/Card/Label routes. Source tables and route tables are locked for the
historical backfill; a missing or mismatched source refuses the transaction
without publishing columns or ledger 117. Normal canonical synchronization
triggers maintain the new clocks through a tenant-scoped BEFORE trigger, with
no additional runtime grants. API and Worker require the named 117 ledger entry.
The [route-clock record](entity-route-clocks.md) documents the full passing
local upgrade/refusal/rollback gate, restricted readiness contracts and current
immutable-image/deployed verification boundary.

Migration 118 preserves the existing discovery-only write authorization error
(42501) when the BEFORE clock guard cannot see a canonical source without its
owning tenant context. The source-clock guard remains 23514 for actual owning
or administrative missing-source faults. The old migration is retained; the
forward repair changes no grants, routing reads or historical clocks. API and
Worker additionally require ledger 118. The original real restricted routing
security gate failed before this repair and passes afterward; all four clocked
route types pass the extended mandatory denial checks.

Migration 119 adds invitation-route clocks from the owning invitation's
persisted creation/update facts, matching tenant, identity and token hash.
The publication Organization-name snapshot is retained. Orphaned or invalid
historical sources refuse the upgrade atomically; the full local migration
gate proves unchanged history and absent columns/ledger on refusal, then
successful repair/repeat. API and Worker require ledger 119. See the
[invitation-route clock record](entity-route-clocks.md#invitation-routing-clocks)
for writer ownership, runtime metadata checks and remaining release scope.

Migration 120 adds Organization-access route creation clocks from canonical
memberships while retaining their update clocks and reciprocal state foreign
keys. Its backfill and guard do not invent historical timestamps, change
discovery authority or add runtime grants. API and Worker require ledger 120.
The [membership route record](entity-route-clocks.md#organization-membership-access-routing)
documents full historical body checks, role/status propagation, Owner creation
precision, no-op/tamper/deferred-integrity verification and release limits.

Migration 121 adds generated update clocks to Organization metadata and
lifecycle events from their retained creation/first-publication times.
It preserves history guards, RLS, privileges and leased delivery fences;
API and Worker require ledger 121. See the [event clock record](organization-event-clocks.md)
for populated-upgrade, replay/rollback/tamper checks and remaining release scope.

Migration 122 adds source-derived Organization metadata counter clocks and
refuses missing/gapped journal history. Its event refresh and deferred guard
preserve existing allocators while requiring retained source facts before
commit. API and Worker require ledger 122. The [stream clock record](organization-metadata-stream-clocks.md)
documents provenance, bounded source lookups and verification scope.

Migration 123 adds source-derived recipient publication counter clocks,
refuses incomplete journal provenance, and preserves the protected recipient
lookup RLS. Its event refresh/deferred final-state guard adds no runtime write
grant. API/Worker require ledger 123. See the [recipient stream clock record](invitation-recipient-stream-clocks.md)
for populated upgrade, tamper/refusal, canonical revision and runtime scope.

Migration 124 adds Organization deletion checkpoint creation time from
the retained accepted request, preserving existing update history and refusing
contradictory legacy clocks. API/Worker readiness requires its ledger. See the
[checkpoint clock record](organization-deletion-progress-clocks.md) for source
ownership, guards and completed whole migration/restricted deletion verification.

Migration 125 resolves a Card's unique identity before scope filtering in
the canonical route clock guard, retaining caller RLS and the prior admission
refusals. API/Worker readiness requires ledger 125. See the
[lookup performance record](card-route-clock-lookup.md) for observed plans and
completed full migration/deletion/routing verification and remaining release scope.

Migration 126 derives notification counter clocks from its complete private
journal and refuses incomplete or nonfinite source history. API/Worker readiness
requires ledger 126. The [notification stream clock record](notification-stream-clocks.md)
documents completed whole migration/refusal, nine-case native notification and
126-ledger readiness/security checks, plus pending watch/ordering, capacity and
immutable-image release verification.

Migration 127 validates retained notification journal attribution, exact parent
timestamps and counter history, then installs an UPDATE/DELETE history guard.
API/Worker readiness requires ledger 127. The
[history integrity record](notification-event-history.md) links completed full
migration/refusal, all-127-ledger readiness and security verification to pending
native producer/consumer and current build-once release acceptance.

Migration 142 introduces private record-retention policy headers, immutable
revisions and actor-bound retry receipts with forced RLS. Deferred source guards
require complete header/history/receipt/audit provenance at commit; namespaces
and historical attribution remain retained. No default policy is invented.
API/Worker readiness requires ledger 142; API receives SELECT only and Worker
receives no policy access. See [records retention](records-retention.md) for
schema integrity, full migration-runner and compiled Production readiness
evidence, plus pending policy commands, holds and release-image acceptance.

Retention policy HTTP activation now uses canonical API SELECT/INSERT on all
three policy tables and UPDATE on the header only; history/receipts remain
immutable and the Worker has no policy access. Apply current
`db/provision-runtime-roles.sql` during upgrade; migration 142 alone does not
install runtime grants. Maintained capability and HTTP fixtures check these
permissions and owning command behavior. See [records retention](records-retention.md)
for locally executed Production checks and pending immutable-image CI acceptance.
