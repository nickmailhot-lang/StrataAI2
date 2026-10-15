# Operational MVP alert baselines (ARCH-08, partial)

The release now carries [alert rules](../../deploy/metrics/alerts.yml) and a
[Prometheus backend example](../../deploy/metrics/prometheus.example.yml) for the
existing [private Collector](operator-metrics.md). These are optional operator
assets, not a new mandatory service, telemetry vendor or application image.
Their presence does not configure notification delivery or establish production
service objectives. The earlier passing `5845da8b` bundle lacks these assets and
the later subsecond histogram repair; require a newer passing candidate before
using this package as release evidence.

## What the rules observe

| Alert | Condition | Continuous hold |
| --- | --- | --- |
| Collector unavailable | Any expected scrape target fails, or the `strataai-collector` job is absent | 2 minutes |
| API histogram unavailable | No API duration bucket at 0.75 seconds is present | 5 minutes |
| API errors | Completed 5xx or incomplete fault observations exceed 5%, with at least 20 requests in the five-minute window | 5 minutes |
| API latency | Fewer than 95% of duration observations are at or below 750 ms, with at least 20 observations in the five-minute window | 10 minutes |
| Worker loop failures | An escaping queue-loop failure counter increases in the five-minute window | 1 minute |
| Email delivery failures | A decided identity-email failed-attempt counter increases in the five-minute window | 1 minute |
| Organization backlog unknown | At least one configured scoped sampler reports unavailable | 2 minutes |
| Identity backlog unknown | At least one enabled identity-email sampler reports unavailable | 2 minutes |

The API latency rule counts the exact 0.75-second bucket; it does not interpolate
a quantile. These are baseline warning policies for review and calibration, not
measured p95, availability/error budgets, production capacity or an Enhanced
400-ms profile. All warning annotations are fixed. Aggregations remove source
labels; emitted rule labels contain only fixed severity/component values.
Backend external labels must identify only the reviewed environment, without
accounts, recipients, tenant identifiers, routes, credentials or private names.
The example targets one Compose API/Worker pair. It is not multi-replica
acceptance: per-replica coverage and cumulative-counter identity require review
before horizontal deployment.

Missing histogram data means unknown measurement coverage, not passing latency.
Backlog sampling is optional: missing samples do not prove a healthy Worker.
Organization observations require explicit Worker scopes; identity observations
require enabled mail and its restricted database capability. Counts and samples
can remain cached at the Collector until expiry. These rules cannot establish
immediate process/database readiness, complete queue coverage, successful email
delivery or backup freshness. Retain independent health/readiness, certificate,
backup/drill and provider monitoring. Collector failure does not mean core human
workflows are unavailable.

## Enable in a reviewed deployment

1. Require exact-candidate CI and release verification. Start the existing
   metrics overlay as documented in the [release guide](../release/README.md).
   Verify API and Worker identity, expected measurements and live scrape
   coverage before enabling alerts. Keep different environments separate.
2. Use the example with an existing Prometheus-compatible backend on the same
   host, or change its target to an approved private address. Host loopback
   `127.0.0.1:9464` is not the host from inside a different container. Do not
   publish OTLP or monitoring publicly to make the example reachable. Preserve
   `honor_labels: true` for the Collector's fixed resource job labels; Prometheus
   still creates scrape `up` with job `strataai-collector`.
3. Validate the rules/configuration with the backend's supported `promtool`,
   load `alerts.yml` via `rule_files`, and retain metrics in a protected durable
   store. Bind any local backend UI/API to loopback or an authenticated private
   endpoint. This repository does not start the backend or supply its storage.
4. Confirm that every contributing API replica has the same histogram schema.
   During rolling upgrades, scope selectors to the intended build/revision or
   wait until old series/window data has expired. Presence of one new replica's
   bucket does not establish coverage for all old replicas. Do not aggregate
   mixed schemas or assume the old bundle supplies the new bucket.
5. Configure the deployment's Alertmanager/notification routing, protected
   credentials, responsible operator and public environment label separately.
   Review thresholds against the chosen objectives and actual traffic. In
   staging, verify both firing and resolved delivery through the intended route,
   including backend/Collector loss. Record the result without message contents
   or private recipient information in CI artifacts.

Do not mark deployed alerting complete from a unit-test success. No notification
is sent by this package's verification. Quiet or idle traffic deliberately does
not establish API error/latency compliance; health probes and coverage warnings
remain necessary. Authentication refusals are not counted as API 5xx errors.
Triage via current private readiness/logs/metrics and preserved build identity;
do not infer an affected account or expose diagnostic/provider details publicly.

## Verification

The official [Prometheus 3.15.0 distribution](https://prometheus.io/download/)
is pinned by SHA-256 in
[the CI verifier](../../scripts/ci/test-operator-alerts.sh). It checks rule syntax,
the backend example and [rule scenarios](../../tests/operator-alerts.test.yml)
using the [official rule-test engine](https://prometheus.io/docs/prometheus/latest/configuration/unit_testing_rules/).
The archive/binary live only in an owned temporary directory and are removed
after validation. No Prometheus service/image or application dependency is added.

The local native verifier passes all three commands and **152 alert assertions
across nine synthetic scenarios**: healthy traffic, missing job, scrape outage
and recovery, error/latency hold periods and recovery, Worker/mail/backlog
degradation and recovery, quiet traffic, foreign namespace isolation, counter
reset and coarse legacy histogram coverage. Synthetic private labels are
deliberately supplied; firing alerts must retain only fixed labels/annotations.
These are simulated time-series values, not HTTP requests or a load test.

The verifier runs in a retained isolated runtime without network access or
application startup. Four source hashes match, the original Docker inventory is
restored, and **230,798,841 temporary bytes** are removed. No image is built,
pulled or imported. Source-quality CI makes validation mandatory before builds;
release integrity requires both operator assets and their checksums. Current
exact-image/full CI, live Collector/backend rule evaluation and actual tested
notification routing still remain required. ARCH-08/11 and the MVP milestone
stay open; large-load tests remain deferred until after MVP.

The actual CI wrapper also passes with an offline curl substitute supplying the
verified archive, and refuses a corrupt archive before execution. Both paths
remove their owned scratch directories. An initial valid-wrapper attempt fails
because tar tries to restore the archive's owner under dropped capabilities;
extraction now explicitly avoids restoring ownership/permissions and uses a
private umask. Final wrapper checks pass without adding privileges or relaxing
the checksum. The external network download and real notification route still
need hosted/deployed execution. All **259 CI mutation guards**, **37 release
artifact tests**, Bash syntax and workflow lint pass, including omission/skip/
ignored-error refusal and missing alert-asset refusal despite valid checksums.
