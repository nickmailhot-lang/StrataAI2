# Source test result artifacts — ARCH-11-FR-018/090

Required source jobs now retain machine-readable result summaries in `source-test-results-web-<sha>` and `source-test-results-dotnet-<sha>`. Both download the [initial metadata document](initial-build-metadata.md) from the same workflow run. Each summary embeds its canonical repository, source SHA, workflow run identity and application version; it cannot be substituted with another run's metadata when executed in GitHub Actions.

The complete web unit/component suite still runs, with Vitest's default console reporter plus JUnit output. The complete Domain and API source suites still run, with the pinned MTP 2.4.0 TRX extension in the two test projects. The [Microsoft report extension documentation](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-test-reports) explains registration and `--report-trx`. No filters, test retries, ignored failures, product dependencies, or image rebuilds are added to CI.

Raw XML lives under the runner's temporary directory. The [summary exporter](../../scripts/ci/summarize-source-tests.py) publishes only schema/version, canonical build metadata, report completeness, outcome counts, opaque case hashes, bounded durations, and source file/static .NET method identifiers where recognized. It excludes raw test display names/theory arguments, assertions, rendered DOM, console output, stack traces, attachments, and machine/user metadata. Opaque case hashes distinguish every recorded case, including duplicates; they are diagnostic report identities, not domain record IDs or test-name replacements in console logs.

The exporter validates available report counters against actual case rows. Missing, malformed, oversized, empty, entirely skipped, or inconsistent reports are explicitly marked and fail the summary step. Failed/error cases retain their outcomes and also produce a failing exit status. Both summarization and upload run after test failure with `always()`; the original source test failure remains mandatory. Only the summary directory is uploaded. If an earlier failure prevents a report, the artifact records the unavailable scope rather than advertising an empty passing suite.

Eight parser/privacy/CLI regressions pass. They exercise JUnit/TRX outcomes, native durations, retained build identity, private-content exclusion, counter mismatches, missing/empty/malformed/all-skipped input, duplicate cases, mismatched workflow identity, and failure exit preservation. Thirty-five workflow coverage/mutation tests pass, including rejection of raw report upload paths, skipped failure summaries, ignored publication errors, filtered mandatory API tests, and a missing source reporter. Actionlint validates the resulting workflow.

Actual reporter integration is checked separately: 43 focused web cases emit JUnit and summarize successfully; 17 focused notification API cases emit MTP TRX and summarize successfully. The complete local Windows Domain run emits 758 results, including 16 failures. Its pre-change compiled baseline has the same total and identical failing method/case counts. Those failures remain visible: the exporter records 742 passed/16 failed and exits unsuccessfully. The same current compiled Domain assemblies then pass all 758 cases in an invocation-owned isolated Linux runtime; that complete TRX also summarizes successfully. The owned container is removed, preserving existing services and volumes. This Linux run uses compiled source in an existing runtime image, not the current immutable release candidate or the full image gate.

The first hosted web execution at `2b940f29e3eef98ad3afc998a0a5e572a646ddf8` succeeds in [run 37848406366](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37848406366). Independently downloaded artifact `11581546149` contains only `web.json`: schema 1, JUnit runner, a complete report with 1,964 passed cases and zero failed/skipped/error cases, and the matching repository/full SHA/run ID/version `0.1.0-1310`. Its ZIP SHA-256 is `16927bdaafc564586d8ed57643af00c849bc441944eabe23c947fd03a8a7440e`. This proves complete hosted web result publication for that commit; the .NET report and remaining gates require their own evidence.

The hosted .NET source job for the same `2b940f29` run also succeeds. Independently downloaded artifact `11582150040` contains only `dotnet.json`, schema 1/TRX, with matching repository/full SHA/run/version. Report 0 records all 758 Domain cases passed; report 1 records all 690 API-host cases passed. Both reports are complete with zero failures/skips/errors, actual case counts equal outcome totals, every case has only the four permitted fields, and every source identifier is classified. The ZIP SHA-256 is `ab32e2af9b6efd8c1789634f15ac78b1078e85e5ac2b33d97d125b912d811eb9`. This is complete hosted source-report execution, supplementing the earlier focused/local proofs rather than relabeling their scope.

The run's PostgreSQL integration, source-quality gate, build-once and security jobs also succeed. All four exact-image integration groups remain independently required; a successful source report is not a complete image gate or release certificate.

The refreshed dependency locks restore in locked mode. The strict Release solution build has zero warnings/errors. Current exact-image integration/security/release gates, broad diagnostic provenance, full acceptance/DoD and the existing native capacity investigation remain required before ARCH-11 closure.

```sh
python3 -m unittest discover -s tests -p source_test_reports_test.py
node --test tests/integration-suites.test.mjs
```

Related: [mandatory integration groups](integration-ci-groups.md), [runtime build identity](build-identity.md), [documentation index](../README.md).

## Schema-123 compiled-source local comparison

The current locked Release build's unchanged complete Domain suite passes
**758/758** in a pinned local Linux runtime, with zero failures/skips/errors.
The XML counters are independently compared with the full case count, and
the owned test container is removed. Compiled binaries are mounted read-only;
reports remain outside the repository. This is compiled-source verification
using an existing runtime image, not current immutable-image release proof.

The Windows comparison finishes **742/758**, with sixteen failures retained
privately: twelve socket failures, two time-zone assertions and two additional
runtime/filesystem checks. No test is skipped or rewritten to hide those
results. An initial direct Linux invocation used MTP report arguments with the
standalone xUnit runner and refused its options before execution. The complete
successful invocation uses that runner's own XML reporter, as advertised by
its local help. This changes the local harness, not mandatory CI.

The first complete Linux API invocation reports **80/691**, with 610
DirectoryNotFound exceptions plus the architecture source-root assertion:
the Windows-built WebApplicationFactory manifest points to Windows paths and
the runtime lacked the source checkout. Its report is retained privately.
A fresh complete invocation mounts the real checkout read-only and privately
maps only existing manifest source roots into that Linux checkout. It uses
the same original 691-case assembly without filtering, retries or changed
assertions. Its result is pending. The original Windows API invocation also
remains live; observation timeout is not treated as completion or grounds
for restarting it.

The separate complete schema-123 Board browser phase remains active. Current
GitHub CI is queued; none of these local reports certify the required current
build-once integration/security/release matrix.

The live Linux API invocation has reported the architecture test's
`Assert.NotNull` source-root failure. Its real production projects are mounted,
but the standalone runner's assembly directory is outside the checkout
ancestry used by that test's root search. A fresh full-suite harness layout is
staged with the same read-only assembly under the real checkout's test-project
path and the same private manifest mapping. The assertion and actual production
project files remain unchanged. This staged harness has not yet been executed;
the current Linux API, Windows API and schema-123 browser handles remain live.

A read-only catalog query on the running schema-123 browser database records
**53 base-table candidates** lacking at least one physical `created_at` or
`updated_at` column. This is not a defect count: immutable journals, source
projections and mutable records require separate classification. In particular,
`organization_deletion_progress` lacks physical creation time but references
the retained `(tenant_id, request_id)` deletion request. Its publisher inserts
request and initial progress in the same owning transaction; page/terminal
capabilities already maintain update time. That source relationship is the next
creation-clock repair candidate, not completed acceptance evidence.

Subsequent polling confirms both complete API handles remain live. The Linux
console continues growing and its running container consumes CPU; the original
Windows console also grew. Neither has a terminal report yet. The diagnosed
architecture assertion is retained rather than hidden by replacing a live run.
The schema-123 browser invocation has since finished 30/32; its full result,
cleanup and newly started full verification are recorded in
[navigation observations](../navigation-observations.md).

The original Windows API invocation subsequently exited successfully. Its
actual TRX contains **691 case rows: 667 passed and 24 not executed**, with zero
failures or report errors. This is not a 691/691 pass: all 24 exclusions match
the existing Linux-only private-staging declarations in attachment download,
preview, Board background image, moved file, controlled-download permission
matrix and Card cover delivery tests. The eighteen permission-matrix cases are
counted individually. These existing platform declarations are unchanged; the
pending complete Linux invocation must provide its own outcome for those cases.
Raw case names, arguments and report bodies remain private. The wrapper's
successful exit alone is not treated as full-scope acceptance evidence.

The complete portable Linux API invocation subsequently finishes **690/691**.
Independent TRX inspection confirms 691 actual rows and executed cases, zero
skips/errors/timeouts, and only the diagnosed production-project source-root
assertion failing. All 24 Windows-excluded private-staging cases execute in this
Linux invocation. Its report remains private; the owned container is removed.
This is not a full pass. A fresh full current schema-124 API run uses the
successful edge-regression build and places its read-only assembly under the
real source checkout's test-project ancestry. It preserves the original 691
cases and assertion. Its controller waits for the full current deletion group
to pass and remove its owned containers/credential files before starting;
failure of that group leaves the API invocation unstarted. Queuing is not
execution or passing evidence.

The repaired deletion group subsequently times out again in its unchanged
100,002-card candidate seed. Its owned containers and credential files are
removed. The current schema-124 API controller exits on that prerequisite
failure without starting the suite. The full checkout-layout API verification
therefore remains unexecuted; the earlier 690/691 result remains retained.

The subsequent complete schema-125 deletion group passes all seven original
contracts, complete runtime readiness and exact checkpoint-clock checks. Its
containers and credential files are independently confirmed absent. The fresh
full **691-case current Linux API invocation has now started** with the
successful schema-125 locked build and corrected checkout ancestry. It retains
all original cases, source-root assertions and Linux private-staging coverage;
there are no filters, retries or deadline increases. Its live controller is
authoritative until a terminal result exists. Current API success and complete
immutable-image CI are not yet proved.

Subsequent schema-126 notification counter verification passes the whole
migration/refusal gate, complete original nine-case native notification phase
(632.93 seconds, one result per case, no skips/flaky/report errors), original
tenant/RLS/runtime-role/Reminder SQL gates and expanded readiness checks for
all 126 independently applied migration entries. The
[notification counter record](notification-stream-clocks.md) retains the
failed overlong-host helper attempt, scope, cleanup and pending watch/ordering,
capacity and release checks. The unchanged complete schema-125 API invocation
continues against its own original read-only build; it is not restarted or
promoted to current schema-126 full API proof.

That unchanged schema-125 Linux API invocation subsequently finishes **691/691**.
Independent TRX inspection matches all 691 actual case rows and declared counters:
zero failures, errors, timeouts, aborts or exclusions. The architecture source-root
assertion and all 24 Windows-excluded Linux file-delivery cases execute. Its
owned container is independently absent. Earlier 690/691 and Windows 667/691
reports remain retained; they are not rewritten as passing evidence.

The newer compiled schema-126 Linux Domain invocation passes **758/758**.
Actual XML case rows and declared assembly counters agree, with zero failures,
skips or report errors. The pinned runtime, original full case set, read-only
compiled artifacts and private report policy remain. A fresh full 691-case Linux
API invocation has started against that newer build; its live handle remains
authoritative until a terminal result exists. Neither local suite certifies
the current immutable-image pipeline, whose metadata job remains queued without
an assigned runner at the recorded inspection.

That full schema-126 Linux API invocation subsequently passes **691/691**.
Independent TRX inspection confirms 691 actual Passed rows and matching
total/executed/passed counters, with zero errors, failures, timeouts, aborts or
excluded cases. Its pinned runtime digest is unchanged and the owned test
container is independently absent. This build predates the concurrent
notification-route rediscovery repair; it does not certify that later code.

The [notification route repair](notification-stream-clocks.md) passes its six
new API regression cases and a full locked solution build. A fresh complete
Linux API invocation has started against the repaired compiled artifacts,
including all original 691 cases and the six additions, without filters,
exclusions, test retries or deadline increases. The actual terminal report must
prove its full scope. Native watch/order and 100,000-notification capacity runs
remain live; current immutable-image CI remains queued. None is promoted from
an unfinished run or an older successful build to current release acceptance.

The route-repaired full Linux API invocation subsequently passes **697/697**.
Independent TRX inspection finds 697 actual Passed rows and matching
total/executed/passed counters, with zero failures, errors, timeouts, aborts,
excluded or pending cases. Its owned test container is independently absent.
This compiled build requires schema 126 and predates migration 127; it does
not certify the later required-ledger change. A fresh complete Linux invocation
now uses the schema-127 compiled artifacts with the same pinned runtime,
read-only source/content-root mapping and full unfiltered suite. Its terminal
report and current immutable build-once pipeline remain required.

The full schema-127 Linux API invocation subsequently passes **697/697** on
2026-10-09. Independent TRX inspection confirms 697 actual Passed rows and
matching total/executed/passed counters, with zero failures, errors, timeouts,
aborts, excluded or pending cases. The original unfiltered suite includes the
Linux file-delivery coverage and source/content-root contract. Its owned test
container is independently absent. This certifies the recorded schema-127
compiled API build; the later frontend changes and current immutable-image
pipeline have separate verification requirements.

On 2026-10-09, the schema-127 **unfiltered persistence executable** completes
successfully against its own fresh PostgreSQL/pgvector database and restricted
API/Worker logins. The actual invocation has no mode arguments, filters,
exclusions or test retries; it uses the original compiled contract payload and
original budgets. Its retained output reaches the final upload-persistence
success marker, with 68 passing contract-summary lines and no unhandled
exception. These are summary markers, not 68 independently collected test
cases. Successful executable exit is evidenced by the helper advancing to its
subsequent SQL commands; the retained runner log matches executable output.
This covers the executable's original default path, including its nested
Worker/preview contracts. Special mode-only branches in
[Program.cs](../../tests/StrataAI.Persistence.Contracts/Program.cs) require
their own original command invocations; this result does not assert that every
supported mode or every PRD acceptance criterion has executed.

The enclosing helper subsequently exits unsuccessfully in the separate
runtime-role script. That script expects a fresh database and checks exact
global receipt counts, whereas the completed persistence executable retains
audit-bearing fixtures and receipt history by design. Its earlier tenant
schema and RLS scripts complete, but the combined helper is not recorded as
passing. The failed attempt and private diagnostics remain retained.

A separate fresh schema-127 invocation then passes all four complete unchanged
original companion gates, in their original prerequisite order:
[tenant schema](../../scripts/ci/test-tenant-schema.sh),
[RLS](../../scripts/ci/test-rls.sh),
[runtime roles](../../scripts/ci/test-runtime-roles.sh) and
[Reminder delivery SQL](../../scripts/ci/test-card-reminder-delivery.sql).
All 127 staged migrations and gate scripts match source after normalizing
both sides' newline encoding. The original counts, scopes, assertions and
budgets are retained. Both invocations' owned containers and environment
files are independently confirmed absent. The full 32-case Board browser
invocation remains active; current immutable build-once release acceptance
remains outstanding. No SQL assertion is relaxed to accommodate a populated
contract database.


## Completed schema-129 persistence and schema-128 API invocations

The complete default schema-129 persistence executable subsequently passes
against its own PostgreSQL/pgvector database and restricted API/Worker logins.
It uses the original default path without mode arguments, exclusions, filters,
case retries or changed concurrency/scale budgets. Exit/outcome are successful,
with 68 passing summary lines and no unhandled exception; these are summary
markers, not an independently collected case count. All 129 staged migrations
and role provisioning source still match normalized current source. Its owned
containers and credential environments are independently absent. Private report:
`attachment-sweep-schema129-full-persistence-native-20261009`.

Unlike the earlier schema-127 enclosing helper, this invocation performs no
fresh-database SQL companion checks after retaining the default fixture history.
The separately executed schema-129 SQL/security/migration gates remain their
own evidence. Default execution does not execute every special-mode branch.
It is not certification of later migrations or the whole PRD acceptance scope.

The full Linux API invocation using compiled schema-128 Debug artifacts also
finishes successfully: **697 actual result rows, 697 unique execution IDs and
697 Passed outcomes**, with matching declared total/executed/passed counters.
Failed, error, timeout, aborted, nonexecuted, pending and inconclusive counters
are zero. No filter or retry is added. It uses the pinned .NET 10.0.12 runtime
image with private source/content-root mappings. Private report:
`work-replay-clock-full-api-linux-schema128-native-20261009`. Owned test container
and credential environments are independently absent.

The compiled API payload predates migrations 129–132. The read-only source/
content-root bind followed the host checkout as it changed during execution;
it was not a frozen Git SHA. This is the completed original source-test scope,
not proof of current-schema PostgreSQL behavior, browser acceptance or an
immutable current release image. Current build-once CI and the independently
reproduced later full-persistence timeout still require resolution.


## Completed schema-131 diagnostic baseline and route-batching experiment

The original complete default schema-131 persistence executable subsequently
passes, with successful executable/helper outcome, 68 passing summary markers
and no unhandled exception. It retains the original default path, arguments,
counts, concurrency/scale budgets and lack of case retries. All 131 staged
migrations and role provisioning source match normalized current source;
owned containers and credential environments are independently absent. Report:
`invitation-mail-schema131-full-persistence-plans-native-20261009`.

This invocation enables private thresholded nested-query plan logging. Its
completed original 100,002-Card seed is captured at 17,013.731 ms. That is an
instrumented local statement observation, not server mutation p95 or a fix
for the independently reproduced CI timeout. It does not certify schema 132,
every special-mode branch or immutable current images.

A separate schema-132 database experiment batches Card route INSERT/UPDATE
projections using statement transition tables. It retains the original
invoker role, canonical per-route clock guard, forced RLS, grants and row-level
DELETE behavior. The prototype is isolated outside the checkout and has not
been adopted as a production migration. It changes no source fixture size,
command deadline, assertion or retry policy.

Five complete companion checks pass: tenant catalog, RLS, runtime roles,
original routing isolation and the full original four-route clock/body/tamper/
no-op/canonical-update assertion script. The current-schema preparation uses
the existing populated-source fixture and excludes its deliberately orphaned
pre-clock-upgrade row; the current clock guard already refuses that row.
All original post-upgrade assertions remain intact. The companion's owned
container and credential environments are removed. Report:
`card-route-batch-prototype-security-native-20261009`.

The original complete seven-contract deletion mode with the private prototype
remains live. It has passed the original 100,002-Card candidate traversal, but
its full-scale mutation and terminal result are still required. The original
132-entry readiness mode has run first; the experiment records no new ledger
version and is not current release certification. Report:
`card-route-batch-prototype-deletion-native-20261009`. No production route
migration is adopted on the strength of a partial run.


## Card route statement regression gate

The [Card route statement gate](../../scripts/ci/test-card-route-statement-semantics.sql)
is now required by PostgreSQL CI after the original routing-isolation check.
Two transaction-local Organization graphs verify multi-row INSERT through a
returning CTE, multi-row UPDATE, INSERT with ON CONFLICT UPDATE, zero-row writes,
DELETE and intentional subtransaction rollback. After each statement, routes
must match the canonical Card tenant, Board, List, lifecycle and both clocks.
The CTE must insert exactly two Cards; deletion must leave no fixture Cards.
Updates and deletes are restricted to the two fixture Organizations, and the
whole fixture transaction rolls back. These are admin projection-consistency
checks; the existing restricted-role/RLS checks remain separately required.

The exact committed gate passes on fresh schema 132 with both the current
row trigger and the isolated transition-table prototype. All 132 migration
files plus role provisioning match current source after newline normalization,
and the staged gate matches the committed script. Its owned container and
credential environments are independently absent. Private report:
`card-route-statement-ci-gate-native-20261009`.

An earlier isolated statement experiment also passes for both implementations,
followed by the five original tenant/RLS/role/routing/route-clock companion
checks. Report: `card-route-batch-statement-semantics-native-20261009`.
These checks establish after-statement behavior for the tested operations;
they do not certify arbitrary same-statement consumers, concurrent route lock
ordering, production latency, all acceptance criteria or immutable release
images. The original complete deletion run remains live and the prototype is
still outside production migrations. PRD-01 remains open at **34% estimated
work remaining**, a planning estimate rather than a passed-test percentage.


## Schema-133 Card route projection verification

The [statement projection record](card-route-statement-projection.md) documents
the forward migration, exact prototype equivalence apart from its ledger insert,
complete original seven-contract deletion result, paired original seed measurements
and their limits. The locked current solution build has zero warnings/errors.
All 18 current migration/security/clock/routing/statement gates pass; all 334
staged source files match after newline normalization and owned gate resources
are independently absent. The failed CRLF staging report is retained separately.

The current 133-entry API/Worker readiness mode passes. The original complete
unfiltered default persistence invocation remains live on its own fresh database
and locked current compiled payload. It has no added case retry, reduced fixture,
weakened assertion or budget. Immutable current-image CI and full acceptance
remain pending; neither the completed prototype nor live default path is counted
as an achieved release gate. PRD-01 remains open at **34% estimated work remaining**.


## Current four-width Owner Portal phase

The [Portal screen record](portal-screen-verification.md) retains the complete
4/4 current compiled Production/real-PostgreSQL/Nginx phase, first-attempt result,
independently verified cleanup, optional verification-policy boundary and all
323-case browser-shard collection scope. The previously inert Browse action now
has a clear unavailable state, without claiming PRD-80 implementation. Full
current API and default persistence invocations remain live; immutable CI and
aggregate acceptance are not inferred from this separate Portal phase.


## Public support reference boundary

The [error-reference record](public-error-references.md) documents the previously
unbounded header projection, the API-aligned identifier validation, failing
pre-repair regression, 98 selected intermediate component/request passes and
18 final request-boundary passes including later control-character coverage.
It retains the distinct scopes and report-placement correction. This does not
complete all user-visible-error/NFR coverage or current immutable-image release
acceptance; the full current API/default persistence invocations remain separate.


## Complete current schema-133 persistence result

The original complete schema-133 default persistence executable finishes with
successful executable/helper outcome, 68 passing summary markers and no
unhandled exception. The original 133-entry API/Worker missing/restored readiness
mode also passes. Counts, assertions, default arguments, concurrency/scale/page
budgets and no-case-retry policy remain unchanged. The default path does not
execute every special-mode branch; summary markers are not separately collected
case counts.

The current scale phase executes 826 bounded mutation jobs, 105,201 ready work
events and one ready terminal across 5,000 active + 100,000 archived Cards and
200 Lists, in 1,362,836 ms, with maximum leased page time 941 ms. These are local
compiled-runtime deletion observations, not normal HTTP p95 or immutable release
performance certification. All 133 migrations plus role source match normalized
current source. Owned containers and credential environments are independently
absent. Private report: `card-route-batch-schema133-readiness-full-native-20261009`.

This completes the previously live default invocation for that scope. Earlier
chronological pending states and failures remain retained; neither an older pass
nor this default result replaces the current full API result, browser matrices,
all special modes, current build-once CI or the remaining acceptance/DoD.


## Complete current schema-133 API result

The original unfiltered Linux API suite completes successfully against the
current compiled schema-133 backend and actual source/content-root mappings.
The terminal TRX contains **697 result rows, 697 unique execution IDs and 697
passes**, matching the declared counters, with zero failures, errors, timeouts,
aborted, pending or unexecuted cases. The pinned .NET 10.0.12 runtime and four
private content-root mappings are retained in the private outcome manifest.

Private report: `card-route-batch-full-api-linux-schema133-native-20261009`.
The owned API test container is removed. The source bind is not a frozen Git
checkout; frontend/docs edits during this run do not change its already compiled
backend. This completes that previously live invocation, not current immutable
image CI, browser acceptance or every PRD requirement.


## Organization settings support references

The [settings error-reference record](organization-settings-error-references.md)
retains the failing source regression, corrected invalid replacement-profile
fixture, final 61/61 source passes and complete 6/6 desktop/phone
settings/realtime/telemetry browser passes. The first browser attempt's 4/6
result remains retained, with the missing scoped Worker and keyboard-focus
preconditions corrected while preserving original assertions and deadlines.
All 323 browser cases remain collected into four complete file partitions.
Settings response references do not complete other screens or immutable CI.


## Authentication and recovery support references

The [identity error-reference record](identity-error-references.md) retains six
pre-repair failures, the initial 244/244 authentication pass and final
309/309 authentication/API-boundary source pass after additional malformed,
network, unreadable-acknowledgment and retry/cancellation coverage. Typecheck,
lint and private web build pass. This does not substitute for current native
browser or immutable-image CI acceptance.
