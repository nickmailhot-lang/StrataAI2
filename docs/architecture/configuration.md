# Configuration and secrets

The same immutable StrataAI2 images are intended to move between environments.

Required baseline production configuration:

- `STRATAAI_RUNTIME_MODE=production`
- `ConnectionStrings__Postgres`
- Or structured `STRATAAI_DATABASE_HOST`, `STRATAAI_DATABASE_NAME`,
  `STRATAAI_DATABASE_USERNAME`, `STRATAAI_DATABASE_PASSWORD` and optional
  `STRATAAI_DATABASE_PORT` (5432 by default). Compose uses these fields with
  separate restricted API and Worker credentials.

Provision roles after migrations as described in [runtime database roles](runtime-database-roles.md).
The runtime account must pass the database security guard; initialization and
migration accounts cannot be used by the API or Worker.

Build revision/version are embedded in the image at build time and cannot be
set by runtime environment variables. The web's `/build-metadata.json`, API's
`/api/runtime` and Worker's `/runtime` report the identifiers of their actual
image assemblies/assets. See [build identity](build-identity.md).

Additional provider credentials are introduced only with the corresponding PRD and must be
provided by deployment secret management/environment variables. Real secrets are never
committed to `.env.example`, image layers, or CI artifacts.

Hosted PostgreSQL connection strings should require TLS (for example, Npgsql `SSL Mode=Require`
or stronger certificate validation supported by the deployment environment). Local Compose is
an explicitly documented exception.

Promotion means moving the exact image tag/digest produced by green CI. Rollback means
redeploying the previous known-good tag/digest without recompilation. Irreversible database
migrations require an explicit phased release plan before production.
