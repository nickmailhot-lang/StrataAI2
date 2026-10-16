# API and Worker operator metrics export (ARCH-08, partial)

The [PostgreSQL pool recovery contract](postgres-pool-recovery.md) adds first
business-request persistence and unchanged API process checks after this
fixture's database restart. Readiness alone cannot establish application pool
recovery; the mutation must succeed on its first attempt.

The optional [operator alert package](operator-alerts.md) supplies tested warning
rules and a private backend example for these measurements. Deployment routing,
live evaluation and firing/resolved notification tests remain operator work.

## Remaining custom API instrument isolation — 2026-10-15

The follow-up to the Organization/Board repair covers the four other custom API
recorders: Checklist client observations, Activity client observations, HTTP
outcomes and authentication outcomes. Checklist/Activity still created optional
meters or instruments outside guards; HTTP/auth guarded publication in one
block, so one publication failure could suppress the independent instrument.

Checklist/Activity now defer meter acquisition until validated observations are
recorded. All four recorders guard each counter/histogram creation, publication
and measurement independently. Factory failure discards the optional observation;
a later request can resume recording using the same service. Together with the
Organization/Board change, all six custom API recorders use this boundary.
Existing names, units, safe label sets, request outcomes, counts and millisecond
to second conversion remain intact. No business retry or client-event replay is
introduced. Client observations remain untrusted aggregates, never business audit.

The baseline fails **17 of 34 new cases**. After repair **all 34 pass**: 24
two-observation cases across four recorders and six diagnostic conditions, four
same-service factory-recovery cases and six actual API-test-host HTTP cases.
The HTTP cases retain anonymous refusal (401), private/invalid input rejection
(400), accepted empty responses (204), oversized input rejection (413) and
independent health (200) while the selected client meter factory or instrument
publication fails. Healthy independent instruments still record both samples
with the original count/duration values.

All **196 related regression cases** pass: the prior 40 Organization/Board
diagnostic cases, 58 HTTP/auth telemetry cases, 81 client/privacy/render/rate-limit
cases, five business HTTP metrics cases and twelve operator/exporter cases.
This gives **230 distinct focused passes**, with no skips/failures. The Release
solution builds without warnings/errors. Each native invocation preserves all
**798 captured source/test inputs and 415 binary files** and restores original
Docker inventory after retiring owned containers. Cached Linux runtimes mount
current compiled inputs read-only; API-host persistence remains controlled Demo.
No database, existing local service, new image build/pull/import, external
provider/collector or large-load test is used. These are scoped compiled-source
contracts; exact-commit immutable/full CI, managed deployment and complete ARCH-08
and MVP acceptance remain required.

## Organization and Board instrument failure isolation — 2026-10-15

Organization and Board-sharing telemetry previously created their meter and
instruments during service construction. A throwing meter factory or native
instrument-publication callback could therefore prevent request execution,
before the existing guarded measurement writes were reached. A throwing request
counter listener also suppressed the independent duration observation.

Both services now create optional instruments during guarded recording after
the request. Factory failure skips that observation, and each instrument's
creation/publication/measurement has an independent guard. A later request can
record normally after factory recovery using the same telemetry service.
Existing meter/instrument names, units, operation mapping, outcome/error
categories, retry-key boolean and private-label boundaries are unchanged.
Business execution, responses, original exceptions and cancellation remain
authoritative; optional telemetry adds no business retry.

The baseline fails **28 of 40 new cases**. After repair **all 40 pass**:
36 direct middleware cases span two services, six diagnostic conditions and
success/fault/cancellation; two cases recover a factory without replacing the
service; two actual API-test-host HTTP cases perform authentication and
Organization/Board creation/read while the selected business meter factory
throws. Private Board reads still conceal existence with 404, member
administration still requires authentication with 401, and health remains 200.
The first repaired HTTP attempt incorrectly expects 401 for the private Board
read; the corrected tests verify both existing 404/401 contracts. That fixture
failure is preserved separately from the final passing evidence.

All **17 unchanged regression cases** also pass: five Organization/Board/search/
invitation HTTP metrics contracts and twelve operator exporter/configuration/
histogram cases. The Release solution builds without warnings/errors. All
**797 captured source/test inputs and 415 test binary files** match after each
completed native run; owned test containers retire and original Docker inventory
is restored. The cached Linux runtime executes current compiled assemblies with
read-only mounts, no database, image build/pull/import, existing local service,
external collector/provider or large-load test. HTTP persistence uses controlled
Demo adapters, not a Production database. Current exact-image/full CI and
managed deployment acceptance remain required; ARCH-08 and the MVP stay open.

## Server duration histogram resolution — 2026-10-15

The custom HTTP and authentication timers record seconds, but their unconfigured
SDK aggregation cannot distinguish the **400 ms / 750 ms** service-objective
thresholds. Both new actual-SDK regressions fail before repair: six measurements
at 0.2, 0.4, 0.401, 0.75, 0.751 and 6 seconds cannot be counted at the requested
thresholds. The other ten operator cases pass. This is an aggregation defect,
not a measured API latency or availability breach.

The API's optional exporter now uses explicit
[OpenTelemetry histogram views](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/docs/metrics/customizing-the-sdk/README.md#configuring-the-aggregation-of-a-histogram)
for `strataai.http.server.duration` and `strataai.authentication.duration`.
Boundaries in seconds are **0, 0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25,
0.4, 0.5, 0.75, 1, 2.5, 5, 7.5, 10, 30**, followed by the overflow bucket.
The six-measurement regressions now require exactly two observations at or below
400 ms, four at or below 750 ms, all six retained and an overflow boundary. Actual
OTLP transport remains enabled in those cases. Worker and other client/server
instruments, names, units, labels, resource identity and export cadence retain
their existing behavior. Authentication's narrower timer is not end-to-end
request latency; these metrics must not be summed together.

The mandatory Collector evidence gate now requires both threshold buckets for
all nine existing fixed HTTP/auth fixture outcomes. Bucket values may be zero:
the gate verifies resolution, not compliance with the latency objective. A
direct old/new probe shows the previous verifier accepted complete counts with
all threshold buckets missing; the revised verifier refuses them. All **31**
verifier cases pass, including individual missing/coarse boundaries, valid empty
buckets and malformed/private/nonfinite HTTP `le` labels or `le` on non-buckets.
All **11** full driver control-flow cases pass in **209.892 seconds**, including
the new evidence flag through healthy/outage/recovery and cleanup paths. The
initial local harness invocation lacked `jq`; correcting its tool path resolves
that prerequisite failure without changing the driver or assertions.

All **82 focused Linux .NET cases** pass: auth 38, HTTP 20, operator/SDK 12 and
rate-limit diagnostics 12. The complete Release solution builds with zero
warnings/errors; all **942 source / 415 binary inputs** match and owned test
containers are removed. An actual current compiled Production API also exports
both histogram families with 0.4/0.75-second wire boundaries, sorted finite
bounds and bucket totals equal to their counts. Its registration/wrong-password/
CSRF/correct-login responses remain **201/401/403/200**, with all eight required
auth count/duration observations and fixed private-safe resource/metric labels.
The host phase completes in **14.587 seconds** using fresh restricted PostgreSQL
17.11 and 149 migrations. All **832 source/persistence + 29 API binary inputs**
match. Cleanup restores Docker inventory and removes **739,722 temporary bytes**;
no image is built/pulled/imported and the original database/local Demo is unused.

This compiled-code receiver verifies actual SDK wire bytes, not the pinned live
Collector or current immutable release. Current-image CI, calibrated dashboards,
tested alert delivery and full ARCH-08 acceptance remain required. During
upgrade, do not aggregate old and new histogram schemas: select the intended
build/revision until all contributing API replicas use the same boundaries.
No service-objective, p95 or production-capacity result is claimed. No large-load
test runs; those remain deferred until after MVP.

## Authentication endpoint outcomes — 2026-10-15

`StrataAI.Authentication` adds `strataai.authentication.requests` and
`strataai.authentication.duration` (seconds) to the API's optional OTLP export.
Only matched POST authentication route templates are observed: register, login,
password forgot/reset, verification resend/consume and logout. Labels are fixed
`operation`, bounded `status_code` and `outcome=completed|cancelled|faulted`.
There are no email/account/Organization identifiers, IPs, tokens, cookies, raw
paths/queries, correlation/retry keys, request bodies or exception details.

These count HTTP endpoint attempts, including retries and receipt replays, not
unique users, accepted credentials, mail delivery or authoritative audit events.
Login 401 and 403 observations distinguish response categories without inferring
account existence or a private reason. Recovery acceptance does not prove that
an account exists or an email was sent. Routing occurs before this timer; generic
framework method/media rejection endpoints and pre-routing failures remain in
overall HTTP observations. Authentication middleware wraps CSRF, session/rate
checks and endpoint execution without changing their behavior. Do not sum these
counts with overall HTTP counts or treat this narrower duration as end-to-end
API latency. Incomplete/cancelled execution uses status zero and preserves the
original exception; instrument/listener failures cannot replace the result or
invoke authentication again.

All **80 focused Linux .NET cases** pass: 38 auth cases, 20 overall HTTP cases,
ten actual SDK transport/configuration cases and 12 rate-limit diagnostic cases.
They cover all seven routes, canonical case/trailing slash, actual registration/
denied/correct sign-in, JSON CSRF refusals, generic media rejection, bounded
labels/statuses, cancellation and throwing diagnostic providers. All **942
source/415 binary inputs** match. The first development fixture used text/plain
where a matched JSON endpoint was intended; corrected JSON cases and a separate
generic-media case now preserve that distinction rather than relaxing checks.

The actual current compiled Production API also passes a fresh restricted
PostgreSQL 17.11/149-migration rehearsal in **12.795s**. Real responses are
registration **201**, wrong-password login **401**, missing-CSRF login **403**,
then correct login **200**. Actual SDK wire output contains all eight required
count/duration observations, the running API's service/build identity and only
fixed auth labels; fixture credentials and email are absent. All **832 source/
persistence inputs** and **29 API binary inputs** match. Owned fixtures restore
Docker inventory and remove **738,564** temporary bytes; no image is built,
pulled or imported and the original database/local Demo is unused. Earlier
incomplete fixture setup attempts are cleaned and excluded from completed proof.

The mandatory exact-image Collector fixture now makes one wrong-password and
one missing-CSRF request against its fresh account before normal login. Both
metric families require register 201 and login 200/401/403. All **28 verifier
cases** and **255 CI coverage guards** pass; fixed operation/status/outcome,
API resource identity, finite values and private-field rejection are enforced.
Histogram bounds must be numeric (or `+Inf`) and cannot be attached to non-bucket
auth metrics. All **11 driver cases** pass, including assertions that exactly
one login 401 and one login 403 are requested before the Worker scope changes.
The current compiled-code receiver is an isolated HTTP fixture, not the pinned
Collector or immutable release images. Current-image collection, deployed
dashboards/alerts and remaining PRD-02/ARCH-08 acceptance remain unfinished.
PRD-02 stays open at **16% estimated work remaining**, unchanged planning estimate.
Large-load testing stays deferred until after the operational MVP.

## Collector target metadata validation — 2026-10-15

The verifier previously ignored `target_info` because it is outside the
`strataai_` metric prefix. A direct probe and **47 failing regression subcases**
show that private resource fields, wrong builds, malformed records and duplicate
metadata could accompany an otherwise accepted scrape. This is a validation gap;
no application data leak or deployed Collector incident is established.

The [pinned Collector implementation](https://github.com/open-telemetry/opentelemetry-collector-contrib/blob/v0.161.0/exporter/prometheusexporter/collector.go)
emits target metadata separately from application measurements, representing
service identity with `job` and retaining the other resource attributes. The
existing app resource contains only fixed service/namespace/version/revision
fields. Present target records must therefore identify the API or Worker, match
the expected version/revision, contain no private/unknown fields or nonempty
instance, and have value one. Duplicate service records, malformed families and
whitespace-hidden invalid samples are refused. Omission remains supported;
Collector plumbing metrics and application observation requirements are preserved.

All **25 verifier tests** and **11 complete driver cases** pass. Driver fixtures
include API and Worker target metadata through healthy collection, database
observation failure, recovery and negative/cleanup paths. Workflow lint and diff
checks pass. No app/runtime code, permissions or backend selection changes.
The pinned Collector image is absent locally and is not pulled; these checks
do not claim new live Collector execution. No container, database or load test
is used. Current immutable-image CI and full ARCH-08 acceptance remain required.

## Identity-email backlog — 2026-10-15

With identity email and OTLP metrics both enabled, the separate Worker now
samples the global identity-mail queue independently of Organization job scope.
`strataai.worker.identity.backlog` exposes only `queue=identity_email` and
`state=pending|running`; `strataai.worker.identity.backlog.available` exposes
that fixed queue and availability zero/one. Pending includes future schedules;
running includes active/expired leases. Sent, failed and cancelled rows are
excluded. These are persisted-state counts, not immediately runnable messages,
delivery success, invitation-mail counts or unique recipients. Do not sum global
counts from multiple Worker replicas.

The reader uses the existing restricted `IdentityDeliveryPostgres` connection,
global service RLS and a read-only transaction, with a dedicated single-connection
pool and two-second connection/command limits. No role grant is added. It reads
counts rather than recipients, tokens, sender/provider fields or subjects, and
never claims, sends, acknowledges or retries mail. The hosted sampler has a
five-second cancellation deadline and waits 30 seconds after each pass. Exporter
callbacks only read memory. Missing/failed/invalid observations report availability
zero and discard the in-process counts; healthy empty queues publish actual zeros.
Require availability one and fresh samples, because downstream gauges can retain
older data. Disabled mail or metrics does not run this sampler; it is not a new
core readiness dependency.

The real restricted PostgreSQL 17.11/149-migration contract passes in **1.878s**:
two subjects, scheduled pending work, active/expired leases, terminal exclusion,
service RLS, unchanged job rows and return to baseline. Mail credentials cannot
read Organizations/password hashes; ordinary API/Worker credentials cannot read
the identity queue. The normal persistence CI run supplies its existing narrow
mail fixture role and now executes this contract. No production permissions are
broadened.

All **75 focused Linux .NET cases** pass, including 11 identity sampler/budget/
lifecycle cases, existing Worker regressions and real SDK export to a controlled
receiver. All **940 source/415 binary inputs** match. The actual compiled current
Worker host also passes a **76.195s** native rehearsal: actual SDK observations
show availability **1 → 0 → 1** across database network failure/recovery, with
exactly two pending/two running mail jobs after recovery. Both mail and Organization
job fingerprints remain unchanged. Liveness stays 200; readiness refuses with
503 in **2.250s**, then recovers to 200. All **831 source/persistence inputs** and
**42 Worker binary inputs** match. Fixtures restore Docker inventory and remove
724,100 standalone-store plus 735,831 host-rehearsal temporary bytes; no new image
is built, pulled or imported and the original database is unused.

These are mounted current compiled-code checks in retained runtimes with a
controlled OTLP HTTP receiver. The host uses non-runnable fixture jobs; it does
not establish provider delivery, a pinned-Collector deployment or immutable-image
release acceptance. All **21 Collector verifier cases** and **255 CI coverage
guards** pass. Identity observations are optional when mail is disabled; when
present, healthy observations require both state counts and exactly one valid
availability sample. Private fields, wrong queues/services, partial/duplicate
samples and invalid values are rejected. Actual current-image collection,
discovery-wide Organization backlog, lease age, deployed dashboards/alerts and
the rest of ARCH-08 remain open. Large-load testing stays post-MVP.

## Bind Collector job labels to service identity — 2026-10-15

The CI scrape verifier previously permitted arbitrary `job` text alongside
otherwise valid service/build metadata. Two direct probes accept a private host
marker on API and Worker observations; 24 regression subcases also reproduce
acceptance of private text, malformed scopes or the other service's identity.
This is a gap in evidence validation, not evidence that the app exported those
values or that a deployed Collector leaked data.

The [pinned Collector 0.161.0 implementation](https://github.com/open-telemetry/opentelemetry-collector-contrib/blob/v0.161.0/exporter/prometheusexporter/utils.go)
derives this label from its resource namespace and service name. With this
fixture's fixed resource metadata, a present `job` must therefore be exactly
`strataai/strataai-api` or `strataai/strataai-worker`, matching the sample's service.
Omission remains supported. Empty, private, escaped, Unicode, suffixed and
cross-service values are refused in healthy and unavailable-backlog modes.
Existing duplicate-label, private-field, build, observation and recovery checks
remain in force. App instrumentation and operator backend choices are unchanged.

All **17 verifier cases** pass. The complete driver fixtures now include the
Collector's matching job labels through healthy, unavailable and recovered
collection. All **11 driver cases** pass with the revised fixtures, including
refusals and cleanup. Hosted CI remains required before release acceptance.
No Collector image is available locally, so this
change does not claim a new live pinned-Collector run. No image is pulled, no
database is used and no load test runs. ARCH-08 remains open.

## Backlog database failure and recovery — 2026-10-15

The actual compiled Production Worker host from `be7bf9e4` passes a **76.227-second**
native rehearsal with fresh restricted PostgreSQL 17.11 and all 149 migrations.
Its real SDK OTLP wire observations report availability **1 → 0 → 1** across
database network disconnection and reconnection. Healthy and recovered counts
are exactly two pending/two running jobs from the configured Organization;
seven foreign pending jobs remain excluded. All fixture jobs are scheduled or
leased beyond the check, and complete persisted-row fingerprints stay unchanged.
This is a small dependency-failure test, not a capacity workload.

Liveness stays HTTP 200; dependency readiness returns HTTP 503 during the outage
in **2.248 seconds**, then returns 200 after recovery. Export observations have
increasing SDK timestamps and the exact running host's service/build identity.
Decoded wire labels exclude fixture IDs, payload markers and credentials. All
**827 source/persistence inputs** and **42 Worker binary inputs** match; owned
containers/network are removed, Docker inventory restored and **731,669**
temporary bytes deleted. No new image is built/downloaded/imported and the
original database is unused. An earlier private harness attempt stops on a
missing import and cleans up; it is not counted as completed verification.

This uses current compiled assemblies mounted read-only in retained runtimes
and an isolated HTTP receiver that captures actual OTLP, not the pinned
Collector or current immutable release images. It does not certify managed
deployment, email delivery, complete current-image CI or the MVP release.

The mandatory exact-image metrics fixture now checks healthy collection,
stops only its disposable Compose PostgreSQL service, requires current
availability zero, restarts that existing database volume and requires healthy
collection again. It checks Worker health/readiness and unchanged fixture rows;
cleanup restores PostgreSQL before restoring the ordinary Worker and deleting
the two owned rows. Three fixed evidence files retain the healthy, unavailable
and recovered results with canonical build identity and checksums. The verifier
rejects missing, duplicate or mixed availability samples; outage mode permits
missing/retained counts but keeps all other observation, identity and privacy
requirements. Hosted execution remains required.

The driver requires GitHub Actions as well as `CI=true` before any HTTP/container
action. Its separate control-flow tests substitute every external HTTP/container
operation and refuse to start if a substitute is missing. Those tests verify
failure refusal and cleanup ordering, not real provider or runtime behavior.

All **15 verifier cases** and **11 complete driver control-flow cases** pass,
including stale/mixed observations, failed stop/restart, wrong readiness,
changed/missing rows and refusal before actions outside GitHub Actions or with
a missing substitute. The driver requires exactly two initial fixture rows and
a valid fingerprint before stopping the database. All **255 CI coverage guards**
and **37 release-artifact tests** pass; evidence staging finishes with 11 passes
and one Windows-specific skip. Bash/workflow lint and diff checks pass. These
checks preserve the existing functional stages and load-test phase gate.

## Scoped Organization job backlog — 2026-10-15

When metrics export and explicit `STRATAAI_WORKER_ORGANIZATION_IDS` are both
configured, the separate Worker samples its configured Organizations at startup
and 30 seconds after each pass. `strataai.worker.scoped.backlog` has only
`queue=organization` and `state=pending|running` instrument labels. It aggregates
persisted states across that scope: pending includes future schedules; running
includes active and expired leases, including leases held by other Workers.
Succeeded/failed rows are excluded. This is not the number immediately eligible
for dispatch, a global queue census, unique-job throughput or an email backlog.
Do not sum overlapping Worker scopes/replicas to infer a global count.

The sampler uses normal restricted-role/schema/transport checks, a separate
single-connection pool, two-second connection/command timeouts, read-only
transactions and transaction-local tenant RLS. It never claims, retries or
dispatches a job, changes a lease or reads payloads. A whole sweep has a
five-second cancellation deadline; only a complete valid aggregate is published.
Exporter callbacks read memory and perform no database I/O. Without an exporter
or explicit scope, this hosted sampler does not run; Demo has no database sampler.

`strataai.worker.scoped.backlog.available{queue=organization}` is zero before
the first successful sweep and after a failed/invalid/partial sweep. Such a sweep
discards the in-process counts; a healthy empty queue publishes two genuine
zero counts and availability one. Consumers must require current availability
one **and fresh samples** before using backlog values: SDKs/Collectors can retain
previous gauge values after a producer stops observing or disappears. Export
and database observation failures cannot stop authoritative job processing or
become a new readiness dependency. IDs, routes, tokens and exception text never
enter these instruments.

All **64 focused Linux .NET cases** pass, including 13 sampler/read-budget cases,
existing job outcomes/loop failures/fairness/logging and real SDK OTLP transport
to a controlled receiver. All **936 source and 415 binary inputs** match. The
restricted PostgreSQL **17.11**, 149-migration contract independently verifies
scheduled work, active/expired leases, terminal exclusion, two tenants, an empty
result, forced RLS and unchanged persisted rows; it passes in **1.636 seconds**.
All **827** captured source/binary inputs match. Owned fixtures are removed,
Docker inventory restored and **724,100** temporary bytes deleted, without new
images or large-load testing. The solution builds with zero warnings/errors;
all 12 Collector verifier cases pass.

The mandatory exact-image Collector fixture now scopes the existing Worker to
its fresh Organization and seeds two non-runnable rows, requiring nonzero
pending/running gauges and availability one alongside prior observations. It
restores the ordinary Worker and removes only those fixture rows on exit. The
persistence contract runs in the normal PostgreSQL suite. These CI changes still
require current-image execution; compiled-code and store checks do not certify
that deployment. Identity-email/discovery-wide backlog, lease age, managed
Collector ingestion, dashboards, tested alerts and full ARCH-08 acceptance remain
open. Large-load testing remains deferred until after the operational MVP.

## Worker queue-loop failures — 2026-10-15

`strataai.worker.loop.failures` counts exceptions that escape an Organization
job pass or identity-email pass into its hosted service loop. Its only instrument
label is `queue`, fixed to `organization` or `identity_email`; it shares the
Worker's existing meter, optional OTLP configuration and embedded service/build
identity. Exception text/type, job and tenant identifiers, recipient addresses,
tokens and provider details never enter this counter. No extra database query
or provider call is introduced.

A failed claim, failed completion acknowledgment or unexpected cancellation
while the service is still running increments the counter once. Normal service
shutdown and empty polls do not. A handler/provider failure which the processor
successfully acknowledges as a retry remains a `strataai.worker.jobs` outcome,
without also becoming a loop failure. The counter is recorded before optional
warning logs; instrument creation, listener and logging failures cannot stop
the next pass. Existing leases, retry policy, queue fairness and shutdown behavior
are unchanged. A failed acknowledgment can follow a completed external effect;
this observation does not establish whether the effect happened or arrange
another execution.

The Collector verifier accepts this optional family only with the fixed queue,
Worker service/build identity and nonnegative finite values. It refuses an
`outcome`, exception/private field, unknown queue, duplicate label or wrong build.
Healthy ingestion still requires the existing committed Organization outcome;
failure samples are optional because that fixture need not encounter a failure.
An absent counter does not prove service health or successful export.

Before implementation, all **12** escaping-failure cases lack the observation,
while six quiet/handled-path cases pass. The changed real hosted service loops
pass all **18** cases, including throwing metric and log listeners, continued
polling, no extra handler/send/acknowledgment, normal cancellable shutdown and
handled retry separation. Together with outcome, logging, Organization fairness
and OTLP transport regressions, **51 Linux .NET cases pass**. The actual Worker
export registration sends both failure queues through the real SDK to a
controlled HTTP transport, and survives receiver failure. The complete solution
build has zero warnings/errors; all **10** Collector verifier tests pass.

These tests exercise current compiled code in a retained runtime with controlled
stores/providers, not a managed provider, production database or current-image
Collector deployment. All **932 source/415 binary** hashes match, owned containers
are removed and the original Docker inventory is restored. No new image build,
download/import or large-load test runs. Backlog/lease-age metrics, discovery-loop
coverage, deployed alerts and full exact-image CI remain acceptance work.

## Complete Collector label validation — 2026-10-15

The mandatory Collector verifier now consumes every label token and rejects
duplicate names before applying its existing family, label, value and build
checks. The former regex extraction silently overwrote duplicate labels or
discarded malformed text, which could hide an earlier unknown queue value or
unparsed field. Six initial negative subcases reproduce false acceptance; a
seventh failing subcase is a valid quoted fixed label that the old parser rejects.
All eight test methods now pass, including original observation omissions,
wrong builds, unknown/private fields, HTTP/Worker outcomes and the new cases.

Whitespace, quoted fixed label names, valid string escapes and one final comma
follow the [Prometheus text format](https://prometheus.io/docs/instrumenting/exposition_formats/).
Duplicate quoted/unquoted names are compared as the same key. Unknown names and
values still fail the existing semantic checks; accepting valid syntax does not
broaden exported fields. This remains a verifier for the selected pinned
Collector families, not a general Prometheus ingestion service. No application
runtime behavior, source test deadline, phase gate or retained-evidence field is
changed. Exact current-image CI remains required; no ticket is closed by this
parser repair.

## Worker job outcomes — 2026-10-15

The separate Worker now exports `StrataAI.WorkerJobs` through the same explicitly
configured OTLP transport, with fixed `service.name=strataai-worker` and its own
embedded build version/revision. API feature/HTTP meters are not selected by the
Worker exporter. Both release services forward `STRATAAI_METRICS_OTLP_ENDPOINT`
and optional secret `STRATAAI_METRICS_OTLP_HEADERS`; the optional
`compose.metrics.yml` overlay enables both against the pinned private receiver.
Omitting the endpoint keeps export disabled. Endpoint validation, three-second
timeouts, no redirects and independence from core readiness remain unchanged.

`strataai.worker.jobs` counts reported outcomes of claimed attempts. Its only
instrument labels are `queue` and `outcome`:

| Queue | Outcomes |
| --- | --- |
| `organization` | `completed`, `retried`, `lease_lost`, `unknown` |
| `identity_email` | `sent`, `cancelled`, `retried`, `failed`, `lease_lost`, `unknown` |

The two existing processor diagnostic adapters record the measurement before
optional logging, after the processor has decided its result. A logging failure
therefore cannot suppress a measurement; an instrument/listener failure cannot
alter that result, stop subsequent work or repeat a provider operation. Empty
polls produce no measurement. Unknown enum values map to the fixed `unknown`
label. Job type, tenant/actor/job/Worker IDs, correlation, lease/receipt/token
values, addresses, provider messages and payloads never enter metric labels.

These are attempt outcomes, not unique jobs, queue depth, end-to-end delivery
latency or proof that an email reached its recipient. `retried` is the existing
processor result for an accepted failure acknowledgment; it does not promise
another execution or describe queue retention/dead-letter policy. Store-claim
exceptions and shutdown before a decided result do not produce these counters.
Backlog, lease age, loop errors and provider-specific metrics remain work.

The mandatory exact-image Collector verifier now also requires an Organization
completion from the actual separate Worker during its small HTTP fixture. It
checks the Worker's service/build identity independently from API samples and
refuses private or unknown queue/outcome labels. Current immutable-image
execution of this extended contract remains pending; email-provider delivery is
not enabled by the Collector fixture.

Validation passes **53 Linux .NET cases**: 12 fixed metric cases, ten exporter
cases, four real Organization Worker-loop cases, seven Worker diagnostic/service
cases and 20 API HTTP regressions. Controlled stores/providers exercise actual
processors and hosted service loops, including simultaneous metric/log listener
failure with one committed completion/send and continued processing. This is
not managed-provider or real PostgreSQL execution. All **934 source/49 binary**
hashes match during execution; owned containers are removed and Docker inventory
restored without new images or load tests. Four actual Compose resolutions pass
for base/overlay and absent/explicit export settings; five Collector verifier
tests and 37 release-artifact tests pass. ARCH-08 remains open for full acceptance.

## Overall HTTP outcomes — 2026-10-15

ARCH-08-FR-006 now has API-wide request counts and elapsed duration through
the explicit `StrataAI.HttpServer` meter. The existing optional OTLP exporter
selects it alongside the feature meters; no new endpoint, provider or application
image is introduced. Native measurements remain available without an exporter.

| Instrument | Unit | Meaning |
| --- | --- | --- |
| `strataai.http.server.requests` | request | One observation when the downstream API pipeline returns or throws |
| `strataai.http.server.duration` | seconds | Elapsed pipeline time for that same observation |

Both carry only `method`, `status_code` and `outcome`. Methods use the nine
standard fixed tokens or `OTHER`; status is 100–599 or zero; outcome is
`completed`, `cancelled` or `faulted`. Completed means the pipeline returned,
including handled 4xx/5xx responses. An escaping exception has status zero
instead of the default 200; only an escaping cancellation with the request's
abort token already cancelled is classified as cancelled. Original exceptions
are rethrown. An instrument-creation or listener failure cannot replace a
response/exception or cause another application invocation.

The middleware wraps error conversion, status pages, database admission, CSRF,
authentication, rate limiting and authorization. It includes health checks and
unmatched routes. Durations for long-lived streams cover their pipeline lifetime,
not individual messages. No path, route template, query, header, body, exception,
actor/tenant/object ID or correlation value is recorded. Status 401/403 counts
describe HTTP rejection outcomes; they are not unique failed-login or confirmed
security-incident counts. Operators can group numeric statuses to measure API
error rates without adding protected or unbounded labels.

The existing mandatory exact-image Collector contract now requires request and
duration samples for GET/200, POST/400, POST/401, POST/403 and PATCH/409, produced
by its actual HTTP operations. It retains all previous feature/privacy checks,
rejects unknown HTTP methods/statuses/outcomes/private labels, and adds only
fixed verification booleans to retained evidence. Current-image execution of
this extended ingestion contract remains pending CI.

Local Release solution build passes with zero warnings/errors. Linux execution
passes **20 HTTP telemetry cases**, **eight exporter cases** (including real OTLP
serialization and collector-transport failure) and **14 existing diagnostic
middleware cases**. The real TestServer pipeline covers health, missing routes,
authentication and CSRF refusal. Other cases cover ten response statuses,
escaping faults/cancellation, unbounded methods and instrument/listener faults.
All 930 captured source and 49 binary hashes match during execution; test
containers are removed and the original Docker inventory restored, without
image builds/downloads/imports or load tests. Four Collector verifier tests,
including per-observation omission and bounded-label refusals, also pass.

This closes the API-wide aggregate-instrumentation gap, not the full ARCH-08
metrics or release requirements. Worker/backlog/provider/database/backup
instruments, dashboards, operational alerts, production objectives and tracing
remain outstanding. Large-load testing stays deferred until after the MVP.

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
operator dashboards/alerts, additional Worker/backlog/provider/database/backup instruments,
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
revision, keyboard operation and WCAG 2.2 AA. Both cases pass in the immutable
`5845da8b` hosted run; see [executed creation/settings evidence](prd-03-acceptance.md#hosted-organization-creationsettings-telemetry--2026-10-15).
Broader Organization member/departure/deletion/invitation telemetry,
per-tenant adoption policy, successful transport reconnect measures and dashboards
remain separate unfinished requirements.

Local settings validation passed 38 focused component/queue cases, four API
category/authentication/atomic-private-batch cases, API/API-test Release builds
with zero warnings/errors, three Collector verifier tests, web/browser
typechecks and zero-warning lint. Both native cases collect successfully.
The two native scenarios have hosted exact-image proof for that candidate;
full current-main CI remains required. PRD-03 stays open at **8% estimated work
remaining**, and ARCH-08 remains open.

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
operation and WCAG 2.2 AA. Both cases pass in the immutable `5845da8b` hosted
run; the [shared evidence record](prd-03-acceptance.md#hosted-organization-creationsettings-telemetry--2026-10-15)
binds exact build metadata and 12 unchanged feature/test/dependency files.

Local validation passed 23 focused creation/queue component cases, six API
category/authentication/privacy cases, three Collector verifier cases, web and
browser typechecks and zero-warning lint. API and API-test Release builds passed
with zero warnings/errors. Both native browser cases collect successfully. This
does not replace the separately recorded hosted execution or complete PRD-03
acceptance. Current-main CI and remaining requirements still govern closure;
PRD-03 stays open at **8% estimated work remaining**. No load test runs in the
hosted-evidence audit, and large-load testing remains deferred until after MVP.
