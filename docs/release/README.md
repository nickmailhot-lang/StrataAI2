# Running a StrataAI2 CI release bundle

The release bundle contains the exact web, API, and Worker images tested by CI.

## Requirements

- Docker Engine or Docker Desktop
- Docker Compose v2

No Node.js, .NET SDK, or source checkout is required.

## Load images

```bash
gunzip -c images/strataai-web.tar.gz | docker load
gunzip -c images/strataai-api.tar.gz | docker load
gunzip -c images/strataai-worker.tar.gz | docker load
```

Copy `.env.release.example` to `.env`, replace the placeholder values, and ensure
the three image variables point at the image tags recorded in `build-metadata.json`.

## Start and migrate

```bash
docker compose --env-file .env -f compose.release.yml up -d --wait
COMPOSE_FILE=compose.release.yml ./apply-migrations.sh
COMPOSE_FILE=compose.release.yml ./provision-runtime-roles.sh
./health-check.sh
```

Migrations are applied from the versioned `db/migrations` directory included in the same
release bundle. Review migration/rollback notes before production promotion.
Use distinct API and Worker database passwords. The initialization account is
reserved for migrations and role provisioning; application readiness fails until
restricted runtime roles have been provisioned. See `db/provision-runtime-roles.sql`
for the explicit grants required by this release.

To enable the Work event delivery Worker, set
`STRATAAI_WORKER_ORGANIZATION_IDS` to a comma-separated list of Organization UUIDs
(maximum 100) and recreate the Worker. Empty scope disables Organization jobs.
The Worker marks persisted events ready for authorized replay and SignalR streams;
The Board UI consumes these streams with reconnect and snapshot fallback. Configure `STRATAAI_REALTIME_PUBLIC_ORIGIN`
with the exact public browser origin (scheme, hostname and port). If it is empty,
the API uses `STRATAAI_PUBLIC_ORIGIN`; if both are empty, live transport is disabled
while normal read/write APIs remain available. Invalid configured origins reject
startup. Nginx supports the `/boards/live` WebSocket upgrade route in the tested image.
Automatic Organization discovery is not implemented. Review this scope when adding
Organizations. Identity mail delivery uses its separate configuration and role.

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
