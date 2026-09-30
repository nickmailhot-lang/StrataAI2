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

Every runtime connection requires all eleven baseline migration versions. Missing
ledger permissions, missing ledger or incomplete baseline refuse database-backed
operations with a sanitized 503 and readiness failure. Additional forward migrations
are allowed only when they preserve this image's contract; incompatible changes
require a phased migration and an updated image compatibility requirement. Ledger
validation does not replace schema review or protect against manual column changes.

CI exercises clean bootstrap, eight-to-nine-to-ten-to-eleven upgrade, repeated execution, concurrent
runners and rollback of failed DDL/ledger changes. Exact release images test refusal
and recovery with an incomplete baseline.
