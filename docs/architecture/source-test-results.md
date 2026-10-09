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
