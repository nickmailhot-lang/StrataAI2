# Running a StrataAI2 CI release bundle

The release bundle contains the exact web, API, and Worker images tested by CI.

## Requirements

- Docker Engine or Docker Desktop
- Docker Compose v2
- Bash with `curl`, `jq`, and standard core utilities including `sha256sum` (for the supplied runtime scripts and verification)

No Node.js, .NET SDK, or source checkout is required.

For a Linux host terminating HTTPS in the existing web image, see
[the optional HTTPS configuration](#optional-https-on-a-linux-docker-host).

## Verify the downloaded bundle

After extracting the artifact ZIP, verify it before loading images:

```bash
sha256sum --check SHA256SUMS
(cd images && sha256sum --check SHA256SUMS)
(cd security && sha256sum --check SHA256SUMS)
chmod +x health-check.sh apply-migrations.sh migration-stream.sh provision-runtime-roles.sh provision-identity-mail-role.sh
```

The ZIP uploader does not preserve executable permissions. The environment example
`.env.release.example` is included explicitly; it contains placeholders only.
The `sbom/` directory contains the tested web/API/Worker and metrics receiver
CycloneDX inventories. `security/` preserves the original security evidence,
its canonical build metadata, and its checksum manifest. Compare its build identity
with the root `build-metadata.json`. The `images/` directory also retains the
original image metadata and checksum manifest; CI compares both image/security
manifests with the verified original inputs before upload. This does not replace
successful CI gates.

CI also starts the assembled payload before upload, applies its migrations,
provisions restricted runtime roles, checks health/build identity and requires
graceful API/Worker restart. See the
[startup evidence and limits](../architecture/release-bundle-startup.md).

## Load images

```bash
gunzip -c images/strataai-web.tar.gz | docker load
gunzip -c images/strataai-api.tar.gz | docker load
gunzip -c images/strataai-worker.tar.gz | docker load
```

## Demo from tested images

After loading the three images above, copy the Demo settings:

```bash
cp .env.demo.example .env.demo
```

Open `build-metadata.json` and copy its `images.web`, `images.api` and
`images.worker` values into the corresponding image variables in `.env.demo`.
Then start the image-only Demo stack:

```bash
docker compose --project-name strataai-demo --env-file .env.demo -f compose.demo.release.yml up -d --no-build --pull never --wait
```

Open [Demo sign-in](http://localhost:8088/login):

| Email | Password |
| --- | --- |
| `demo@strataai.test` | `StrataAI-Demo-2026!` |

Demo needs only the three retained images. Its API and separate Worker use
process-local Demo state, with no database or provider configuration. The web
port binds to this machine's loopback interface; API and Worker remain on the
private internal network. The optional port/origin settings are in
`.env.demo.example`. Restarting the Demo API resets its in-memory data and
restores the known account. See [Demo runtime behavior](../architecture/runtime-modes.md#demo-sign-in).

Stop this Demo project with:

```bash
docker compose --project-name strataai-demo --env-file .env.demo -f compose.demo.release.yml down
```

The same tested images support [Production configuration](#production-configuration)
below. The repository's development `compose.demo.yml` builds from source;
this bundle's `compose.demo.release.yml` uses the already-loaded images.

## Production configuration

Copy `.env.release.example` to `.env`, replace the placeholder values, and ensure
the three image variables point at the image tags recorded in `build-metadata.json`.
Choose a unique, stable `COMPOSE_PROJECT_NAME` for this environment before its
first deployment. For an existing deployment, retain its current project name
instead of adopting the example's name. Preserve it when extracting another
release into a different directory; the PostgreSQL and activity-key volumes
belong to that project. See [promotion and rollback](promotion-and-rollback.md)
for retaining the prior release and checking schema compatibility.
The initially empty API retry-key fields are required: configure a current key
version and a JSON ring of base64 32-byte secrets before starting the API. See
[required production configuration](../architecture/configuration.md).

All three application image settings are required, including when starting only
PostgreSQL for migrations. Missing or empty values stop Compose before it starts
services. Release Compose never pulls application images automatically: load the
verified archives first (or explicitly fetch the verified immutable registry
references), then use those references. This prevents a missing setting from
silently selecting a local development tag. PostgreSQL and optional support
services retain their documented provider images.

The included authentication policy defaults disable self-registration and
require verified email. To permit initial account enrollment, set
`STRATAAI_AUTH_ALLOW_SELF_REGISTRATION=true` after configuring the intended
verification/mail transport and its restricted Worker database role. Keep the
verification requirement enabled for that flow. The release Compose file now
forwards the documented policy settings from `.env`; password length, session
hours and security-token minutes apply to the API, and verification policy also
reaches Worker handlers. Explicit empty or malformed API settings refuse startup.
See [policy configuration and verification](../architecture/release-auth-policy-verification.md).

## Start and migrate

The [canonical role provisioner resets function grants](../architecture/runtime-database-roles.md#function-capability-reset-and-public-grants--2026-10-16)
as well as table grants. Remove any unintended `PUBLIC EXECUTE` on private
non-trigger definer functions before retrying a refused rotation. Refusal rolls
back credential and ACL changes; shared grants are preserved for operator review.

Provisioning also [matches the runtime connection guard](../architecture/runtime-database-roles.md#provisioning-and-connection-guard-agreement--2026-10-16):
API/Worker roles must lack effective public-schema `CREATE` and own no public
relations (including sequences) or functions. Remove unintended schema grants
or transfer object ownership to the reviewed migration identity before retrying.
Refusal preserves credentials and grants; shared `PUBLIC` rights are not revoked
automatically.

The [runtime provisioner checks effective capabilities](../architecture/runtime-database-roles.md#atomic-effective-capability-validation--2026-10-16),
including `PUBLIC` grants and database-role memberships. An unsafe configuration
refuses atomically, preserving existing credentials/grants. Review and remove
the unintended grant or membership before retrying; the script does not silently
change permissions belonging to other principals. These password-based runtime
roles are dedicated identities with no peer-role memberships.

```bash
docker compose --env-file .env -f compose.release.yml up -d --wait postgres
COMPOSE_FILE=compose.release.yml ./apply-migrations.sh
COMPOSE_FILE=compose.release.yml ./provision-runtime-roles.sh
docker compose --env-file .env -f compose.release.yml up -d --wait
bash verify-release-runtime.sh build-metadata.json
```

After all [promotion and core workflow checks](promotion-and-rollback.md) pass,
use the bundled `record-release.py` command (Python 3.11 or newer) to atomically retain the
current and prior accepted build/image references outside this immutable bundle.
It repeats runtime verification and refuses an acceptance update on failure.
The promotion guide covers protected state, first deployment and rollback use.

Release verification first requires HTTP 200 from all six web/API/Worker health and
readiness checks; redirects and unavailable endpoints fail promotion. Requests
use a five-second connection limit and a 30-second total limit each. Set
`STRATAAI_HEALTH_TIMEOUT_SECONDS` to an integer from 1 to 300 to adjust the
per-request total limit. Response bodies are discarded. Configure the documented
service URLs to their actual deployment addresses; a login or redirect page is
not readiness evidence.

It then checks the web, direct API, Worker and web-proxied API build revision/version against this
bundle's already verified metadata. Metadata transfers are limited to 15 seconds
(or the smaller health timeout) and 4,096 response bytes. Mismatches, redirects,
malformed or incomplete responses fail. The API and Worker must report their
respective `strataai-api` / `strataai-worker` service identities and `production`
mode; healthy Demo or mixed-mode hosts cannot satisfy Production acceptance.
The web proxy must serve the same selected API build and Production service
identity; a healthy proxy routed to an older API cannot pass verification.
Both commands connect directly and
ignore personal curl configuration; TLS verification stays enabled. The plain
`health-check.sh` remains a health diagnostic and does not prove build identity.
See [promotion verification and limits](promotion-and-rollback.md#build-bound-operator-verification-2026-10-15).

Start PostgreSQL first, then migrate and provision restricted roles before starting
the application hosts. Their readiness requires the current schema and those roles.

Migrations are applied from the versioned `db/migrations` directory included in the same
release bundle. Review migration/rollback notes before production promotion.
Use distinct API and Worker database passwords. The initialization account is
reserved for migrations and role provisioning; application readiness fails until
restricted runtime roles have been provisioned. See `db/provision-runtime-roles.sql`
for the explicit grants required by this release.

To enable the Work event delivery Worker, set
`STRATAAI_WORKER_ORGANIZATION_IDS` to a comma-separated list of Organization UUIDs
(maximum 100) and recreate the Worker. Empty scope disables this general job loop.
Accepted Organization deletions have a separate automatic discovery loop,
enabled by default with `STRATAAI_ORGANIZATION_DELETION_DISCOVERY_ENABLED=true`.
It needs no manual Organization list and continues routing pending completion
delivery after the parent becomes DELETED. Set that flag to `false` to suspend
automatic deletion processing. Read the
[deletion lifecycle and discovery contract](../architecture/organization-deletion-lifecycle.md#automatic-production-deletion-discovery).
Metadata event delivery also has independent automatic routing, enabled by
default with `STRATAAI_ORGANIZATION_METADATA_DISCOVERY_ENABLED=true`. It needs
no manual scope list and claims only metadata jobs, leaving provider and Work
event jobs for their existing scopes. Set it to `false` to suspend metadata
routing. See [metadata delivery](../architecture/organization-metadata-events.md).
The Worker marks persisted events ready for authorized replay and SignalR streams;
The Board UI consumes these streams with reconnect and snapshot fallback. Configure `STRATAAI_REALTIME_PUBLIC_ORIGIN`
with the exact public browser origin (scheme, hostname and port). If it is empty,
the API uses `STRATAAI_PUBLIC_ORIGIN`; if both are empty, live transport is disabled
while normal read/write APIs remain available. Invalid configured origins reject
startup. Nginx supports the `/boards/live` WebSocket upgrade route in the tested image.
Automatic routing also covers Organization configuration and operational
lifecycle delivery, invitation recipient authority and invitation issuer
authority. The six discovery settings in `.env.release.example` reach the Worker;
set the corresponding setting to `false` to disable that automatic discovery
loop. Explicit general-job scope remains independently configured. Omitted
settings retain the Production default of `true`. Explicit empty or malformed
settings reach Worker validation and refuse startup. Releases before this fix
ignored the configuration and operational-lifecycle settings in `.env`, and
replaced empty discovery values with defaults; review existing values during
promotion. See [discovery configuration verification](../architecture/release-auth-policy-verification.md#worker-discovery-settings).
Review the
explicit general-job scope when adding Organizations that need other Work or
provider processing. Identity mail delivery uses its separate configuration and role.

## Optional HTTPS on a Linux Docker host

Use `compose.https.yml` with Docker Compose **2.24.4 or later** when this web
image terminates TLS. Existing approved external ingress remains an alternative.
Use assets from the same release as its images; older bundles/images lack the
optional listener. This overlay introduces no image or build. It publishes only
the web TLS port and removes the base web HTTP and API/Worker host bindings.
Internal HTTP remains available to container health checks and private services.

Obtain a browser-trusted certificate for the intended hostname through your
certificate provider. Outside the checkout and extracted bundle, prepare an
existing private directory containing `fullchain.pem` (leaf certificate first,
then intermediates) and its matching `privkey.pem`. Restrict directory/key
access to the operator and the container's Nginx master process. The read-only
directory bind refuses automatic directory creation. Keep key files out of
Git, image contexts, release payloads and evidence uploads. The overlay does
not issue or automatically renew certificates.

Add these non-secret settings to the configured `.env`:

```dotenv
STRATAAI_HTTPS_CERTIFICATE_DIRECTORY=/srv/strataai-certificates
STRATAAI_HTTPS_PORT=443
STRATAAI_HTTPS_BIND_ADDRESS=0.0.0.0
STRATAAI_PUBLIC_ORIGIN=https://tasks.example.com
STRATAAI_REALTIME_PUBLIC_ORIGIN=https://tasks.example.com
```

Replace the directory and hostname with the actual deployment values, including
the external port in both origins when it is not 443. First start PostgreSQL,
migrate and provision roles using the preceding instructions. Start the
application with both files:

```bash
docker compose --env-file .env -f compose.release.yml -f compose.https.yml config --quiet
docker compose --env-file .env -f compose.release.yml -f compose.https.yml up -d --no-build --pull never --wait
docker compose --env-file .env -f compose.release.yml -f compose.https.yml ps
curl --fail --show-error https://tasks.example.com/api/health
```

Require healthy container status, successful certificate trust/hostname checks
and HTTP 200. Do not disable client certificate verification. API and Worker
readiness remain private; inspect their container health and, when diagnosing,
query `/readyz` from inside each container. The base `health-check.sh` defaults
to published diagnostic ports and is unsuitable for this overlay without
separate approved diagnostic addresses. Verify real sign-in, task persistence
and authorized live collaboration through the HTTPS origin before traffic.
The shared response headers add HSTS only to HTTPS responses.

For renewal, stage the complete matching certificate/key pair as **new sibling
files in the mounted directory**, with the same private permissions and ownership
as the existing pair. Rename each staged file over its destination, then validate
the full configuration and reload the web listener without changing images.
Do not overwrite existing files in place: Nginx can inherit cached SSL objects
when file identity and modification time remain unchanged, including rapid
renewal and rollback. See the [Nginx SSL object cache contract](https://nginx.org/en/docs/ngx_core_module.html#ssl_object_cache_inheritable).
Publish both files before validation; the two renames are not a transaction, so
serialize certificate maintenance and do not reload between them.

```bash
docker compose --env-file .env -f compose.release.yml -f compose.https.yml exec -T web nginx -t
docker compose --env-file .env -f compose.release.yml -f compose.https.yml exec -T web nginx -s reload
```

If validation fails, stop before reload and restore the matching prior files.
Do not treat a successful reload command as proof that the replacement is
served: verify a fresh TLS connection, its expected certificate fingerprint,
trust, hostname and expiry, then repeat sign-in/session and saved-task checks.
Existing connections can still belong to the old listener workers. To restore
the prior certificate, stage its matching chain and key as new sibling files and
rename them over the mounted destinations, run the same validation/reload
commands and verify another fresh connection. This certificate procedure does
not roll back application images or database changes.

Keep the matching prior certificate/key available for rollback. Recheck the
served certificate and expiry after renewal. Symlinks must resolve within the
mounted directory; a provider's links into an unmounted archive will fail.
Certificate issuance, expiry monitoring, DNS/firewall routing and the actual
deployment's HTTPS origin remain operator requirements. See
[HTTPS verification and limits](../architecture/https-deployment.md).

The bundle includes a read-only certificate check for operator monitoring:

```bash
# Example policy: require at least fourteen days of remaining validity.
bash verify-https-certificate.sh tasks.example.com 443 1209600
# For a private CA, supply its trusted PEM file explicitly:
bash verify-https-certificate.sh tasks.example.com 443 1209600 /secure/company-ca.pem
```

Run this from a host with Bash, OpenSSL 3 and GNU coreutils (`timeout` included).
It opens a fresh TLS connection, verifies hostname/IP identity and current
trust, and checks the captured certificate and usable trust path at the chosen
future horizon. DNS names use SNI. The required horizon is 1–31,536,000 seconds;
choose it to match your renewal policy. Each OpenSSL invocation has a ten-second
deadline by default; `STRATAAI_TLS_TIMEOUT_SECONDS` accepts an explicit integer
from 1 to 60. Captured peer output is limited to 64 KiB, private scratch files
are removed, and failures return nonzero with a generic message.

Connect the exit status to your monitoring and alerts, and test delivery of
those alerts. This command installs no schedule, renews no certificate and
does not check revocation or every load-balancer node. It does not replace
application readiness or the [Production runtime identity check](#start-and-migrate).

## Optional private file attachments on Linux

File upload is disabled by default. Enable it only after provisioning the private
AWS S3 bucket, expected 12-digit account owner, supported region, least-privilege
runtime access and a private ClamAV Unix socket with maintained definitions.
The application does not create buckets, IAM roles, tokens or scanner services.
See [the storage and scanner contracts](../architecture/attachment-object-storage.md).

Use a release containing the workload-identity dependency described in that
contract; earlier bundles such as `bb3cd4fc` lack the STS assembly needed for
web identity. Keep the same deployment project, durable keys and database.
Complete the normal migration/role procedure before starting enabled hosts.
Add these settings to the private operator environment file:

```dotenv
STRATAAI_ATTACHMENT_S3_BUCKET=your-private-bucket
STRATAAI_ATTACHMENT_S3_OWNER=123456789012
STRATAAI_ATTACHMENT_S3_REGION=us-east-1
STRATAAI_WORKER_ORGANIZATION_IDS=replace-with-explicit-organization-uuids
STRATAAI_ATTACHMENT_SCANNER_DIRECTORY=/srv/strataai-scanner
STRATAAI_API_ATTACHMENT_ROLE_ARN=arn:aws:iam::123456789012:role/strataai-api
STRATAAI_WORKER_ATTACHMENT_ROLE_ARN=arn:aws:iam::123456789012:role/strataai-worker
STRATAAI_API_ATTACHMENT_TOKEN_DIRECTORY=/srv/strataai-identity/api
STRATAAI_WORKER_ATTACHMENT_TOKEN_DIRECTORY=/srv/strataai-identity/worker
```

These are example identifiers and paths. The scanner directory must contain
`clamd.sock`. Each identity directory must contain a readable `token` supplied
and rotated by your trusted workload-identity issuer. Keep the directories
private; mount the directories read-only so replacement tokens remain visible.
The SDK obtains temporary role credentials. Avoid injecting static AWS keys
alongside web identity, because they take precedence in the SDK credential chain.

Compose's environment file supplies interpolation values; it does not
automatically forward arbitrary AWS variables or mount host token files. Save
the following operator-owned overlay outside the bundle, for example as
`/srv/strataai-config/attachment-identity.yml`:

```yaml
services:
  api:
    environment:
      AWS_ROLE_ARN: ${STRATAAI_API_ATTACHMENT_ROLE_ARN:?Set API attachment role}
      AWS_WEB_IDENTITY_TOKEN_FILE: /run/strataai-identity/token
      AWS_REGION: ${STRATAAI_ATTACHMENT_S3_REGION:?Set attachment region}
    volumes:
      - type: bind
        source: ${STRATAAI_API_ATTACHMENT_TOKEN_DIRECTORY:?Set existing API token directory}
        target: /run/strataai-identity
        read_only: true
        bind:
          create_host_path: false
  worker:
    environment:
      AWS_ROLE_ARN: ${STRATAAI_WORKER_ATTACHMENT_ROLE_ARN:?Set Worker attachment role}
      AWS_WEB_IDENTITY_TOKEN_FILE: /run/strataai-identity/token
      AWS_REGION: ${STRATAAI_ATTACHMENT_S3_REGION:?Set attachment region}
    volumes:
      - type: bind
        source: ${STRATAAI_WORKER_ATTACHMENT_TOKEN_DIRECTORY:?Set existing Worker token directory}
        target: /run/strataai-identity
        read_only: true
        bind:
          create_host_path: false
```

Validate without printing resolved secrets, then recreate the application hosts:

```bash
docker compose --env-file /srv/strataai-config/runtime.env -f compose.release.yml -f compose.attachments.yml -f /srv/strataai-config/attachment-identity.yml config --quiet
docker compose --env-file /srv/strataai-config/runtime.env -f compose.release.yml -f compose.attachments.yml -f /srv/strataai-config/attachment-identity.yml up -d --no-build --pull never --wait api worker web
```

For the HTTPS deployment, include `-f compose.https.yml` in both commands and
retain its certificate settings. Both hosts must be ready: the API checks the
database and private bucket; the Worker also checks the scanner. Use the
[runtime identity check](#start-and-migrate), then verify a small real upload
through Pending, Clean/Rejected scanning, controlled download, image preview
and cover where applicable. Confirm quarantine refusal, permission withdrawal,
provider outages and recovery in the intended deployment. Do not set
`STRATAAI_ATTACHMENT_TEST_LOCAL_ROOT` or `IntegrationTest` to bypass Production
providers. Role trust, bucket policy, token rotation, scanner definitions and
managed-provider lifecycle/backup acceptance still require operator evidence.

## Optional verification and recovery mail role

The included `.env.release.example` lists the complete optional mail setup,
disabled by default. Configure its sender, exact HTTPS public origin, provider
account and token-signing key ring in both API and Worker. The provider API key
and dedicated delivery connection are Worker-only settings; the API receives
neither. Keep real credentials out of source control and retain signing-key
versions needed by existing tokens and queued work. Complete the role procedure
below and the [mail transport configuration](../architecture/identity-email.md)
before enabling mail or verified-account enrollment.

The bundle includes `provision-identity-mail-role.sh` and
`db/provision-identity-mail-role.sql`. To use the supplied dedicated role, set
`STRATAAI_IDENTITY_DELIVERY_DB_PASSWORD` to a distinct secret of at least 20
characters before creating the PostgreSQL service. After migrations and general
runtime-role provisioning, run:

```bash
COMPOSE_FILE=compose.release.yml ./provision-identity-mail-role.sh
```

This creates or reprovisions `strataai_identity_mail_runtime` with only the mail
job, required identity-column and schema-version grants. It refuses privileged,
role-member or application-object-owning existing roles before changing their
credentials. Reprovisioning removes obsolete public-table, column and routine
grants. The role guard and approved caller-permission claim/completion functions
receive explicit execution grants, supporting deployments that revoke shared
public access to those helpers. Unexpected public private-definer execution or
maintenance privileges refuse rotation with credentials/ACLs preserved. Review
and remove the unintended shared grant, then retry. See the [mail-role boundary
and verification](../architecture/identity-mail-role-provisioning.md#function-and-maintenance-boundaries--2026-10-16).
Password validation errors now return a failing exit status in both provisioners.

Configure `STRATAAI_IDENTITY_DELIVERY_CONNECTION_STRING` for this role and the
same secret, using correctly encoded Npgsql connection-string syntax. Include
`SSL Mode=Disable` only for this private local `postgres` service; use
`SSL Mode=VerifyFull` and configured certificate trust for a hosted database.
The main API/Worker local connections make their plaintext exception explicit
in Compose. See [database transport configuration](../architecture/configuration.md#postgresql-transport)
before connecting either database role to an external service. Supply
the sender, HTTPS origin, signing keys, provider account and Worker-only provider
credential described in [identity mail configuration](../architecture/identity-email.md).
The helper does not enable mail, create provider resources or send messages.
On an existing deployment, these helpers read secrets from the PostgreSQL
container environment: refresh that service's environment with its existing
data volume before provisioning changed secrets. Plan that restart as part of
promotion; changing the environment alone does not rotate an existing database
administrator password.

## Rotate local runtime database passwords

This procedure rotates the API and Worker roles in the release's local
PostgreSQL service. Schedule an interruption and keep the same project name,
named volumes and three verified application image references. For a managed
database, follow its credential-rotation procedure instead.

Stop the application hosts, then edit the private `.env` to supply two new,
distinct runtime passwords of at least 20 characters:

```bash
docker compose --env-file .env -f compose.release.yml stop --timeout 20 api worker
```

Refresh PostgreSQL's environment with its existing data volume, provision the
roles, and recreate the application hosts with the new settings:

```bash
docker compose --env-file .env -f compose.release.yml up -d --no-deps --force-recreate --wait postgres
COMPOSE_FILE=compose.release.yml ./provision-runtime-roles.sh
docker compose --env-file .env -f compose.release.yml up -d --no-deps --force-recreate --wait api worker
bash verify-release-runtime.sh build-metadata.json
```

Require successful role provisioning and all health/readiness checks before
restoring traffic. Confirm the runtime revision/image identities and that the
previous passwords are rejected. Store credentials only in deployment secret
management; never include them in a release record or diagnostic output. No
image rebuild or database-volume deletion is part of this procedure. This
does not rotate the PostgreSQL administrator password, provider credentials
or application signing/encryption keys.

## Encrypted Docker PostgreSQL backups

The bundle includes `backup-postgres.sh` for an already-running Docker
PostgreSQL service. This optional command needs OpenSSL with CMS AES-GCM support
and GNU coreutils (`ln -T`). Use a Bash/GNU environment such as Linux or Windows
Git Bash. No Node.js or .NET SDK is required.

Use your approved recipient certificate and keep its recovery private key in
separate protected custody. Independently verify the certificate fingerprint;
the command checks parsing/expiry, not your trust decision. Choose an existing
protected output directory outside the release bundle. Replace the container,
database and database-user values with this deployment's actual values:

```bash
postgres_container=$(docker compose --env-file .env -f compose.release.yml ps -q postgres)
export STRATAAI_BACKUP_DB_USER=strataai
backup_archive="/secure/backups/strataai-$(date -u +%Y%m%dT%H%M%SZ).cms"
backup_record="${backup_archive%.cms}.json"
bash backup-postgres.sh "$postgres_container" strataai /secure/backup-recipient.crt "$backup_archive" "$backup_record"
python3 verify-backup-record.py "$backup_record" "$backup_archive" 86400
```

The database login must be able to dump the entire database, including forced
RLS tables, using its configured local container authentication. The command
does not start a service, migrate schema, supply a password or fall back to a
different database. It accepts explicit database/user names, not connection
URIs. Failed noninteractive authentication or a blocked dump fails the backup.

The custom-format dump streams directly into authenticated encryption. No
plaintext dump is written. A failed dump/encryption leaves no published archive.
Publication is atomic and refuses an existing file/directory, including one
created while the dump runs. Temporary encrypted output and private diagnostics
are removed. The optional fifth argument publishes a new sibling JSON completion
record after publishing the encrypted archive. It contains only schema version,
dump start/completion Unix times, archive length and SHA-256; it contains no
database/container/path/recipient identity. Existing records are refused before
reading the database. Record publication races fail without overwriting the
other file; an already published encrypted archive may then remain without a
valid completion marker. Do not treat that partial result as a successful job.
The four-argument archive command remains available without a completion record.

The read-only checker needs Python 3.11 or newer. It verifies the entire archive
against the explicitly supplied, operator-protected record, rejects changed or
missing files, and measures age from the dump's start after checking integrity.
The last argument is the chosen maximum age in seconds: use the deployment's
database recovery policy, such as 86400 for the Standard 24-hour limit or 3600
for the Enhanced one-hour limit. Old snapshots, future/reversed clocks and
malformed records fail. Copying a file or finishing a slow dump does not renew
the snapshot's age. Success prints only fixed status/age/length fields; failure
returns nonzero without printing private input paths or a passing result.

Keep a separately protected authoritative copy of the completion record. The
record is not signed: permission to rewrite both record and archive defeats its
provenance. Record the protected archive location, recipient identity and
restore-test result separately; copy the encrypted archive and its trusted
record to the approved off-host destination under your backup policy. Verify
that stored copy against the authoritative record as well. This check does not
decrypt/authenticate CMS, restore data, prove off-host durability or cover object
storage and key rings; retain authenticated recovery drills.

Schedule this command and restore drills in the deployment's operating system
or managed backup system. This command does not configure a scheduler, storage
retention or a running freshness monitor. Run the checker for the explicit
record/archive pair from the scheduler or monitoring system and alert on its
nonzero exit. A database dump omits global roles, object
storage and durable key rings. Follow the
[recovery requirements and authenticated restore sequence](../architecture/database-recovery.md)
and [promotion/rollback procedure](promotion-and-rollback.md) before production
recovery. The full CI rehearsal exercises this exact backup command with a small
source database and a fresh isolated target cluster. It checks distinct cluster
identities, missing-role rollback and reviewed fixture-role recreation. It
also verifies canonical target API/Worker and separate mail-role provisioning,
all three real SCRAM logins and scoped database operations. It does not certify deployed application recovery,
production RPO/RTO or providers.

## Restore into an isolated empty Docker database

Use the archive SHA-256 from your separately protected backup record. Recipient
encryption alone does not identify the backup creator; do not derive the
expected digest from an untrusted archive and treat that as provenance.
Recover the matching certificate/private key from their protected custody.
Expired historical certificates can still decrypt their original backups.

Create a new empty database from `template0` on the selected PostgreSQL service;
do not connect the application or Workers to it yet. Replace the database/user
names and paths below with this recovery's actual values. `createdb` refuses an
already-existing name, and the restore command never drops/cleans a database.

```bash
postgres_container=$(docker compose --env-file .env -f compose.release.yml ps -q postgres)
docker exec "$postgres_container" createdb --username=strataai --template=template0 strataai_restore
export STRATAAI_RESTORE_DB_USER=strataai
export STRATAAI_RESTORE_TMPDIR=/secure/restore-work
archive_sha256=replace-with-the-64-hex-digit-trusted-backup-digest
bash restore-postgres.sh "$postgres_container" strataai_restore /secure/backup-recipient.crt /secure/backup-recipient.key /secure/backups/backup.cms "$archive_sha256"
```

The temporary directory must already exist with access limited to the recovery
operator, on protected storage with space for an encrypted archive snapshot and
its decrypted custom dump. The command hashes/decrypts the same private snapshot,
authenticates the entire file before any Docker/SQL operation, validates the
archive format, and refuses targets containing application relations, routines,
types, custom schemas or extensions beyond the default `plpgsql`. Restoration
uses `pg_restore --single-transaction --exit-on-error`; no `--clean` or automatic
database creation is used. Temporary ciphertext, key copy, plaintext and private
diagnostics are removed on success or failure. The command does not delete the
operator's original archive or keys.

For recovery into a new cluster, first recreate the reviewed owners and restricted
identities referenced by the trusted archive's ACLs. Keep application hosts stopped
and the target isolated. A database dump does not create cluster-global roles;
missing roles make this transactional restore fail. Preserve least privilege and
use fresh protected login credentials rather than copying global superusers or
password hashes. The CI fixture recreates restricted non-login identities before
restore, provisions API/Worker and separate mail credentials only after the
schema/data/ACL checks, and verifies their actual authenticated sessions. If the
trusted archive references `strataai_identity_mail_runtime`, recreate that
reviewed restricted non-login identity before restoring, along with the API and
Worker identities. Activate it with fresh protected credentials through the
bundled mail provisioner after validation. See the [mail-enabled recovery
contract](../architecture/database-recovery.md#recovery-with-the-separate-mail-identity--2026-10-16). Production ownership, roles,
credentials and post-restore application checks require their own review before
traffic or background deliveries resume.

For an encrypted private key, supply `STRATAAI_RESTORE_KEY_PASSPHRASE` through
your secret provider. Its value is supplied only to the OpenSSL process through
the environment, never in command arguments or Docker calls. An absent/incorrect
passphrase fails without an interactive prompt. No Node.js/.NET SDK is needed;
the command uses the same Bash/OpenSSL/GNU host tools as backup.

Keep the restored target isolated until application/tenant/permission checks,
global-role provisioning, compatible image selection and coordinated
object-storage/key-ring recovery pass. A successful database restore is not a
traffic promotion or a complete disaster-recovery result. Record the restore
time, archive/digest/key identity, checks and outcome. Recovery `.cms`/`.dump`
files are not release assets; bundle integrity rejects them even with matching
checksums, and recovery archives/temporary folders are ignored by Git.

## Stop

```bash
docker compose --env-file .env -f compose.release.yml down
```

The application images in this bundle must not be rebuilt before deployment. Promotion
means using these exact tested image artifacts/digests with environment-specific configuration.

Optional operator metrics: start with
the [API/Worker measurement and configuration guide](../architecture/operator-metrics.md).
Both services use the configured receiver with independent service/build identity.

Start
`docker compose -f compose.release.yml -f compose.metrics.yml up -d` to add the
pinned private OTLP receiver. Scrapes are available only on host loopback at
`http://127.0.0.1:9464/metrics`. This provides in-memory aggregate collection;
configure a managed history/dashboard/alerting backend separately. For an external
receiver instead, use the full `STRATAAI_METRICS_OTLP_ENDPOINT` and optional secret
headers with the base Compose file. Collector outage does not block core readiness.

The release also includes `deploy/metrics/alerts.yml` and
`deploy/metrics/prometheus.example.yml`. The latter targets that same host's
loopback scrape and loads the rules; change the private target if the backend
runs elsewhere. Use them with an existing protected Prometheus-compatible
backend. Review [the alert meanings, activation checks and verification
limits](../architecture/operator-alerts.md) before enabling. API error/latency,
monitoring coverage, Worker/mail failures and unavailable backlog observations
have tested warning rules, but notification destinations, durable history,
threshold calibration and live firing/resolved delivery remain deployment work.
These files do not start a backend or add another required application image.
