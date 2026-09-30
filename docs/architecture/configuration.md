# Configuration and secrets

The same immutable StrataAI2 images are intended to move between environments.

Required baseline production configuration:

- `STRATAAI_RUNTIME_MODE=production`
- `ConnectionStrings__Postgres`
- `STRATAAI_BUILD_REVISION`
- `STRATAAI_BUILD_VERSION`

Additional provider credentials are introduced only with the corresponding PRD and must be
provided by deployment secret management/environment variables. Real secrets are never
committed to `.env.example`, image layers, or CI artifacts.

Hosted PostgreSQL connection strings should require TLS (for example, Npgsql `SSL Mode=Require`
or stronger certificate validation supported by the deployment environment). Local Compose is
an explicitly documented exception.

Promotion means moving the exact image tag/digest produced by green CI. Rollback means
redeploying the previous known-good tag/digest without recompilation. Irreversible database
migrations require an explicit phased release plan before production.
