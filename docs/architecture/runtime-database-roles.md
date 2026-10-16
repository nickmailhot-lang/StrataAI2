# Runtime database roles

## Provisioning and connection guard agreement — 2026-10-16

The previous provisioning safeguard still accepted public-schema `CREATE` and
public function ownership even though the application's existing connection
guard returned false through actual API/Worker logins. Six native regression
cases reproduce this mismatch: direct and `PUBLIC` schema creation for each
role, plus each role owning a harmless function. Provisioning accepted the
configuration and rotated passwords; the application would still refuse it.

The [provisioner](../../db/provision-runtime-roles.sql) now rejects effective
public-schema creation and ownership of any public relation or function before
commit. Relation ownership includes sequences, which are outside ordinary
table/column grant inspection. It preserves the existing connection guard and
canonical grants. Operators must remove unintended schema grants or transfer
object ownership to the reviewed migration identity before reprovisioning.
Refusal rolls back credentials, ownership and grants; it does not automatically
change shared `PUBLIC` permissions or transfer objects.

The [maintained native provisioning test](../../tests/identity_role_provision_test.py)
passes all eight new cases (four schema grants, two functions, two sequences)
plus the previous nine relation/column `PUBLIC` probes, real Worker `SET ROLE`
escape, owned-table grant options and restricted identity-mail/password tests.
Actual API/Worker logins report unsafe before refusal and safe after remediation.
Fingerprints now include schema ACLs/ownership and function ownership. The test
passes in **73.582 seconds**; its wrapper takes 74.808 seconds, matches all 154
captured inputs and restores original Docker inventory. No existing database,
application host, provider, new image or large-load workload is used. Release
payload tests pass 47/47. The complete RLS/runtime-role driver passes separately
in 52.571 seconds with all 192 captured inputs matching. Its 114-table data
fingerprint is unchanged through ten real runtime UPDATE refusals and ten
injected-column guard refusals. Both disposable fixtures restore original
Docker inventory and remove their owned directories. Current immutable-image
CI and source-free release acceptance remain separate and outstanding.

The [fresh-cluster recovery rehearsal after this hardening](database-recovery.md#recovery-after-runtime-provisioning-hardening--2026-10-16)
also passes with the unchanged complete backup/restore driver: restored API/Worker
SCRAM authentication, safe role guards, tenant isolation and scoped durable queue
processing. This remains database recovery evidence; application/managed-provider
and current immutable-release acceptance are separate.

## Atomic effective-capability validation — 2026-10-16

[General role provisioning](../../db/provision-runtime-roles.sql) previously
reset direct API/Worker grants but still accepted extra effective privileges
from `PUBLIC`. Nine native probes reproduce acceptance of unintended credential
column reads, preview insertion/publication updates, private-history updates,
references, deletion, truncation, trigger and maintenance privileges. These are
permission probes in a disposable database; no destructive data operation or
maintenance workload is performed.

A further native Worker login shows why `NOINHERIT` is insufficient: ordinary
access to the credential column is false, but `SET ROLE` into a separately
granted peer makes it true. The relation-only repair still accepts that
configuration; the additional membership regression reproduces this escape.
An owned-table probe also isolates implicit owner grant options after ordinary
SELECT is revoked; provisioning refuses and preserves the original owner/ACLs.

Provisioning now validates its completed canonical direct grants before commit:

- API and Worker database identities must have no other database-role memberships.
- Effective table and column capabilities may not exceed their explicit canonical
  ACL entries. All public regular/partitioned/foreign tables, views and materialized
  views are inspected, including PostgreSQL17 maintenance capability.
- These dedicated runtime roles may not acquire grant options.
- Extra `PUBLIC` rights are not silently revoked from other principals. A redundant
  public schema-ledger read already allowed to both roles remains admissible.

Refusal aborts the same transaction that creates/configures roles, rotates
passwords and applies grants. The new probes compare fingerprints of both
managed identities/credentials, relation ownership, relation/column/function ACLs and memberships
before and after refusal. The operator must review and remove the unintended
grant or membership, then rerun canonical provisioning; recovery also passes.
Application Organization/Board memberships are unrelated to these PostgreSQL
database-role memberships and are unchanged.

The [required provisioning test](../../tests/identity_role_provision_test.py)
passes the nine `PUBLIC` refusals, actual-login peer escape refusal, implicit
owner grant-option refusal, benign
redundant grant and all previous password, retained-receipt, restricted mail
claim/completion and unsafe-mail-role checks. The maintained test method passes
in **52.497 seconds**; the independent wrapper takes 53.708 seconds and matches
all 154 captured database/fixture inputs. The separate complete RLS/runtime-role
driver passes in 52.879 seconds, with all 192 captured inputs matching. Its
114-table fingerprint stays unchanged through ten real runtime UPDATE refusals
and ten injected-column-grant guard refusals. Both wrappers restore original
Docker inventory; their owned databases/directories are removed. No existing
database, application host, provider, image build/pull/import or large-load test
is used. Expired-receipt batch fixtures remain functional retention checks.

This is a provisioning safeguard, not continuous monitoring of later DBA grants.
The existing per-connection security-function checks remain a separate boundary.
Externally provisioned equivalent roles need the same effective-capability review.
Historical bundles retain their original provisioner; current immutable-image,
source-free bundle and complete architecture acceptance remain required.

The [retained identity receipt audit](schema149-clock-inventory.md#retained-identity-receipt-facts--2026-10-16)
requires no UPDATE capability on any column of five global receipt tables for
either API or Worker. The disposable CI driver now checks that stronger
predicate; its native guard rejects all ten injected column-grant states.
The maintained provisioning test also verifies canonical reprovisioning removes all ten deliberately added
grants. Existing role SQL and expiry/subject boundaries remain unchanged; the
full RLS/role driver and updated provisioning test pass separately. These are
isolated database contracts; current image/deployment acceptance remains required.

The [fresh-cluster recovery contract](database-recovery.md#restored-runtime-identity-verification--2026-10-15)
now recreates the reviewed API/Worker identities before restoring archive ACLs,
then runs canonical provisioning on the verified target with fresh passwords.
Actual SCRAM sessions prove password refusal, safe role guards, complete ledger,
tenant/capability isolation and scoped queue publication/claim/acknowledgment.
It reproduces and repairs the missing-runtime-role failure in the first
fresh-cluster rehearsal. This is database recovery evidence; application-host,
managed-provider and full current immutable-release acceptance remain separate.

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
managed grants and role flags, then refuses unexpected memberships/effective
capabilities before commit. Operators must remedy shared `PUBLIC` grants and
role memberships rather than relying on direct-grant resets. Future migrations
must explicitly review any new runtime grants.

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
