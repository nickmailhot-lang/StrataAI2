# Source test result artifacts — ARCH-11-FR-018/090

Required source jobs now retain machine-readable result summaries in `source-test-results-web-<sha>` and `source-test-results-dotnet-<sha>`. Both download the [initial metadata document](initial-build-metadata.md) from the same workflow run. Each summary embeds its canonical repository, source SHA, workflow run identity and application version; it cannot be substituted with another run's metadata when executed in GitHub Actions.

The complete web unit/component suite still runs, with Vitest's default console reporter plus JUnit output. The complete Domain and API source suites still run, with the pinned MTP 2.4.0 TRX extension in the two test projects. The [Microsoft report extension documentation](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-test-reports) explains registration and `--report-trx`. No filters, test retries, ignored failures, product dependencies, or image rebuilds are added to CI.

Raw XML lives under the runner's temporary directory. The [summary exporter](../../scripts/ci/summarize-source-tests.py) publishes only schema/version, canonical build metadata, report completeness, outcome counts, opaque case hashes, bounded durations, and source file/static .NET method identifiers where recognized. It excludes raw test display names/theory arguments, assertions, rendered DOM, console output, stack traces, attachments, and machine/user metadata. Opaque case hashes distinguish every recorded case, including duplicates; they are diagnostic report identities, not domain record IDs or test-name replacements in console logs.

The exporter validates available report counters against actual case rows. Missing, malformed, oversized, empty, entirely skipped, or inconsistent reports are explicitly marked and fail the summary step. Failed/error cases retain their outcomes and also produce a failing exit status. Both summarization and upload run after test failure with `always()`; the original source test failure remains mandatory. Only the summary directory is uploaded. If an earlier failure prevents a report, the artifact records the unavailable scope rather than advertising an empty passing suite.

Eight parser/privacy/CLI regressions pass. They exercise JUnit/TRX outcomes, native durations, retained build identity, private-content exclusion, counter mismatches, missing/empty/malformed/all-skipped input, duplicate cases, mismatched workflow identity, and failure exit preservation. Thirty workflow coverage/mutation tests pass, including rejection of raw report upload paths, skipped failure summaries, ignored publication errors, filtered mandatory API tests, and a missing source reporter. Actionlint validates the resulting workflow.

Actual reporter integration is checked separately: 43 focused web cases emit JUnit and summarize successfully; 17 focused notification API cases emit MTP TRX and summarize successfully. The complete local Windows Domain run emits 758 results, including 16 failures. Its pre-change compiled baseline has the same total and identical failing method/case counts. Those failures remain visible: the exporter records 742 passed/16 failed and exits unsuccessfully. The same current compiled Domain assemblies then pass all 758 cases in an invocation-owned isolated Linux runtime; that complete TRX also summarizes successfully. The owned container is removed, preserving existing services and volumes. This Linux run uses compiled source in an existing runtime image, not the current immutable release candidate or the full image gate.

The refreshed dependency locks restore in locked mode. The strict Release solution build has zero warnings/errors. Current complete hosted source/report artifact execution, exact-image integration/security/release gates, broad diagnostic provenance, full acceptance/DoD and the existing native capacity investigation remain required before ARCH-11 closure.

```sh
python3 -m unittest discover -s tests -p source_test_reports_test.py
node --test tests/integration-suites.test.mjs
```

Related: [mandatory integration groups](integration-ci-groups.md), [runtime build identity](build-identity.md), [documentation index](../README.md).
