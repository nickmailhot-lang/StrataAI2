# Configuration and lifecycle HTTP latency — PRD-27

The maintained [latency fixture](../../scripts/ci/test-organization-configuration-latency.sh)
measures four authorized synchronous operations through the release Nginx edge:
configuration change/read and operational lifecycle change/review. CI uses the
retained images from the build-once pipeline. A failure to meet the target fails
the step; measured evidence still records `target_not_met` for diagnosis.

Each operation has five warmup requests followed by 25 measured requests,
with concurrency one. Registration, fixture-account verification, login and
Organization/Board/List creation are excluded. Timings cover complete HTTP
acknowledgment/response reading. No external provider is invoked by these
operations. This is a controlled regression workload, not a universal deployment
SLO, concurrent-load benchmark or proof of Worker publication latency.

Each configuration request supplies all 18 fields, including actual owning
intake Board/List IDs, fiscal/calendar fields, jurisdiction-policy source/notes
and 32 entries in each bounded collection. The candidate's maximum serialized
configuration size was 57,267 bytes. Thirty distinct versioned changes create
retained history. Every acknowledgment must match the current actor, owning
Organization, expected next version and exact proposed configuration. A fresh
admitted read must match the acknowledged revision. Lifecycle changes alternate
suspension/reactivation, with matching current administrative review after each
of the 30 transitions. Fresh keys are generated for every change; receipt replay
latency has separate recovery acceptance.

[Measurement code](../../scripts/ci/measure-organization-latency.py) uses nearest-rank
percentiles. The p95 sample must be strictly below 500 ms, using its unrounded
value; the retained percentile preserves that value. Invalid counts, negative
or nonfinite measurements cannot pass. HTTP failure, wrong actor/version/source
or mismatched authoritative refresh stops the fixture. The CLI allows only the
configured local release edge and refuses redirects. Private cookie files and
response values are not retained or printed.

The artifact contains only schema version, build revision, fixed topology/status,
workload counts/size, verification flags and aggregate timings. CI stages it with
canonical build metadata and checksums and retains it for 90 days. Organization,
account, Board/List IDs, keys, names, contact/policy values, cookies and raw
requests/responses are excluded. Exact-image artifact ingestion remains unproven
until that CI step executes successfully.

## Executed local Production scope

A disposable canonical-role PostgreSQL fixture applied fresh ordered migrations
and hosted the current compiled API in Production mode. It ran the maintained
measurement function against real verified-account HTTP sessions. Original
PostgreSQL was unchanged; owned runtime/schema/PostgreSQL containers, database,
roles and credential files were removed. All 1,884 captured repository files
matched at completion. This candidate measurement snapshot predates the subsequent
CLI diagnostics/redirect and percentile-display refinements; those have source
checks. No complete release-performance claim follows from this local run.

| Operation | Measured samples | Local p50 | Local p95 |
| --- | ---: | ---: | ---: |
| Configuration change | 25 | 124.805 ms | 161.614 ms |
| Configuration read | 25 | 51.435 ms | 70.169 ms |
| Lifecycle change | 25 | 81.916 ms | 104.992 ms |
| Lifecycle review | 25 | 42.561 ms | 60.949 ms |

Five source checks cover percentile boundaries, unrounded target decisions,
invalid samples, bounded full-field payload, invalid identity/topology before
HTTP and rejection of a wrong-actor acknowledgment. They do not prove CLI
execution or deployed latency. Full current-source regressions and exact-image
release execution remain required; the [acceptance map](prd-27-acceptance.md)
keeps PRD-27 open.
