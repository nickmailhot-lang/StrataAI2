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
from its explicitly supplied disposable PostgreSQL service into a freshly
initialized, network-isolated PostgreSQL container. The target uses the source's
exact cached provider image ID (`--pull never`) and must have a different
`pg_control_system()` system identifier. It creates randomly named source and
target databases from `template0` in their respective clusters, applies every production migration,
and seeds two Organizations with one Board/List/Card each and append-only audit
records. It never selects an existing deployment database. This is a small
functional recovery check; large-load testing remains post-MVP.

The target does not inherit source roles. An authenticated restore without the
required fixture role must fail, leaving the complete empty-target fingerprint
unchanged. Only then does the contract recreate the reviewed non-login role,
explicitly verify that it lacks superuser, database/role creation, login,
replication and RLS-bypass powers, and restore again. It never copies global
superusers or password hashes. Target readiness requires the final TCP listener,
not the image's temporary initialization server. Owned target data is temporary
and its container/volumes are removed on exit.

The reviewed API and Worker identities are also created as restricted non-login
roles before restoration: CI already has them when migrations conditionally
grant capabilities, so archive ACLs can reference them. After the restored
schema/data/ACL and security checks pass, only the target runs the canonical
role-provisioning SQL with fresh distinct passwords. Actual SCRAM-authenticated
loopback TCP sessions must reject incorrect passwords and accept each runtime
login, pass the role guard and read the complete migration ledger. SQL exercises
API tenant reads/writes and Worker capability separation, then commits two small
API jobs and acknowledges only the scoped job through a separate Worker session.
The source fingerprint must remain unchanged after these target operations.

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

## Hosted exact-commit recovery evidence — 2026-10-15

The initial CI-only rehearsal executes and succeeds in
[PostgreSQL job 116251534368](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/38680420583/job/116251534368)
for exact commit `bb3cd4fc19a4edb3df035bc255a21973c772c54e`.
Its actual completion log confirms complete migrated schema/data/ACL comparison,
five deliberate corruption/refusal probes, pgvector, restricted tenant reads/
writes, foreign keys and append-only audit protections. The entire run then
finishes successfully across all 16 jobs, including the original-image security,
integration and source-free release gates. The
[candidate record](operational-mvp-milestone.md#current-status) links its retained
bundle and identifies the independent metadata/security audit and its limits.

This resolves pending hosted execution of the initial rehearsal. The candidate
does not contain the later operator backup or restore commands; their later
local results below require their own complete exact-commit CI. It does not prove
scheduled deployment backups, alert delivery, object/key-ring recovery or actual
production RPO/RTO. All 27 large-capacity phases remain skipped; no new load test
is run for this audit.

## Executed local evidence

The initial CI-only contract at `bb3cd4fc` passes against a fresh, network-disabled
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
documentation links. Hosted execution of this initial gate is now recorded above.
These results do not establish deployed backup operation or managed recovery.

The subsequent operator-command follow-up uses the actual
`scripts/backup-postgres.sh` in the complete maintained rehearsal. It passes in
48.043 seconds with all 156 captured inputs matching, full Docker inventory
restored and 726,137 bytes of owned temporary files removed. All 149 migrations,
two-Card fixture, five corruption/refusal probes and restored security checks
remain. The existing local database is unused; no application image is built,
imported or downloaded.

All eight operator-command tests pass, covering exact encrypted-stream recovery,
literal Unicode/space-containing names, invalid identity/certificate/connection
URI refusals, a recipient certificate replaced during encryption, failed or
partial dumps/encryption, existing output and file/directory publication races.
The command encrypts with a validated private-directory certificate snapshot.
All 33 release-artifact tests and 228 CI coverage guards pass, including omission
of the operator command or its pre-build refusal tests. Shell/workflow lint and
18 local documentation links pass. The command is required in new release
payloads; older bundles do not gain it. New exact-commit hosted CI remains
required, and this evidence does not establish a deployed backup schedule.

## Operator restore path

The [restore command](../release/README.md#restore-into-an-isolated-empty-docker-database)
is required in new release payloads alongside backup. It requires an explicit
running container, empty target/database user, recipient certificate/private
key, protected temporary directory and expected SHA-256 from the trusted backup
record. Archive, certificate and key are snapshotted privately; the exact archive
snapshot is checked before full GCM authentication. No Docker/SQL command runs
before authentication succeeds. The private-key passphrase is environment-only
for OpenSSL and is removed from the environment before Docker calls.

The format check precedes target inspection. Existing relations, routines,
types, custom schemas or nondefault extensions refuse restoration. The actual
restore is one transaction with error termination, without cleaning, dropping
or creating the database. Operators must keep the target isolated throughout
recovery and later validate the complete application/provider state. Private
temporary material is removed on failure and success. Encrypted recovery
archives/plain dumps are excluded from release assets, regardless of checksums.

The maintained real PostgreSQL rehearsal now invokes both operator commands.
It adds trusted-digest refusal and unchanged-target checks for existing schema,
function, type, pgvector extension and populated database. Together with the
original wrong-key/tamper and data/ACL/CHECK probes, eleven deliberate refusal/
corruption cases must pass. This expands functional recovery proof, without
claiming managed-provider or deployed disaster-recovery acceptance.

The complete rehearsal passes locally in 61.595 seconds, with all 157 captured
inputs matching, full Docker inventory restored and 731,599 owned temporary
bytes removed. All 149 migrations and the original two-Card security fixture
remain; the existing local database is unused and no application image is
built/imported/downloaded. This is a small functional recovery check.

All ten restore-command tests pass with real OpenSSL and synthetic Docker
transport, including expired historical certificates, encrypted private keys,
passphrase isolation, full-authentication ordering, archive replacement,
format/query/restore failures and nonempty-target refusal. The eight backup
tests, nine comparison tests, 35 release-artifact tests and 230 CI coverage
guards pass. Two new release-artifact regressions first accept private `.cms`
and `.dump` payloads with regenerated checksums; both are now refused. Shell/
workflow lint and 19 local documentation links pass. Hosted execution of this
restore implementation is recorded in the subsequent follow-up below.

The earlier operator-backup revision
`c822186c25882117fb0dcfcdd4ad2819be9a8313` now passes the hosted
[PostgreSQL job](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/38682594588/job/116252397759).
Its retained build-metadata artifact binds that run to the exact revision. The
completed job executes the real backup command and passes the complete
schema/data/ACL comparison, five deliberate corruption/refusal cases, pgvector,
restricted tenant operations, parent foreign keys and append-only audit checks.
The populated upgrade runner and all three TLS certificate cases also pass.
This job predates the operator restore command and its expanded eleven-case
rehearsal; it does not prove their hosted execution or a complete release.

The operator-restore revision
`ebfbfe1b89f9ced4bbb942aba2ea4ab9bf81bcea` subsequently passes its hosted
[PostgreSQL job](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/38685376116/job/116266313872).
The retained metadata artifact binds that job's run to the exact revision.
Both actual operator commands execute, with all eleven refusal/corruption cases,
full migrated schema/data/ACL comparison, pgvector, restricted tenant reads and
writes, cross-tenant parent constraints and append-only audit protections.
Populated upgrades and all three database TLS certificate cases also pass.
The backup/restore commands, maintained rehearsal and all production migrations
remain unchanged through `e5570793`; this is scoped hosted implementation
evidence, not a complete passing release or scheduled production recovery.

## Source-issued protected activity cursor after restoration — 2026-10-15

The [HTTPS recovery contract](../../scripts/ci/https-database-recovery.inc.sh)
now carries an actual API-issued encrypted activity continuation across the
fresh-cluster restore. A small fixture adds 51 already-published synthetic
history rows to the one-Card Board, crossing the fixed 50-item page boundary.
It records the first page, original token and nonempty disjoint continuation
before stopping the source. The saved Secure session and fresh sign-in on the
restored target must replay that same token and return the exact original
continuation. Rebinding it to the Card must return `invalid_activity_cursor`.
This verifies actual use of the retained key ring, rather than only its volume
name. It does not recover keys from an off-host backup.

The [eight controlled refusal/continuation cases](../../tests/https-recovery-cursor.test.mjs)
reject missing cursors, incomplete first pages, empty/overlapping continuations,
changed restored data, accepted rebinding and unrelated error codes. All **107**
selected cursor, HTTPS realtime and startup/cleanup cases pass, along with
**247** CI coverage guards, workflow lint and Bash syntax. The new cases are
mandatory before image builds; the existing assembled HTTPS gate executes the
actual restored-host check.

The complete final local rehearsal passes in **210.904 seconds**. Source-issued
cursor recovery, actual target and original Worker/WSS edits, source fingerprint,
image identity and both projects' cleanup all pass. All **774** frozen inputs
match; Docker inventory is restored and **798,596** owned temporary bytes are
removed. It uses the archive-verified `24f56246` application images with current
read-only edge settings and maintained recovery scripts, without building,
downloading or importing images or using the original database.

The initial invocation reaches successful cursor recovery but fails the existing
live-stream check because the synthetic history was left Pending without jobs.
The fixture now explicitly represents already-published history. The real
Worker/edit assertions remain unchanged. Synthetic history does not prove 51
command publications, capacity or performance; no large-load test runs.
Current immutable-release CI, managed providers, off-host keys/objects and
scheduled production recovery remain required. ARCH-08 and the MVP stay open.

## HTTPS application use after fresh-cluster restoration — 2026-10-15

The mandatory assembled HTTPS gate now calls
[the application recovery contract](../../scripts/ci/https-database-recovery.inc.sh)
before its original live-Board checks. It stops the source API/Worker, encrypts
the actual single-account/Organization/Board/List/Card database through the
bundled backup command, and authenticates the archive into a fresh cluster in
a separate owned Compose project. Cluster identifiers must differ; PostgreSQL
and application image identities must match their originals. Restricted
non-login identities precede restored ACLs, and canonical target provisioning
activates fresh API/Worker credentials.

The target reuses only the source fixture's owned durable activity key ring.
This verifies database recovery with that dependency retained, not restoration
of an off-host key backup. HTTPS uses the existing fixture certificate through
a new private localhost listener. The API and Worker must report the exact
Production build identity; Worker readiness must also be Production/ready.
The original Secure session and an independent fresh sign-in must read the exact
saved graph. The actual target Worker then delivers an authorized edit through
WSS, with persisted cursor recovery, anonymous/origin refusal, content-free
frames and logout withdrawal, using the existing live-Board contract.

The stopped source's complete normalized dump, ACLs and separately catalogued
CHECK definitions must remain unchanged. The target is removed before the source
hosts restart; its external key ring remains owned by the source project. The
original source session must still read the original graph. Final cleanup checks
both projects' owned containers/volumes/networks, including error paths, then
removes private archive/key/plaintext work. Seventeen new cleanup subprocess
cases cover both project lifecycles and preserve earlier failures; all **70**
startup/cleanup cases pass.

The first complete local invocation passes in 207.801 seconds with the retained,
archive-verified `24f562464f706884e4a27577b61c65ff385b4263` application images and
current Nginx/header configuration mounted read-only. The final invocation passes
in **206.115 seconds**, including explicit Worker identity/readiness and complete
source CHECK fingerprint probes. All **771** frozen inputs match; both owned
projects are removed, 795,880 temporary host bytes are deleted and the original
Docker inventory is restored. All **246** CI coverage guards also pass.
This uses no application source build or new image download/import. The fixture
enables self-registration and disables verification/mail for its synthetic user;
it exercises actual HTTPS/Secure cookies, not the earlier localhost HTTP cookie
override. It does not prove current-main immutable images, every restored
capability, managed mail/storage/scanner, off-host key/object recovery, public
deployment or production RPO/RTO. ARCH-08 and the MVP remain open; large-load
testing remains deferred.

## Application use after restoration

A separate small local check runs both operator commands from
`763257e7d46433f9a76155db975a74d05eea430f` with the archive-verified, previously
retained application images at `24f562464f706884e4a27577b61c65ff385b4263`.
It creates a fresh isolated PostgreSQL cluster, applies all 149 migrations and
the restricted API/Worker roles, then creates one synthetic account and one
Organization/Board/List/Card hierarchy through the real web/API endpoints.

With the API and Worker stopped, the backup command encrypts that source, and
the restore command authenticates it into a separate empty database in the
same cluster. Both hosts are recreated with the target database configured;
their actual runtime settings, changed container IDs and unchanged original
image identities are checked. The existing session and a fresh sign-in both
read the exact saved Board snapshot. A new Unicode Card edit increments its
version and preserves its creation time. Independent database queries show
the edit and new audit facts only in the target, with the source Card and audit
count unchanged. All 395 captured script/schema/configuration inputs match.
The owned containers, volumes, network, credentials and encrypted archive are
removed, restoring the full original Docker inventory and removing 1,150,931
temporary bytes. The existing local database is never used; no image is built,
downloaded or imported.

This fixture enables self-registration and disables verification/mail delivery
for its synthetic account. Its local HTTP client explicitly carries its own
Secure session cookie on localhost; the application cookie stays Secure.
It does not test deployment HTTPS, current-main images, cross-cluster roles,
off-host recovery keys, attachments, external deliveries or production RPO/RTO.
Initial private-checker failures (an incorrect route, ordinary Python cookie
handling on HTTP and creation/snapshot timestamp precision) stop before any
restore; each fixture is cleaned up. The final invocation passes the full
restore and application checks above. This is supporting recovery evidence,
not a substitute for current exact-commit CI or managed deployment acceptance.

## Restored runtime identity verification — 2026-10-15

A representative source cluster containing the API/Worker identities reproduces
a failure in the first fresh-cluster rehearsal: migrations grant conditional
capabilities, but the target initially recreated only the fixture reader. The
correctly authenticated archive therefore fails during restore. The reproduced
failure exits 1; cleanup restores Docker inventory. The earlier 70.571-second
capture below did not include those preexisting source runtime roles.

The repair creates reviewed API/Worker identities as restricted non-login roles
before archive ACLs are restored. It then activates them on the target through
the unchanged canonical provisioning SQL with fresh credentials. It changes no
preexisting source role/password and copies no global password hashes.

The final two-Card, 149-migration rehearsal passes in **76.258 seconds**, including
initialization/cleanup. Both actual SCRAM logins accept their credentials and
refuse incorrect passwords, pass the runtime role guard and read the complete
ledger. The API refuses missing-context/foreign-tenant disclosure and writes,
allows its own tenant write, and cannot acquire Worker scan recovery. The Worker
cannot directly read Cards/private attachment metadata, sees only its tenant's
queue, claims the committed API job, refuses another process's acknowledgment,
and commits its own acknowledgment. The second tenant's job remains Pending at
attempt zero; the source fingerprint remains unchanged.

The complete schema/data/ACL, twelve archive/refusal/corruption and original
tenant/security checks also pass. All **159** frozen inputs match; both owned
containers and 756,394 temporary host bytes are removed, restoring the original
Docker inventory. The [SQL contract](../../scripts/ci/database-backup-runtime-verify.sql)
uses real restricted sessions, not administrator `SET ROLE`. It does not execute
the application connection factory/HTTP hosts, all runtime capabilities, managed
providers or coordinated object/key recovery. Current immutable CI and scheduled
production recovery/RPO/RTO remain required. No large-load test runs; ARCH-08 and
the MVP remain open.

## Fresh-cluster recovery verification — 2026-10-15

The updated mandatory contract passes against two owned PostgreSQL clusters with
distinct system identifiers and the same cached provider image. Its two-Card,
two-Organization source includes all **149 migrations**. The target initially
lacks the fixture role: restoration must fail and its complete empty fingerprint
must remain unchanged. Recreating only the reviewed least-privilege non-login
identity permits the actual encrypted operator restore to succeed.

Complete schema/data/ACL comparison, all **twelve** deliberate refusal/corruption
checks, pgvector, restricted tenant reads/writes, cross-tenant FK refusal and
append-only audit protections pass. The final owned rehearsal completes in
**70.571 seconds**, including initialization and cleanup; nine comparison/parser
subprocess tests also pass. All **157** captured source inputs match. Both owned
containers and temporary databases/roles/files are removed, including 734,290
temporary host bytes, and the original Docker inventory is restored. No original
database is used, no application image is rebuilt and no load test runs.

This replaces the same-cluster assumption for the current mandatory rehearsal;
earlier candidate/local records above retain their original scope. It does not
prove full production API/Worker role provisioning after cross-cluster restore,
deployed application startup, managed-provider restore, coordinated object/key
recovery or scheduled production RPO/RTO. Current immutable CI remains required.
ARCH-08 and the operational MVP remain open.

## Deployment recovery remains an operator requirement

The [Docker operator backup command](../release/README.md#encrypted-docker-postgresql-backups)
is included in the release payload and exercised by this CI contract. It streams
the full custom dump directly into CMS AES-256-GCM, checks the recipient
certificate before database access, never replaces an existing archive and
cleans owned temporary files on failure. It requires the deployment's explicit
database login and operator-selected running container/database. Its local
authentication and Docker scope do not implement managed database backups.

Neither this command nor the CI contract is a deployed backup scheduler.
Before routing production traffic, configure and record:

- An encrypted backup destination, access restrictions, retention and separately
  protected recovery keys. Pin and verify the intended recipient or managed-key
  identity; this fixture's one-day test key is unsuitable for deployment.
- Backup and restore-drill schedules matched to the approved RPO/RTO. CI runs on
  source changes do not establish backup freshness or an operating schedule.
- Recovery of PostgreSQL, object-storage versions, durable key rings and the
  external configuration/provider dependencies as a coordinated set. A logical
  PostgreSQL dump omits cluster-global roles. The current fresh-cluster rehearsal
  recreates reviewed fixture/API/Worker identities; production recovery must separately
  restore/reprovision the reviewed owners, grants and restricted API/Worker
  identities before applying archive ACLs, with fresh protected login credentials.
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
