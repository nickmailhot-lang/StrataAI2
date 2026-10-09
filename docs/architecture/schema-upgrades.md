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

Every runtime connection requires all 29 current baseline migration versions,
from `001_foundation` through `029_label_routing`. Missing
ledger permissions, missing ledger or incomplete baseline refuse database-backed
operations with a sanitized 503 and readiness failure. Additional forward migrations
are allowed only when they preserve this image's contract; incompatible changes
require a phased migration and an updated image compatibility requirement. Ledger
validation does not replace schema review or protect against manual column changes.

CI exercises clean bootstrap, sequential upgrades from eight through 29 versions,
populated membership/invitation integrity, repeated execution, concurrent runners
and rollback of failed DDL/ledger changes. Exact release images test refusal
and recovery with an incomplete baseline.

After migration, the catalog guard classifies every non-extension application
table in `public`. Explicitly listed global identity/ledger tables retain their
separate subject/service authorization contracts. All other tables require a
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

The required PostgreSQL job runs deliberate catalog failures for nullable or
missing isolation keys, disabled or unforced RLS and absent policies. Each failure
must originate from the guard, roll back its disposable DDL and leave the valid
migrated catalog intact. Local shell syntax/diff checks pass; first Linux execution
of this new catalog guard is still required before treating it as release evidence.

## Runtime migration readiness

Production connections require every named migration through
`116_invitation_authority_page_clocks`. The readiness query checks for missing
required ledger entries directly, avoiding a separately maintained numeric total.
Extra later migrations do not substitute for a missing required entry.

CI run `37407613926` exposed the prior mismatch: the required list contained
81 migrations while the connection check expected 77, so a fully migrated database
was rejected before restricted search traversal. The corrected check preserves
fail-closed handling for unavailable ledgers and missing privileges.
`RuntimeSchemaReadinessContract` runs in the mandatory PostgreSQL contract job
using both restricted API and Worker logins. It checks complete-ledger admission, refusal when
foundation or navigation entries are temporarily hidden by the fixture admin,
and recovery after each entry is restored. Fixture mutations are confined to the
disposable CI database and restored in `finally`. Compilation passed with zero
warnings/errors; native execution of this repair remains pending CI.


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
