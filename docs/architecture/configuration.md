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

The release Compose file forwards all five settings to the API and the
verification requirement to Worker handlers. Set them in the deployment `.env`
or runtime environment. When omitted, the production Compose defaults match
the table above. Explicit empty or malformed API values remain supplied values
and reach startup validation; Compose does not replace them with defaults.
See [release authentication configuration verification](release-auth-policy-verification.md).
Existing valid configuration keeps exactly its requested policy. Before upgrading
an environment that previously supplied invalid values, correct those settings
or omit them deliberately to select the defaults. An empty environment value is
not omission. This validation changes startup admission, not stored profiles,
passwords, issued sessions or security-token records.

The mandatory [retained-image startup gate](runtime-modes.md#retained-host-startup-refusal-verification)
checks six real invalid-policy API startups across both modes, plus both hosts'
runtime-mode and missing-database refusals. It requires the intended startup error
and rejects unrelated crashes/timeouts. All twelve cases passed locally with
current compiled hosts in cached runtime images; exact current CI remains required.

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

### Production public origins

Both `STRATAAI_PUBLIC_ORIGIN` and `STRATAAI_REALTIME_PUBLIC_ORIGIN`, when
configured in Production, must be absolute HTTPS origins without credentials,
paths beyond `/`, query strings or fragments. The shared runtime validates
each independently for API and Worker, before creating database dependencies.
A valid realtime override does not excuse an insecure general public origin;
disabling identity mail does not disable this validation. Errors name only the
setting, never the supplied URL or its potentially private values.

HTTP is permitted only for local loopback origins such as
`http://localhost:8088`, `http://127.0.0.1:8088` and `http://[::1]:8088`.
Private LAN addresses, wildcard bind addresses and external hosts require HTTPS.
This exception supports the local Compose/browser workflow; it is not a public
deployment TLS termination configuration. Configure the public HTTPS origin
when deploying behind your TLS proxy. Omitted/empty origins retain existing
optional-feature behavior. Demo retains its existing origin policy.
Enabled identity email still requires an HTTPS general public origin, including
for local links; this stricter delivery requirement is unchanged.

The [origin tests](../../tests/StrataAI.Api.Tests/ProductionOriginTests.cs)
reproduce 30 failures among 49 cases against the previous runtime. All 49 pass
after the repair; together with transport and actual runtime composition tests,
87 focused Debug tests pass. The Debug solution builds with zero warnings/errors.
Eight actual compiled Debug API/Worker startups reject both unsafe origin
settings, with and without a secure alternative setting, without echoing input.
These starts refuse before database setup and do not use Docker or a database.
The [retained-image refusal gate](../../scripts/ci/test-runtime-startup-refusals.sh)
now requires 24 cases, including eight origin refusals. Its seven self-test
scenarios pass, including rejection of disclosed origin values. Exact-image CI
for this repair remains required; these local checks do not prove deployed TLS
termination or complete ARCH-12 acceptance. Large-load testing remains deferred.

### PostgreSQL transport

API, general Worker and identity-mail database connections default to
`SSL Mode=VerifyFull`: encryption, certificate trust and server-name validation
are required. Install the hosted database's CA in the runtime trust store or
supply Npgsql's `Root Certificate` setting through the connection string.
An explicit `Require`, `VerifyCA` or `VerifyFull` mode is preserved; `Require`
encrypts without validating the server identity. `Allow` and `Prefer` are refused
because they permit plaintext fallback. Omitted GSS encryption is disabled so
ambient Kerberos credentials cannot replace the TLS transport; explicitly
enabled GSS encryption is refused by this TLS contract.

For structured credentials, `STRATAAI_DATABASE_SSL_MODE` accepts `Require`,
`VerifyCA`, `VerifyFull` or `Disable`. Omission selects `VerifyFull`; an empty,
unknown or numeric setting refuses startup. A supplied
`ConnectionStrings__Postgres` takes precedence over structured credentials;
put `SSL Mode` in that connection string. The separate
`ConnectionStrings__IdentityDeliveryPostgres` uses the same transport guard.
Refusal messages do not echo connection strings, passwords or parser errors.

Plaintext requires an explicit `SSL Mode=Disable` and every configured host must
be the private Compose service `postgres`, a loopback address/`localhost`, or a
Unix socket directory. External and mixed local/external host lists are refused.
The bundled local Compose files explicitly set
`STRATAAI_DATABASE_SSL_MODE=Disable` for their private PostgreSQL service;
restricted CI connection strings also state their local exception explicitly.
For the optional mail role on that service, include `SSL Mode=Disable` in its
separate connection string. Do not reuse that exception for a hosted database.

The previous application inherited the driver's `Prefer` default and ignored
structured SSL-mode configuration. Operators upgrading from that behavior must
configure a trusted hosted certificate or an explicit supported mode; an
unencrypted external server will no longer be accepted. See
[Npgsql's TLS modes](https://www.npgsql.org/doc/security.html#encryption-ssltls).

Verification for the transport repair: the pre-change implementation fails
19 of the initial 27 focused cases; all 31 final
[transport cases](../../tests/StrataAI.Api.Tests/PostgresTransportTests.cs) pass.
A real loopback TCP probe observes the driver's SSL request and then refuses
TLS: the repaired client closes without sending a plaintext startup packet.
The original client sent a subsequent byte instead. Four compiled Release
API/Worker startups reject external plaintext through both connection-string
and structured configuration, without echoing the supplied credential.
The [retained-image startup gate](../../scripts/ci/test-runtime-startup-refusals.sh)
included those four cases, bringing its required total at the transport repair
to 16. The origin repair above extends that total to 24; execution for the new
immutable candidate remains required.

The refusal runner's Docker stand-in exercises all 16 intended failures and
refuses successful startup, unrelated crashes, timeouts and disclosed policy
or transport values (six fixture scenarios). CI at `70bb1f68` exposed its stale
12-case fixture expectation before compiling .NET; the fixture now recognizes
the new transport cases, and its credential-disclosure probe must fail at the
first TLS case. The complete updated fixture passes locally. Both runners
resolve and check their uniquely named temporary directories before cleanup.

The Release solution builds with zero warnings/errors. The 242 CI/shard/startup
guards, workflow lint and 11 actual Compose-rendering checks pass. A complete
Windows Domain run reports 1,017 passed and 16 failed; the pre-existing Debug
binaries without the transport guard reproduce the same 16 cases and diagnostics
apart from random fixture IDs. They concern Windows timezone data, Unix sockets,
symlink privileges and the Linux Worker path. This is an explicit local platform
limitation, not a green full-suite claim. The full API run and new candidate's
Linux/retained-image CI remain pending; the earlier `24f56246` release does not
certify this repair or deployed hosted certificates.

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
