# Complete browser suite on isolated runners — ARCH-11

The four logical integration groups remain mandatory. The full-browser group now has four file partitions, so the matrix has seven isolated executions: commands, foundation, notifications, and full browser 1/4 through 4/4. Each execution downloads/checksums/loads the same three build-once images and owns its fresh PostgreSQL/API/web/Worker/provider topology. Concurrency remains capped at four runners, with fail-fast disabled; one failure cannot erase another partition's diagnostics. The stable aggregate requires success of the complete matrix before release.

Only the full browser invocation gains Playwright's `--shard` argument. `fullyParallel: false`, one browser worker, zero retries, deadlines, rate pacing, native gestures, assertions and fixture routing stay unchanged. Files stay intact and execute sequentially inside their owning topology; different partitions do not race the same Worker configuration or database. Each full-browser runner prepares its own identity provider/signing keys, optional-verification browser policy and automatic metadata/recipient/issuer routing, and retains its API/edge abuse checks. Other integration groups are not partitioned.

Playwright documents [file-level sharding without fullyParallel](https://playwright.dev/docs/test-sharding#balancing-shards). This avoids changing interaction timing through parallel tests inside a shared stack. It is intended to reduce the long complete-browser phase; no measured hosted duration reduction or 15–20-minute pipeline achievement is claimed before actual execution results exist.

The required web source job runs the [actual collection verifier](../../scripts/ci/verify-browser-shards.mjs) and [negative coverage tests](../../tests/browser-shards.test.mjs). The verifier collects the complete suite and all four partitions using the installed locked Playwright CLI, with the release headers/rate pacing flags. It compares case identity, project/file, timeout and expected outcome, rejects duplicate/foreign/omitted cases or split files, and checks unchanged sequential/no-retry configuration. Raw JSON/display names/configuration paths stay in an invocation-owned private temporary directory, which is removed after checking a resolved path boundary. Only counts are printed.

The current source collects 320 cases in 124 files. The partition sizes are 82/79/79/80; their disjoint union exactly equals the complete suite and every file has one owner. The initial ad hoc stdout parser encounters existing Node helper-test output and uses the wrong JSON case-id level; the final verifier reads Playwright's dedicated JSON output file and uses specification/project identity. Actual Windows and isolated Linux collection pass with the same counts. The Linux proof uses the read-only workspace/dependencies in an isolated Node runtime; fresh hosted locked installation/execution remains independently required. No browser/API requests are performed by this collection check.

Nine partition/collection regressions and 52 workflow coverage/mutation checks pass. The workflow verifier requires all seven executions, every full-browser partition/index/total, unfiltered invocation, source coverage check, fresh-image flow, unchanged strict gate and unique artifact names. An independent comparison preserves all 112 original integration steps' commands, environment, conditions, shells and action inputs, allowing only the partition argument and three artifact-name suffixes. Actionlint passes.

Diagnostic artifact names now include the shard index:

| Artifact | Name |
| --- | --- |
| Browser failure evidence | `browser-diagnostics-<sha>-<suite>-<shard>` |
| Container failure evidence | `container-integration-diagnostics-<sha>-<suite>-<shard>` |
| Scoped browser performance | `browser-performance-<sha>-<shard>` |

Other artifact names, payload schemas, retention, canonical metadata and checksum wrapping remain unchanged. A partition with no measured performance payload creates no measurement artifact. These source/collection proofs establish partition coverage and isolation wiring, not successful current immutable-image browser results or complete ticket acceptance. Existing live runs are left running; they are not restarted or cancelled to obtain a pass. Owned proof containers are removed, preserving existing services and volumes.

Related: [integration groups](integration-ci-groups.md), [evidence artifact provenance](artifact-evidence-provenance.md), [documentation index](../README.md).
