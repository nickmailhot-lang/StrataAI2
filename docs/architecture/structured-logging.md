# API and Worker console logs

ARCH-08-FR-003 requires structured diagnostic records with timestamp, severity,
component, build and meaningful correlation/context, without secrets or protected
content. Both hosts now select the shared `strataai-json` console formatter.
It writes one JSON object per line and uses the host assembly's embedded build
identity. Runtime environment settings, formatter configuration and log scopes
cannot supply a different service/build identity through this registration.

## Record fields

| Field | Meaning |
| --- | --- |
| `Timestamp` | UTC time at formatting, in ISO 8601 form |
| `LogLevel` | .NET severity name |
| `Component` | Fixed `strataai-api` or `strataai-worker` |
| `BuildRevision`, `BuildVersion` | Validated embedded host identifiers |
| `Category` | Logger category, normally the emitting type |
| `EventId`, `EventName` | Existing structured event identity |
| `CorrelationId` | Approved scope/entry reference; null when unavailable |
| `OrganizationId` | A typed nonempty Organization/Tenant GUID; null when unavailable |
| `Message` | Existing controlled application message, JSON escaped |
| `ExceptionPresent` | Whether an exception accompanied the event |

Correlation references accept a nonempty GUID or at most 64 ASCII letters,
digits, dots, hyphens and underscores. Entry identifiers take precedence over
available outer-scope identifiers. Invalid strings do not become structured
context. An identifier in diagnostic state does not grant application access or
prove authorization. The formatter reads no request, response, database or
business object to invent missing context.

## Privacy and failure handling

Only the identifiers above are collected from structured state/scopes. Arbitrary
scope text and other state properties are not serialized. Exception messages,
stacks and objects are omitted; the message formatter receives no exception.
Controlled messages remain subject to the application logging policy: never
pass credentials, addresses, tokens, connection strings, queries, private paths
or content bodies as message arguments. The formatter is not a general content
redaction engine. Framework request-start URL logging remains at Warning in
both hosts to avoid transport tokens appearing at the default Information level.

A failing scope enumerator or message formatter preserves fixed metadata; a
failed message renderer produces `Log message unavailable.`. Console serialization
or writer failure is best effort and does not recursively log another failure.
Existing protected API/Worker diagnostic paths continue to preserve responses,
committed jobs and subsequent processing when optional providers fail.

These records cover managed console logging. Runtime/native stderr and separately
configured logging providers require their own handling. Business/security
history remains in append-only AuditEvents; metrics and correlation follow the
[operator metrics](operator-metrics.md) and [build identity](build-identity.md)
contracts. This does not establish full tracing, production alerts or complete
ARCH-08 acceptance.

## Upgrade and verification

New builds use JSON console records by default. Update log consumers that expect
the former plain-text console layout; use named fields and the embedded build
identity for support correlation. Earlier retained release candidates keep their
own log format and pinned guide. No application route, database schema, business
permission or frontend behavior changes.

The mandatory commands integration job now reads the exact-image API/Worker
console streams privately and checks their record shape, UTC times, service/build
identity and identifier types. The `console-records-<revision>` artifact retains
only fixed verification flags, build identity and aggregate record counts,
staged with canonical metadata and checksums. Raw records, messages and context
identifiers remain outside that artifact. Source tests reject malformed records,
duplicates, missing/private fields, spoofed builds, invalid identifiers and unsafe
types; CI coverage guards require both source refusals and the runtime verifier.

The Release solution builds with zero warnings/errors. Linux execution passes
**82 focused .NET cases**, including all **15 formatter cases** and existing
HTTP/Worker/metrics diagnostic-failure checks. All **931 source/415 binary**
hashes match. Separate current compiled Demo API and Worker hosts emit **34 actual
records** (20 API, 14 Worker), all with the fixed service/build fields. Runtime and
formatter configuration spoofs are ignored, the rejected API request preserves
its correlation reference, and both hosts stop gracefully with exit code zero.
The same CI validator accepts these captures without retaining their contents.
All **555 source/71 binary** inputs match; both fixture audits restore the original
Docker inventory and remove owned containers. No original database is used.

All eight console-evidence tests and **255 CI coverage guards** pass; workflow
lint, diff checks and the 451 local targets in the touched reading guides pass.
These compiled Demo hosts use retained runtimes without building/downloading
images; they do not establish Production database/provider or current immutable
image acceptance. Current exact-commit CI and full ARCH-08 remain required.
Large-load testing stays deferred until after the operational MVP.
