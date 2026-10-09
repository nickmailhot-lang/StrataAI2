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
chmod +x health-check.sh apply-migrations.sh migration-stream.sh provision-runtime-roles.sh
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
The initially empty API retry-key fields are required: configure a current key
version and a JSON ring of base64 32-byte secrets before starting the API. See
[required production configuration](../architecture/configuration.md).

## Start and migrate

```bash
docker compose --env-file .env -f compose.release.yml up -d --wait postgres
COMPOSE_FILE=compose.release.yml ./apply-migrations.sh
COMPOSE_FILE=compose.release.yml ./provision-runtime-roles.sh
docker compose --env-file .env -f compose.release.yml up -d --wait
./health-check.sh
```

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
Automatic routing is available for deletion and metadata delivery. Review the
explicit general-job scope when adding Organizations that need other Work or
provider processing. Identity mail delivery uses its separate configuration and role.

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
