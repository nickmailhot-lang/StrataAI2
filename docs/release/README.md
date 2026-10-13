# Running a StrataAI2 CI release bundle

The release bundle contains the exact web, API, and Worker images tested by CI.

## Requirements

- Docker Engine or Docker Desktop
- Docker Compose v2
- Bash with `curl` and `sha256sum` (for the supplied runtime scripts and checksum verification)

No Node.js, .NET SDK, or source checkout is required.

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

```bash
docker compose --env-file .env -f compose.release.yml up -d --wait postgres
COMPOSE_FILE=compose.release.yml ./apply-migrations.sh
COMPOSE_FILE=compose.release.yml ./provision-runtime-roles.sh
docker compose --env-file .env -f compose.release.yml up -d --wait
./health-check.sh
```

The health script requires HTTP 200 from all six web/API/Worker health and
readiness checks; redirects and unavailable endpoints fail promotion. Requests
use a five-second connection limit and a 30-second total limit each. Set
`STRATAAI_HEALTH_TIMEOUT_SECONDS` to an integer from 1 to 300 to adjust the
per-request total limit. Response bodies are discarded. Configure the documented
service URLs to their actual deployment addresses; a login or redirect page is
not readiness evidence.

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

## Optional verification and recovery mail role

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
credentials. Reprovisioning removes obsolete public-table and column grants.
Password validation errors now return a failing exit status in both provisioners.

Configure `STRATAAI_IDENTITY_DELIVERY_CONNECTION_STRING` for this role and the
same secret, using correctly encoded Npgsql connection-string syntax. Supply
the sender, HTTPS origin, signing keys, provider account and Worker-only provider
credential described in [identity mail configuration](../architecture/identity-email.md).
The helper does not enable mail, create provider resources or send messages.
On an existing deployment, these helpers read secrets from the PostgreSQL
container environment: refresh that service's environment with its existing
data volume before provisioning changed secrets. Plan that restart as part of
promotion; changing the environment alone does not rotate an existing database
administrator password.

## Stop

```bash
docker compose --env-file .env -f compose.release.yml down
```

The application images in this bundle must not be rebuilt before deployment. Promotion
means using these exact tested image artifacts/digests with environment-specific configuration.

Optional operator metrics: start with
`docker compose -f compose.release.yml -f compose.metrics.yml up -d` to add the
pinned private OTLP receiver. Scrapes are available only on host loopback at
`http://127.0.0.1:9464/metrics`. This provides in-memory aggregate collection;
configure a managed history/dashboard/alerting backend separately. For an external
receiver instead, use the full `STRATAAI_METRICS_OTLP_ENDPOINT` and optional secret
headers with the base Compose file. Collector outage does not block core readiness.
