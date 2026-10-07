# Runtime modes

StrataAI2 has two explicit runtime modes controlled by `STRATAAI_RUNTIME_MODE`.

## Demo

`demo` is deterministic, local, and isolated from production providers.

- PostgreSQL is not required.
- Production AI, mailbox, OAuth, and vendor integrations are not resolved.
- Versioned sample data is packaged in code.
- `GET /api/demo/state` returns current sample state.
- `POST /api/demo/reset` restores the deterministic sample state.
- `DELETE /api/demo/state` clears it.

These endpoints are not mapped in Production.

Authenticated invitation commands also maintain a private, process-local
[recipient source journal and replay reader](invitation-recipient-events.md).
Its source history and counters commit or roll back with the existing invitation
transaction. API restart resets this Demo history; sample-catalog reset endpoints
do not reset it. Both modes expose protected recipient SignalR replay, consumed
by the invitations page for future actual invitation transitions. See the linked
journal guide for protected recovery, unfinished authority invalidation and
remaining native acceptance evidence.

Demo also journals canonical Organization creation/editing, membership changes
and Internal Organization invitation transitions in the original transaction.
Both modes expose [protected metadata HTTP and SignalR replay](organization-metadata-replay.md#demo-metadata-replay),
including browser draft preservation and reconnect recovery. Demo publication is
synchronous in-memory simulation; Production retains its separate durable Worker.
API restart resets the journal, while sample-catalog reset leaves it intact.

Demo automatically processes committed deletion requests through a
[bounded graph simulation](organization-deletion-lifecycle.md#demo-bounded-graph-simulation)
hosted inside the Demo API. It traverses actual attachments, Cards, Lists and
Boards, retains history, then commits the terminal Organization audit and
canonical completion source together. Protected Owner observations and member
HTTP/SignalR lifecycle recovery consume that source. A 202 acknowledges the
request; an empty graph or unavailable ordinary read never establishes completion.

This simulation is process-local and does not use external providers. Failed
processing restores the graph, history, parent and source before retrying. The
accepted request supplies the original actor authority independently of a
browser session; current account/membership/session checks still protect reads.
API restart loses this Demo state, while sample-catalog reset leaves it intact.
Production retains its separate Worker, restricted PostgreSQL durable jobs,
leases and transactional outbox. Demo completion does not prove physical object
erasure or backup expiration.

### Demo sign-in

Set `STRATAAI_RUNTIME_MODE=demo`, start the API and web application, and open
the web application's `/login` page. Each fresh Demo API process seeds this
verified, active account; no registration or email verification is required:

| Field | Value |
| --- | --- |
| Email | `demo@strataai.test` |
| Password | `StrataAI-Demo-2026!` |
| Display name | Demo User |

The account signs in through the normal password and session flow. It starts
without Organization memberships; create an Organization after signing in to
explore the collaboration workflows. The `/api/demo/state` sample catalog is
separate from authenticated Organization membership and work data.

The Demo API also starts with ASP.NET's Development service validation enabled
when binary attachment storage is disabled. Its attachment services compose
without resolving an object-storage provider: protected reads return no bytes,
and binary uploads remain unavailable. URL attachments retain their existing
workflow.

These are public test credentials, seeded only in Demo's in-memory identity
store. Production uses its PostgreSQL identity store and does not seed this
account. Account changes and sessions last for the current API process. Restart
the Demo API to restore the original credentials; `/api/demo/reset` and
`DELETE /api/demo/state` affect only the sample catalog, not accounts or sessions.

Return to the [project README](../../README.md#demo-sign-in) or the
[documentation index](../README.md).

`DemoAccountTests.cs` covers sign-in with these documented credentials without
registration, rejection of a wrong password without a session, and Development
startup with binary attachment storage disabled. All three focused API-host
checks passed again on 2026-10-07. The seeded account has no implicit Organization
access; these checks do not establish complete authentication PRD acceptance.

### Demo isolation verification

Both API and separate Worker isolation are mandatory retained-image CI gates.
The [Worker isolation runner](../../scripts/ci/test-demo-worker-network-isolation.sh)
starts the already-built Worker with `--network none`, no published ports and
only Demo/Development/log-level settings. It verifies Demo readiness, Worker
health and safe runtime diagnostics through loopback using the image's existing
curl, and confirms the process remains running. It installs no packages and
rebuilds no images. Its trap retires the uniquely named test container.

Eight [Worker refusal fixtures](../../scripts/ci/test-demo-worker-network-isolation-fixture.sh)
cover success, an external network, published ports, a stopped process, non-Demo
readiness, a different service, non-Demo runtime diagnostics and HTTP diagnostic
failure; every started fixture is retired. These execute in the source gate.

The unmodified Worker runner passed locally against the current compiled Worker
mounted read-only in a cached framework/runtime image, with networking disabled
and zero published ports. All eight refusal cases passed. This is current-source
runtime evidence, not proof of the current retained release image; that requires
the exact-commit CI gate. Demo Worker health does not establish Production job,
mail, provider or retention acceptance. Production still uses the separate Worker
with its restricted durable PostgreSQL stores and explicit providers.

[ARCH-05](https://github.com/nickmailhot-lang/StrataAI2/issues/86) requires the
documented workflows to operate without an external network. Merely omitting a
PostgreSQL connection string does not establish that boundary. The retained-image
CI check now starts the exact API image with Docker `--network none`, no published
port, no PostgreSQL and no production provider credentials. A namespace-local
client reaches only the API's loopback interface. Readiness must identify Demo
before sample-state/reset and the existing authentication/workflow checks run.

The API runner also verifies the packaged version-1 catalog's canonical IDs and
names, clears it and reads back the empty catalog, then resets and reads back
the exact original sample. These checks passed against an actual current-source
API with networking disabled. They prove process-local catalog reset rather than
resetting accounts, sessions or independently created collaboration data.

The [isolation runner](../../scripts/ci/test-demo-network-isolation.sh) reuses
[the complete Demo smoke suite](../../scripts/ci/test-demo-auth.sh): documented
seeded login, wrong-password refusal, sample-reset session preservation, CSRF,
profile concurrency/recovery, Organization lifecycle, Internal/Portal invitation
separation, Board/List/Card workflows, password-reset session revocation and
account-deactivation Owner continuity. It adds no package to the application
image and does not rebuild it. All existing smoke assertions are retained.

The runner uses the Linux CI host's `sudo nsenter` with a Docker daemon on that
same host; it enters only the target API network namespace. This is test-client
tooling, not a runtime requirement. Docker Desktop users can run a client inside
the API namespace instead. Ordinary Demo startup still requires neither nsenter
nor production providers. The runner stops its unique disposable API on success
or failure. Seven [refusal fixtures](../../scripts/ci/test-demo-network-isolation-fixture.sh)
pass for success, an external-network namespace, an invalid process reference and
non-Demo readiness, wrong sample version/content, failed clear and failed reset;
unconfirmed prerequisites never reach the smoke suite.

This check complements the fourteen desktop/phone metadata, terminal and Portal
recipient lifecycle scenarios on retained Demo API/web images. It does not certify all
provider integrations, physical object erasure or complete release acceptance.

The isolation check exposed a Demo discovery mismatch: the legacy
`GET /organizations` included DELETING/DELETED parents whose historical membership
was still active. Production already excludes both states. Demo now applies the
same filter without removing memberships, audit history or independently
protected completion recovery. A real API-host regression first failed on the
pending parent, then checks both pending and completed withdrawal for Owner and
member while a separate active Organization remains available.

All 55 selected PRD-03 API-host cases pass after the filter repair, with a fresh
zero-warning full solution build. The unchanged complete Demo smoke suite also
passes locally against the readonly fixed compiled API in `network=none`, using
namespace-local curl with no published port or PostgreSQL/provider credentials.
That local mounted runtime does not establish retained-current-image identity.
The previous `6bee3ce5` CI source gate passed 739 domain, 600 API-host and 1,897 web
cases plus PostgreSQL integration, but its Demo smoke gate failed on this discovery
mismatch. Current retained-image confirmation remains required.

CI run [37660730995](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37660730995)
at `19f77ea1` subsequently passed the exact retained API's complete no-network
Demo workflow step, embedded build identity checks and Production missing-database
startup refusal. Its source gate, image builds and image security scans passed.
This confirms the repaired API isolation gate at that revision; the container job
and full release were still running when inspected. Later Worker isolation,
catalog clear/reset, policy validation and composition increments still need
their own exact-commit pipeline outcomes. No whole-ticket closure follows from
this older scoped gate pass.

## Production

`production` fails startup unless `ConnectionStrings__Postgres` is configured.
Production never falls back to sample persistence.

Both API and Worker expose safe runtime/build diagnostics and readiness. Environment-specific
configuration remains outside container images.

### Production API composition verification

[Runtime composition tests](../../tests/StrataAI.Api.Tests/RuntimeCompositionTests.cs)
boot the actual Production API with Development service validation and resolve
23 implemented persistence/replay/job boundaries. Identity, Organization,
invitation, Work, notification, watch, Reminder and background-job contracts must
resolve the expected PostgreSQL implementations. No Demo catalog, Demo deletion
simulation or Demo hosted service is registered. The normal Production identity
policy remains invitation-based and requires verified email.

A separately enabled Production mail case resolves the durable invitation
publisher and shared identity/invitation token signer. The API does not resolve
an email transport: actual provider sending remains in the separate Worker.
Disabled optional mail also remains absent rather than selecting a Demo sender.

Actual Production GET/DELETE catalog and POST reset requests return 404; safe
runtime diagnostics still report Production. Missing, empty and unknown modes
refuse actual API startup. All six cases pass with a zero-warning API/test build.
The fixture uses an explicit unreachable database endpoint and does not replace
stores, execute database commands or send email. These checks are registration,
route and startup evidence; restricted PostgreSQL behavior, Worker/provider
delivery, other future provider modules and current retained-image/full-release
acceptance require their own evidence before ARCH-05 can close.

### Retained-host startup refusal verification

The [startup refusal gate](../../scripts/ci/test-runtime-startup-refusals.sh)
replaces the previous API-only missing-database check. It runs twelve actual
startup cases against the already-built API and Worker images, with networking
disabled and no published ports: both hosts reject empty/unknown runtime modes
and missing Production persistence; API startup rejects malformed verified-email
policy, excessive session lifetime and a weak password minimum in both modes.
Policy cases use a deliberately unreachable fixture database configuration.

Each case must terminate unsuccessfully with its expected configuration error.
A successful start, timeout or unrelated crash cannot prove refusal. Failure
diagnostics report the fixed case number without printing the captured startup
body, and the test's invalid policy value must not appear in that body. Temporary
logs and the uniquely named test container are cleaned up on exit.

Five [runner refusal fixtures](../../scripts/ci/test-runtime-startup-refusals-fixture.sh)
pass: intended refusals, unexpected successful startup, unrelated crash, timeout
and disclosed policy input. These are mandatory source checks. The unmodified
startup runner also passed all twelve actual host cases locally using current
compiled assemblies mounted read-only in cached runtime images. This is current
host behavior evidence, not current retained-image identity or whole-release
acceptance. The exact-image invocation is mandatory in container integration.
