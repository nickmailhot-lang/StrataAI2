# API operator metrics export (ARCH-08, partial)

The native `StrataAI.BoardSharing`, `StrataAI.ChecklistClient`,
`StrataAI.ActivityClient` and `StrataAI.Organizations` meters
can now export through OpenTelemetry .NET 1.19.1 using OTLP HTTP/protobuf.
The API registers the exporter only when `STRATAAI_METRICS_OTLP_ENDPOINT` is
explicitly configured. `compose.release.yml` forwards this setting and optional
`STRATAAI_METRICS_OTLP_HEADERS` from external operator configuration. Nothing in
the immutable images embeds a collector address or credential.

Set the endpoint to an operator-controlled receiver's full metrics URL, for
example `http://collector:4318/v1/metrics` on a private deployment network or an
HTTPS endpoint. Optional headers use the OTLP exporter's header format; keep
credentials in deployment secret configuration. URI user info, query, fragment,
non-HTTP schemes, relative URLs, excessive URL length and a missing `/v1/metrics`
suffix are rejected with a generic startup error. An unset endpoint leaves native
instruments available without starting an exporter or assuming a localhost
receiver. Standard OTEL environment variables do not implicitly enable this path.

The exporter sends cumulative counters/histograms every ten seconds, with a
three-second export/HTTP timeout and no HTTP redirects. Collector availability
is not a core application readiness dependency. Collection uses the existing
bounded labels; resource metadata is limited to fixed service name/namespace and
embedded build revision/version. No resource detectors, automatic HTTP/database
instrumentation, application logs or traces are registered by this change.
Request URLs, SQL, host identity, tenant/actor/object IDs, keys and business
content are not intentionally exported. Client observations remain untrusted and
are not authoritative audit records.

`OperatorMetricsTests` compiles eight cases covering disabled/invalid settings,
actual OTLP serialization/transport into an isolated test HTTP handler, inclusion
of both Checklist metric types/fixed resource identity, exclusion of an unrelated
Meter, and collector failure without a thrown product exception. This transport
fixture does not prove a deployed collector can ingest or retain the data, nor
that dashboards/alerts work. Local warning-as-error build, locked restore and
NuGet vulnerability audit pass; Linux test execution is pending. Windows
Application Control prevents local .NET execution and has not been bypassed.

Remaining work includes exact-image collector ingestion and retained evidence,
operator dashboards/alerts, Worker/backlog/provider/database/backup instruments,
tracing/correlation, encrypted backup/restore proof and full ARCH-08 acceptance.
This export foundation does not close ARCH-08 or PRD-13.

References: [official exporter contract](https://opentelemetry.io/docs/languages/dotnet/exporters/)
and [pinned 1.19.1 OTLP options](https://github.com/open-telemetry/opentelemetry-dotnet/blob/core-1.19.1/src/OpenTelemetry.Exporter.OpenTelemetryProtocol/README.md).

Run 37107966645 at 1827732 executed 256 API-host tests successfully but failed
the new export case at ForceFlush: its fake transport implemented SendAsync only,
whereas the pinned exporter uses synchronous Send on Linux/.NET. The fixture now
implements both paths without changing the successful-flush, wire-content or
privacy assertions. The repaired suite compiles with zero warnings/errors; new
Linux execution is pending. No successful exporter execution is claimed yet.

## Optional receiver and ingestion gate

`compose.metrics.yml` adds a digest-pinned official OpenTelemetry Collector
0.161.0 to the release topology. Start it with
`docker compose -f compose.release.yml -f compose.metrics.yml up -d`.
It listens for OTLP only on the deployment's private Compose network. Its
Prometheus scrape port is bound to host loopback at `127.0.0.1:9464/metrics`.
Treat that network and host access as operator boundaries. No public app route
proxies metrics. The receiver is optional and is not a fourth application image;
the three build-once app images remain identical. It uses a read-only config/root,
dropped capabilities, no-new-privileges, a 256 MiB container limit and bounded
memory/batches. Only four service/build resource attributes become metric labels.
It retains aggregate scrape state in memory; restart recovery, persistent history,
production HA, dashboards and alerting remain separate unfinished requirements.

CI now starts this receiver with the immutable release API and validates its
configuration with the pinned binary. A mandatory stage creates a disposable
private Checklist fixture through Nginx, records a legitimate aggregate batch,
rejects anonymous/extra-private-field batches, then polls actual collected metric
families. It verifies exact client counts (denied/invalid batches cannot inflate
these), server counters/durations and current embedded build metadata, excludes
private fixture content/UUIDs/unexpected labels and retains only fixed booleans,
collector version and commit. Raw scrapes, accounts, scope IDs and credentials
are disposable and are not retained. Three verifier tests cover success, missing
families/wrong builds, private content/identity/keys and unknown families. They
pass locally; shell syntax and diff checks pass. Local Docker and YAML libraries
are unavailable, so collector/configuration/topology execution remains unproven
until Linux CI passes. The receiver's SBOM and fixed Critical vulnerability gate
use the same policy as the app images. Optional Compose/config files are included
in the release bundle. This does not complete ARCH-08 or PRD-13.

Collector reference:
[pinned Prometheus exporter configuration](https://github.com/open-telemetry/opentelemetry-collector-contrib/blob/v0.161.0/exporter/prometheusexporter/README.md).

The synchronous transport repair alone did not pass: run 37108290219 at 4201d99
still failed ForceFlush (256 host cases passed). Inspection of the pinned SDK
showed the combined exporter/reader callback applies inline after named options,
replacing the injected test transport. Exporter and reader settings now register
through named options, and AddOtlpExporter uses the named single-options overload
without an inline delegate. Endpoint, resource, timeout, redirect and collection
policies are unchanged; the real successful-flush/private-field assertions remain.
The solution builds with zero warnings/errors; repaired execution is pending CI.

Subsequent Linux run 37108766526 at 14c58bd passed all 246 Domain and 257
API-host tests, including optional SDK transport/ForceFlush cases. The named
options repair is now executed evidence. Its independent notification focus
failure skipped image/receiver stages; e9b5d15 repairs that case, with all 872
local web cases passing. Actual deployed Collector ingestion remains pending.

Exact-image receiver evidence is now executed at e9b5d15, run 37109396890,
container job 111165055788: pinned Collector configuration validation and real
OTLP ingestion/private-field checks both passed. Retained artifact 11269482094
contains only schema/revision/status/topology/Collector version and six true
verification flags: client events/duration, server requests/duration, fixed build
identity and excluded private fields. The downloaded JSON was inspected and
matches the exact revision. Security job 111165055801 also passed. This proves
native metric export into the actual pinned receiver; it does not prove dashboards,
alerting, persistent history/HA, backup restore or the rest of ARCH-08. The run's
browser stage remains live, so full required-ci/release success is not claimed.

## Organization server outcomes (PRD-03)

The Organization meter records `strataai.organization.requests` and
`strataai.organization.duration` (seconds) for matched create/list/read/update,
member review/removal, departure, directory/surface admission, deletion request
and independently admitted deletion observation, and metadata/lifecycle replay.
Labels contain only a fixed operation, outcome, allowlisted stable error code,
and a Boolean indicating a well-formed retry key. They contain no names,
description/logo content, user/Organization/object IDs, email, route values,
correlation IDs, cursors or actual retry keys. Repeated keyed acknowledgment
counts as a successful request, not another business mutation.

Duration includes the normal authorization and handler path. Authentication and
rate-limit refusals are counted for matched routes; unmatched paths add no new
operation label. Listener/export failures cannot change the product result.
Histograms permit operator latency quantiles; this instrumentation alone does
not establish the PRD p95 target or feature adoption by tenant. No Organization
identifier is exported to manufacture tenant-level analytics.

The mandatory exact-image Collector fixture now requires real Organization
creation success and an unauthenticated Organization read denial, plus request
and duration observations and the existing private-field/build checks. Missing
Organization observations fail its verifier. Local verifier tests cover missing
samples, wrong build metadata, protected labels/values and unknown families.
Current exact-image ingestion remains pending. Browser use/retry/error and live
recovery rates, tenant adoption policy, dashboards/alerts and broader PRD-03
telemetry acceptance remain unfinished.

Local validation for the Organization increment: the Release solution build
passed with zero warnings/errors; the real API outcome/privacy case passed;
all eight operator configuration/export/failure cases and three Collector
verifier tests passed. The operator transport case now also requires both
Organization metric families in actual serialized OTLP. Bash and diff checks
pass. Deployed Collector ingestion for the current images remains pending.

## Organization settings browser observations

The existing bounded Activity client queue now admits three fixed categories:
`organization_settings_disclosure`, `organization_settings_read` and
`organization_settings_update`. Settings counts one open only after both account
checks and current administrative admission; normal reads/saves, explicit
refresh/original retries, conflicts, exceptions and current results are observed.
Live reset/unavailability records a recovery retry request, not a claim that a
socket successfully reconnected. Results include bounded client duration. Reports
retain only action/kind/count/duration and cannot include draft fields, names,
logo URL, account/Organization IDs, revisions, keys, routes or error text.

These are untrusted best-effort operator observations. Reporting failure does
not retry a business command or change the saved draft, original request,
account binding, full-operation deadline or current-authority checks. The server
requires authentication and rejects a batch containing any unrecognized/private
field before recording any member. The existing optional OTLP export covers
them; its exact-image fixture now requires these fixed settings categories and
an update duration. That fixture submits synthetic client observations to prove
transport/ingestion, rather than claiming a real browser produced them.

Four component cases cover admitted open/read/live recovery, exact original save
recovery, conflict/failure, and no open before admission, with private wire-field
assertions. The native desktop/phone settings telemetry cases use a real committed
PATCH whose response is lost, explicit identical-key/body recovery, actual
production browser reports accepted by the authenticated endpoint, one stored
revision, keyboard operation and WCAG 2.2 AA. Native execution remains pending.
Broader Organization member/departure/deletion/invitation telemetry,
per-tenant adoption policy, successful transport reconnect measures and dashboards
remain separate unfinished requirements.

Local settings validation passed 38 focused component/queue cases, four API
category/authentication/atomic-private-batch cases, API/API-test Release builds
with zero warnings/errors, three Collector verifier tests, web/browser
typechecks and zero-warning lint. Both native cases collect successfully.
The two native scenarios are implemented; current-image runtime evidence remains
pending and this does not close PRD-03 or ARCH-08.

## Organization creation client observations

The creation dialog records one `organization_creation_disclosure/open` per
mount. Admitted initial attempts and explicit original recovery record
`organization_creation/use` or `retry`, respectively. Caught uncertainty records
`exception`, a 409 records `conflict`, and completed attempts record success or
failure with their full-operation duration. Success requires the original
receipt, current Organization access and final account confirmation; receipt
recovery alone is insufficient. Invalid local input is not a submitted attempt.

The observations use the existing bounded best-effort queue and authenticated
whole-batch parser. Only action, kind, count and optional duration are allowed;
account/Organization IDs, names, descriptions, original keys, paths, revisions
and exception text are excluded. Reporting cannot retry a creation or change
its original account/key/body, deadline or authority checks. These observations
remain untrusted analytics, separate from audit history and server metrics.

Component cases cover exact original recovery, conflict/failure and refusal of
current access after a valid receipt, checking private-field exclusion. API
cases cover authentication, accepted fixed categories and atomic rejection of
mixed private batches. The exact-image Collector fixture now requires fixed
creation categories and a duration; its client observations are synthetic
ingestion evidence. Separate desktop/phone browser cases lose a real committed
creation response, recover the identical key/body, require one stored
Organization/version, actual production reports with 204 responses, keyboard
operation and WCAG 2.2 AA. Native runtime verification remains pending.

Local validation passed 23 focused creation/queue component cases, six API
category/authentication/privacy cases, three Collector verifier cases, web and
browser typechecks and zero-warning lint. API and API-test Release builds passed
with zero warnings/errors. Both native browser cases collect successfully. This
does not establish current-image execution or complete PRD-03 acceptance.
