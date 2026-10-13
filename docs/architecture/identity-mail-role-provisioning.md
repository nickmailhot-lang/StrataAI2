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
credential. Reprovisioning resets public-table and column grants before adding
the narrow capability boundary. Column reset is necessary because table-level
REVOKE does not remove old column grants.
An effective-permission check also includes PUBLIC grants and rolls back if
they would expose application data or credential columns beyond this boundary.

The real PostgreSQL test exposed another bug: the old password-refusal branch
used `\quit 3`, which this PostgreSQL client treated as successful termination.
Both general and mail-role provisioners now raise a named database exception
under ON_ERROR_STOP. Weak/reused credentials return failure before role creation,
with no supplied secret in the error message.

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

The SENT lease-function check uses a synthetic provider receipt. This verifies
database capability and provisioning, not managed mail delivery, sender-domain
verification or current retained-image deployment. Current complete CI and full
[authentication acceptance](prd-02-acceptance.md) still govern closure.
Estimated PRD-02 work remaining stays **16%**; its issue remains open.
