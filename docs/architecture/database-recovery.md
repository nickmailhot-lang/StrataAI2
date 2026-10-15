# Encrypted PostgreSQL recovery verification

[ARCH-08](https://github.com/nickmailhot-lang/StrataAI2/issues/89) requires
encrypted backups and scheduled, recorded restore tests. A runnable release
bundle and a successful container restart do not establish those requirements.
Use this with [schema compatibility](schema-upgrades.md),
[runtime database roles](runtime-database-roles.md) and
[promotion and rollback](../release/promotion-and-rollback.md).

## Mandatory small CI rehearsal

The PostgreSQL source-quality job executes
[the recovery contract](../../scripts/ci/test-database-backup-restore.sh)
against its explicitly supplied disposable PostgreSQL service. It creates two
randomly named databases from `template0`, applies every production migration,
and seeds two Organizations with one Board/List/Card each and append-only audit
records. It never selects an existing deployment database. This is a small
functional recovery check; large-load testing remains post-MVP.

The contract streams a custom-format `pg_dump` directly into OpenSSL CMS
AES-256-GCM encryption using an ephemeral recipient certificate. Backup creation
does not write a plaintext dump. Wrong-recipient and modified-ciphertext cases
must fail before any restore, leaving the empty target unchanged. Decryption
can produce bytes before authentication completes, so the contract fully
authenticates into a private temporary file before invoking `pg_restore`.
Restoration uses one transaction and stops on errors.

The restored database must match the source's complete plain dump, including
data, schema and ACLs. Only random psql session restrict markers are excluded;
inline CHECK definitions are replaced by their PostgreSQL pretty catalog form,
including relation/type identity and constraint name. PostgreSQL reparses and
flattens redundant nested AND parentheses during restore, so raw dump bytes
are not a semantic schema comparison. All CHECK definitions remain compared.
Complete COPY rows are sorted because physical row order is not relational
state; row values and multiplicity remain compared, including Unicode content.
Deliberately changing ledger data, widening an ACL or dropping a CHECK must each
break the comparison; repairing each defect must recover equality.
Failed dump/catalog commands must not count as equality or as corruption
detection, even when they emit otherwise matching output. Separate subprocess
tests exercise these late failures inside Bash command substitution.

The restored catalog must also pass the existing complete tenant-schema guard.
Independent SQL checks require pgvector, unchanged audit history after a refused
update, rejection of a cross-Organization Card parent, missing-context read
isolation, one-Organization reads and permitted own-tenant writes under a
non-superuser/non-login role. A foreign write must fail with the specific RLS
error, rather than an unrelated missing privilege. These checks do not certify
every runtime API/Worker capability.

Fresh keys, encrypted archives, decrypted output and diagnostic SQL logs stay
in the owned private directory and are removed, along with both databases and
the temporary role. None is uploaded. The CI job log records the result; failure
blocks the source gate and image builds. Coverage guards reject omission,
replacement, conditional deferral, ignored failure and duplicate invocation.

## Executed local evidence

The complete maintained contract passes against a fresh, network-disabled
PostgreSQL 17/pgvector container with all 149 migrations and the two-Card
fixture. The final invocation takes 54.434 seconds, all 155 captured inputs
match, the full Docker inventory is restored and 723,656 bytes of owned
temporary files are removed. No application image is built, imported or
downloaded, and the existing local database is unused.

The first comparisons correctly stop on raw DDL-parentheses and physical-row
order differences. Scoped diagnostics establish the canonical forms above;
the final deliberate data/ACL/CHECK mutations prove that those differences
are still detected. A missing List-route write grant initially prevents the
fixture role's permitted write; supplying that explicit fixture permission
fixes the test without changing production permissions or RLS. Separately, a
late-failing dump regression first reproduces command-substitution error loss;
explicit status propagation fixes it. All nine recovery comparison tests and
226 CI coverage guards pass, as do shell syntax, workflow lint and 317 local
documentation links. Exact-commit hosted CI for this new gate remains required.
These results do not establish deployed backup operation or managed recovery.

## Deployment recovery remains an operator requirement

This CI contract is not a deployed backup scheduler or production backup tool.
Before routing production traffic, configure and record:

- An encrypted backup destination, access restrictions, retention and separately
  protected recovery keys. Pin and verify the intended recipient or managed-key
  identity; this fixture's one-day test key is unsuitable for deployment.
- Backup and restore-drill schedules matched to the approved RPO/RTO. CI runs on
  source changes do not establish backup freshness or an operating schedule.
- Recovery of PostgreSQL, object-storage versions, durable key rings and the
  external configuration/provider dependencies as a coordinated set. A logical
  PostgreSQL dump omits cluster-global roles; this rehearsal restores in one
  isolated cluster where its roles still exist. Cross-cluster recovery must
  restore/reprovision the reviewed ownership, grants and restricted logins.
- A restore into an isolated empty destination, schema/image compatibility and
  actual application/provider checks before allowing traffic or background
  deliveries. Recovering live data may repeat external effects; do not simply
  start Workers against an unreviewed restored snapshot.
- A non-secret recovery record with source/candidate revision, backup identity
  and protected location, creation/restore times, integrity/key checks, measured
  recovery window and outcome. Keep backup contents and keys out of CI artifacts.

Measured production RPO/RTO, managed-provider restoration, coordinated
object/key-ring recovery and scheduled production drills remain outstanding.
ARCH-08 and the full MVP deployment acceptance remain open.

## References

- [PostgreSQL custom dumps](https://www.postgresql.org/docs/17/app-pgdump.html)
- [PostgreSQL transactional restore and ownership/ACL options](https://www.postgresql.org/docs/17/app-pgrestore.html)
- [OpenSSL CMS encryption and AEAD modes](https://docs.openssl.org/3.5/man1/openssl-cms/)
