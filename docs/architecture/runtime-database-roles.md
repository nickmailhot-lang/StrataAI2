# Runtime database roles

Apply migrations with the initialization/migration account, then run
`scripts/provision-compose-runtime-roles.sh`. Supply distinct API and Worker
passwords of at least 20 characters. Compose passes these separately from the
initialization credential. Structured runtime credentials are assembled with
Npgsql's connection-string builder, including passwords containing delimiters.

The API receives explicit identity, routing and tenant-data permissions. It can
append audit events and identity-mail jobs, but cannot read the mail queue or
modify audit events. The organization Worker can read/update background jobs;
it cannot access identity tables or tenant business tables. Identity mail keeps
its separate restricted connection and column grants. Grant that role EXECUTE
on `public.runtime_database_role_is_safe()` after migration 009.

Every borrowed runtime connection validates the database security function.
Superuser, RLS bypass, role/database creation, replication, predefined privileged
role membership, public-schema creation and public-object ownership are rejected.
Readiness returns 503; API operations return a correlated, sanitized 503 problem.
Liveness remains available. Missing migration or guard permissions fail closed.
Restoring safe permissions restores readiness without restarting the service.

Hosted deployments may provision equivalent roles externally. Grant only the
permissions listed in `db/provision-runtime-roles.sql`, plus the guard function.
Never use the migration account as a runtime credential. Existing managed roles
must have no extra memberships, ownership or column grants; provisioning resets
table grants and role flags but operators must remove legacy column grants and
memberships. Future migrations must explicitly review any new runtime grants.

CI tests actual login sessions (not administrator sessions using SET ROLE),
service-specific denied access, privilege escalation and recovery, and the exact
release images' readiness and safe API failure behavior.

## Required current membership authority migration

Both runtime connections require the named
`113_invitation_recipient_membership_authority` ledger entry. The current real
PostgreSQL readiness contract accepts the complete ledger, refuses temporarily
hidden required entries using separate restricted API/Worker logins, and recovers
after each restoration, including 113. The owned fixture database is removed.
The migration replaces existing publication/view definitions and introduces no
new runtime table grants. See
[migration and native evidence](browser-recovery-ci.md#current-board-membership-authority-and-actual-recipient-interruption).
