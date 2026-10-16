# Optional production identity-mail role provisioning

The release now includes an optional provisioner for the documented separate
identity-mail database principal. It creates `strataai_identity_mail_runtime`
with LOGIN, no administrative attributes, no role memberships and no inheritance.
Its grants cover schema-version reads, service-scoped identity delivery jobs,
four required user columns and six required reset/verification-token columns.
It has no password-hash or Organization/Board grants.

Supply `STRATAAI_IDENTITY_DELIVERY_DB_PASSWORD` before creating the PostgreSQL
service, then run the bundled helper after migrations and general role setup.
The password must contain at least 20 characters and differ from the database
administrator, API and general Worker passwords. Configure the existing Worker
identity connection string with this role and the same encoded secret. See the
[release sequence](../release/README.md#optional-verification-and-recovery-mail-role).
Existing deployments must refresh the PostgreSQL container's environment before
provisioning changed secrets, preserving the same data volume and deployment
identity. The helper does not enable transport or provision cloud resources.

Provisioning is transactional. It refuses an existing privileged, role-member,
public-schema-creating or application-object-owning role before changing its
credential. Reprovisioning resets public-table, column and routine grants before adding
the narrow capability boundary. Column reset is necessary because table-level
REVOKE does not remove old column grants.
An effective-permission check also includes PUBLIC grants and rolls back if
they would expose application data or credential columns beyond this boundary.

The real PostgreSQL test exposed another bug: the old password-refusal branch
used `\quit 3`, which this PostgreSQL client treated as successful termination.
Both general and mail-role provisioners now raise a named database exception
under ON_ERROR_STOP. Weak/reused credentials return failure before role creation,
with no supplied secret in the error message.

## Function and maintenance boundaries — 2026-10-16

The separate [mail-role provisioner](../../db/provision-identity-mail-role.sql)
now resets public-schema routine grants along with its table/column grants.
Native baseline probes reproduce eight failures: three obsolete direct function
capabilities, retained execution delegation, two public private-definer grants,
and public maintenance privileges on both an unrelated attachment table and the
permitted delivery queue. The complete baseline fails in 111.319 seconds; the
existing direct-maintenance reset remains a passing control. No maintenance
operation is performed by these probes.

Only the approved role guard and caller-permission claim/completion helpers are
explicitly regranted. Claim/completion no longer depend on shared public
execution remaining enabled. Unexpected public access to non-trigger definer
functions or remaining function grant options refuses provisioning. Relation
checks also refuse `MAINTAIN` alongside `DELETE`, `TRUNCATE` and `TRIGGER`.
Existing narrow column grants, forced service-scope RLS and privileged-role,
membership, schema-creation and ownership refusals remain in force.

Refusal rolls back credentials and ACLs in the same provisioning transaction;
shared `PUBLIC` permissions are preserved for operator review. Remove the
unintended public grant before retrying. This is provisioning validation, not
continuous monitoring of subsequent administrator grants. It adds no transport,
provider integration, application feature or migration. Historical bundles keep
their original script; current complete immutable-image CI remains required.

The [required native PostgreSQL regression](../../tests/identity_role_provision_test.py)
passes in **115.445 seconds**: all nine new reset/refusal/control cases, followed
by an actual restricted login's claim/completion with public access to both
approved helpers revoked. All previous API/Worker and mail password, column,
lease, membership, ownership, service RLS and permission checks also pass.
Refusal fingerprints include all three runtime identities/credentials plus
schema/relation/column/function ACLs, ownership and memberships, and require a
successful query with a nonempty digest. The wrapper takes 116.669 seconds,
matches all 154 captured inputs and restores original Docker inventory. Its
owned PostgreSQL container and temporary directory are removed. No existing
database, application host, provider integration, new image, maintenance operation
or large-load workload is used. Release-payload tests pass 47/47 in 14.649 seconds.
Current immutable-image and managed-mail delivery acceptance remain outstanding;
PRD-02 stays open at **16% estimated work remaining**.

The [mail-enabled fresh-cluster recovery contract](database-recovery.md#recovery-with-the-separate-mail-identity--2026-10-16)
also preserves the reviewed mail identity's archived ACLs, recreates it without
copying source credentials, and activates fresh target credentials only after
restore verification. Its actual restricted session verifies delivery lease
and privacy boundaries. Production managed-mail and coordinated application
recovery remain separate acceptance requirements.

## Verification and limits

The mandatory isolated PostgreSQL/pgvector test applies all ordered migrations
in a new container with private credentials, tmpfs data and no external network.
It verifies three refused general-role configurations with no roles created;
five refused mail-role configurations; successful/repeated provisioning; real
authenticated role safety; forced service-scope RLS; token/user column access;
lease claim and completion; denied password-hash/domain access; removal of an
old password-hash column grant; and refusal of membership, CREATEDB and object
ownership without changing the credential.
Inherited PUBLIC password-hash and Board grants also refuse provisioning with
the original credential preserved. A password containing quote and
semicolon characters also authenticates successfully. The complete test passes
and removes its owned container and temporary credential files.

The initial run correctly exposed successful exit codes from password refusal;
its cleanup fixture also referenced an already absent peer role. Neither is
passing evidence. The final corrected isolated run passes. All 25 release
integrity tests, four actual Compose policy tests, 213 CI coverage guards, two
source-free startup checks, workflow validation and shell syntax checks pass.
The verifier refuses payloads missing either the helper or its SQL even if
checksums are recalculated. Packaging retains the original immutable images.

The first hosted run fails during fixture migrations before capability checks.
Its initial Unix-socket readiness probe can admit the image's temporary init
server, which does not listen on TCP. The fixture now requires an authenticated
TCP SELECT against its intended database before migration. The complete local
rerun passes; current hosted verification remains required. This changes setup
admission, not role grants, test assertions or their readiness deadline.

The SENT lease-function check uses a synthetic provider receipt. This verifies
database capability and provisioning, not managed mail delivery, sender-domain
verification or current retained-image deployment. Current complete CI and full
[authentication acceptance](prd-02-acceptance.md) still govern closure.
Estimated PRD-02 work remaining stays **16%**; its issue remains open.
