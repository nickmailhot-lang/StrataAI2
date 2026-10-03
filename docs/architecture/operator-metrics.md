# API operator metrics export (ARCH-08, partial)

The existing native `StrataAI.BoardSharing` and `StrataAI.ChecklistClient` meters
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
