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
./health-check.sh
```

Migrations are applied from the versioned `db/migrations` directory included in the same
release bundle. Review migration/rollback notes before production promotion.

## Stop

```bash
docker compose --env-file .env -f compose.release.yml down
```

The application images in this bundle must not be rebuilt before deployment. Promotion
means using these exact tested image artifacts/digests with environment-specific configuration.
