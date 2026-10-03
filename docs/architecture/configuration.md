# Configuration and secrets

The same immutable StrataAI2 images are intended to move between environments.

Required baseline production configuration:

- `STRATAAI_RUNTIME_MODE=production`
- `ConnectionStrings__Postgres`
- API-only `STRATAAI_AUTH_RETRY_CURRENT_KEY` and `STRATAAI_AUTH_RETRY_KEYS`, supplied by runtime secret management. The key ring maps unique versions to base64 32-byte secrets; retain old versions while sign-in receipts remain live. See [sign-in retries](../identity-login-retries.md).
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

Attachment runtime is explicitly enabled with `STRATAAI_ATTACHMENT_STORAGE_ENABLED=true`
in Production. Both API and Worker require `STRATAAI_ATTACHMENT_S3_BUCKET`, a
12-digit expected `STRATAAI_ATTACHMENT_S3_OWNER`, and supported explicit
`STRATAAI_ATTACHMENT_S3_REGION`. The official AWS SDK runtime credential chain
supplies credentials; use deployment-managed workload IAM or secret injection,
never checked-in credentials. There is no custom/HTTP endpoint or local storage
fallback. Disabled or absent enablement leaves existing deployments unchanged;
malformed enablement and incomplete enabled configuration fail startup.

The enabled Worker also requires explicit `STRATAAI_WORKER_ORGANIZATION_IDS` and
an absolute `STRATAAI_ATTACHMENT_SCANNER_SOCKET`. It registers the actual
lease-fenced PostgreSQL scan store, quarantine coordinator, ClamAV adapter and
`ATTACHMENT_SCAN` handler in the existing Organization job processor. The API
does not register Worker scan capabilities. Readiness checks the guarded database
and current bucket public-access/policy/ownership controls within two seconds;
Worker additionally requires the local scanner's framed PING/PONG response.
Provider failure returns the existing generic 503 without provider diagnostics.

For Linux deployment, combine the immutable-image `compose.release.yml` with
`compose.attachments.yml`. Set `STRATAAI_ATTACHMENT_SCANNER_DIRECTORY` to an
existing operator-owned directory containing `clamd.sock`. The overlay mounts
it read-only into Worker and refuses automatic host-directory creation; it
introduces no images or build step. Restrict directory/socket access to the
scanner and Worker identities and supply AWS credential-chain configuration
through deployment secret management. Provision the private S3 bucket, policy,
ownership controls and ClamAV/signature updates separately. The overlay does
not create cloud resources or a scanner daemon.

Runtime integration and synthetic/local-socket tests do not establish deployed
bucket/IAM, antivirus signature freshness, scan-limit policy, socket permissions,
full immutable container acceptance or complete file upload/download behavior.
