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

Every runtime connection requires all 27 current baseline migration versions,
from `001_foundation` through `027_routing_isolation`. Missing
ledger permissions, missing ledger or incomplete baseline refuse database-backed
operations with a sanitized 503 and readiness failure. Additional forward migrations
are allowed only when they preserve this image's contract; incompatible changes
require a phased migration and an updated image compatibility requirement. Ledger
validation does not replace schema review or protect against manual column changes.

CI exercises clean bootstrap, sequential upgrades from eight through 27 versions,
populated membership/invitation integrity, repeated execution, concurrent runners
and rollback of failed DDL/ledger changes. Exact release images test refusal
and recovery with an incomplete baseline.

After migration, the catalog guard classifies every non-extension application
table in `public`. Explicitly listed global identity/ledger tables retain their
separate subject/service authorization contracts. All other tables require a
non-null UUID `tenant_id`, or the root Organization's non-null UUID `id`, enabled
and forced RLS, and an explicit policy. Adding a global table requires deliberate
classification and its own security review; classification grants no privileges.
This guard checks catalog structure, not the correctness of policy predicates.
Cross-tenant query/write, missing-context and restricted-role fixtures remain
required to prove actual isolation behavior.

The required PostgreSQL job runs deliberate catalog failures for nullable or
missing isolation keys, disabled or unforced RLS and absent policies. Each failure
must originate from the guard, roll back its disposable DDL and leave the valid
migrated catalog intact. Local shell syntax/diff checks pass; first Linux execution
of this new catalog guard is still required before treating it as release evidence.
