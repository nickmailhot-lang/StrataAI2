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

## Authentication policy validation

Omitting an authentication policy setting retains the documented mode default.
Supplying an empty, malformed or out-of-range value fails API startup in both
Demo and Production; values are never silently replaced or clamped. Errors name
the setting and its allowed form without echoing the supplied value.

| Setting | Allowed supplied values | Default |
| --- | --- | --- |
| `STRATAAI_AUTH_ALLOW_SELF_REGISTRATION` | `true` or `false` | Demo `true`, Production `false` |
| `STRATAAI_AUTH_REQUIRE_VERIFIED_EMAIL` | `true` or `false` | Demo `false`, Production `true` |
| `STRATAAI_AUTH_MIN_PASSWORD_LENGTH` | Integer from 8 to 128 | 12 |
| `STRATAAI_AUTH_SESSION_HOURS` | Integer from 1 to 720 | 12 |
| `STRATAAI_AUTH_SECURITY_TOKEN_MINUTES` | Integer from 5 to 1440 | 30 |

Boolean parsing is case-insensitive; integer parsing uses invariant culture.
Existing valid configuration keeps exactly its requested policy. Before upgrading
an environment that previously supplied invalid values, correct those settings
or omit them deliberately to select the defaults. An empty environment value is
not omission. This validation changes startup admission, not stored profiles,
passwords, issued sessions or security-token records.

## Runtime and provider configuration

Build revision/version are embedded in the image at build time and cannot be
set by runtime environment variables. The web's `/build-metadata.json`, API's
`/api/runtime` and Worker's `/runtime` report the identifiers of their actual
image assemblies/assets. See [build identity](build-identity.md).

Production Workers default `STRATAAI_ORGANIZATION_DELETION_DISCOVERY_ENABLED`
to `true`. They discover bounded Organization UUID pages for canonical accepted
deletions and pending terminal-event delivery; new Organizations need no manual
scope update for deletion. Set it explicitly to `false` to suspend automatic
discovery. Invalid values and enabled Demo discovery fail startup. The separate
`STRATAAI_WORKER_ORGANIZATION_IDS` scope still controls general Work-event and
provider processing for active Organizations. See
[deletion discovery](organization-deletion-lifecycle.md#automatic-production-deletion-discovery).

Production Workers also default `STRATAAI_ORGANIZATION_METADATA_DISCOVERY_ENABLED`
to `true`. This independent loop routes committed metadata source events with
bounded UUID pages and claims only metadata delivery jobs under tenant RLS.
New Organizations need no manual scope update for metadata readiness. Set it to
`false` to suspend metadata routing independently. Invalid values and enabled
Demo metadata discovery reject startup. See
[metadata routing and delivery](organization-metadata-events.md).

Production Workers default `STRATAAI_INVITATION_RECIPIENT_AUTHORITY_DISCOVERY_ENABLED`
to `true`. This independent loop discovers at most 100 Organization IDs per
page and claims only invitation recipient authority jobs under tenant RLS.
No `STRATAAI_WORKER_ORGANIZATION_IDS` update is needed for newly published
authority work. Set the flag to `false` to suspend this loop independently;
invalid values and enabled Demo authority discovery reject startup. Apply
migration 106 and provision the Worker capability grants before startup.
See [recipient authority delivery](invitation-recipient-authority.md#automatic-production-authority-discovery).

Additional provider credentials are introduced only with the corresponding PRD and must be
provided by deployment secret management/environment variables. Real secrets are never
committed to `.env.example`, image layers, or CI artifacts.

Production Workers default `STRATAAI_INVITATION_ISSUER_AUTHORITY_DISCOVERY_ENABLED`
to `true`. This independent loop leases private account-deactivation routing
jobs and publishes at most 100 owning Organization authority roots per page.
It requires migrations 109–110 and the provisioned Worker capability grants. The
recipient authority discovery loop must also be enabled to deliver those roots.
Set the issuer flag to `false` to suspend only account-source routing. Invalid
values and enabled Demo issuer discovery reject startup. No configured
Organization IDs are required. See [issuer account authority](invitation-recipient-authority.md#canonical-issuer-account-deactivation).

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
`ATTACHMENT_SCAN` handler in the existing Organization job processor. A successful
supported-image scan atomically queues `ATTACHMENT_PREVIEW`; the enabled Worker
registers its private intent/publication store, anonymous integrity staging,
isolated generator, storage recovery and preview handler. The API does not register
Worker scan/preview capabilities. Default/older Worker claims exclude preview jobs;
only explicitly enabled Workers opt into their transaction-scoped claims.
Readiness checks the guarded database
and current bucket public-access/policy/ownership controls within two seconds;
Worker additionally requires the local scanner's framed PING/PONG response.
Provider failure returns the existing generic 503 without provider diagnostics.

Original objects retain `attachments/{Organization:N}/{Attachment:N}` keys.
Private previews use `attachment-previews/{Organization:N}/{Job:N}`. Bucket/workload
permissions for enabled preview delivery must cover that separate namespace under
the same private ownership/public-access controls. Server-owned identities select
both namespaces; client filenames, paths and prefixes never select storage keys.

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

Enabled attachment runtime also configures `STRATAAI_ATTACHMENT_MAX_BYTES`
(default 20 MiB / 20971520 bytes, bounded to 1 GiB) and
`STRATAAI_ATTACHMENT_ALLOWED_TYPES` (default `image/png,image/jpeg,image/webp,application/pdf`).
The allowlist must be a nonempty unique subset of these exact MIME values;
unsupported active content, empty/duplicate entries and invalid byte limits fail
startup. Actual-byte inspection determines MIME; request MIME and filenames
cannot select an allowed type. Size is checked in original-intent admission and
writer claiming; measured Stored-file publication rechecks both current size
and type policy. Configure scanner StreamMaxLength and operational deadlines
for the selected upload bound; protocol PING alone does not prove those limits
or signature freshness.

Enabled Production hosts map `POST /cards/{cardId}/attachments`. Send a raw
`application/octet-stream` body with `X-StrataAI-Request: 1` and a mandatory
nonempty UUID `Idempotency-Key`. Bind the original request using
`X-Attachment-Name` (canonical base64 of strict UTF-8, at most 255 UTF-16
characters), `X-Attachment-Size` (decimal original byte count),
`X-Attachment-SHA256` (64 lower-case hexadecimal characters) and
`X-Card-Version` (original decimal revision). If supplied, `Content-Length`
must equal the original byte count. Filenames, digest claims and request MIME
do not establish verified metadata; the server inspects and measures actual
bytes before publishing Pending metadata and the durable scan job.

Retry with the same actor, Card, key, name, size, digest and original revision.
An active writer returns a fixed conflict; ambiguous provider writes retain a
reconciliation claim. Recovery proves absence or measures the original private
object before proceeding. Do not change claims or mint a new key merely because
an acknowledgment was lost. Cancellation does not prove an object was absent.
Pending/Rejected/Failed files have no normal download or preview access.

The file collection edge route streams without request-body disk buffering,
with a 1 GiB hard ceiling; Application applies the configured lower limit before
reading. Idle body timeout is 60 seconds, proxy send/read deadlines 330 seconds
and Application provider deadline at most five minutes. Normal Demo and disabled
hosts retain URL routes and do not map binary creation. Synthetic HTTP and
provider tests do not establish deployed bucket/scanner or complete FILE
lifecycle acceptance.

Enabled hosts also expose GET /cards/{cardId}/attachment-upload-options to
currently authorized editors. Its scoped/current-revision response supplies the
configured byte ceiling and sorted type subset for review. Reading these options
never grants an upload or skips subsequent current admission/policy checks.
