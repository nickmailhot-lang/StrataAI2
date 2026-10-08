# Initial build metadata — ARCH-11-FR-006/008

The CI `metadata` job checks out the triggering commit and runs the dependency-free [metadata emitter](../../scripts/ci/build-metadata.mjs). The emitter compares actual `git rev-parse HEAD` with `GITHUB_SHA` before publishing anything. A mismatch, malformed identity/version, missing output destination, or publication failure fails the job with a fixed diagnostic. It does not fall back to a moving branch or `development` identity.

The initial document includes repository, full commit SHA, 12-character short SHA, workflow run ID/number/attempt, application version, optional release version, full-SHA image tag, a UTC creation clock, and the three exact image references. Numeric run identities remain strings, including values above JavaScript's safe integer range. Known scalar values are exposed as job outputs; carriage returns and newlines are rejected before they can become output records.

Main/PR/manual branch builds derive their version from the checked-in root package version plus the workflow run number. The existing `0.1.0-<run-number>` convention is preserved. A prerelease package version appends the run to its prerelease identifiers and retains any build metadata. Release tags must use `v<SemVer>`; for example, `v1.2.3-rc.1` emits `1.2.3-rc.1`. Malformed release tags fail instead of silently receiving a branch-build version. Version validation follows [SemVer 2.0](https://semver.org/), within the existing hosts' 80-character build identity limit. All image tags remain the immutable full commit SHA, including release-tag runs.

All three source quality jobs depend on metadata. The web production build, image build arguments, and integration expected identity consume its outputs. Both `source-quality-gate` and `required-ci` explicitly reject a failed, cancelled, or skipped metadata result. Existing runtime/OCI identity and override-resistance checks continue to compare the actual retained web/API/Worker images with these expected values.

The initial job uploads `strataai-build-metadata-<sha>`. `build-images-once` downloads that document, checks its expected commit/tag/version/run identity, and copies it beside the three exported image archives. `SHA256SUMS` includes the metadata document. Each mandatory integration group verifies that manifest before loading the same built images. After all required gates pass, release bundling copies this original metadata document from the image artifact rather than reconstructing it later. Release checksums cover that copied document too. No application image is rebuilt downstream.

Local evidence: 27 metadata tests and 25 integration coverage/mutation tests pass together (52 total); Actionlint validates workflow syntax/expressions. The command-line regression creates its own synthetic Git repository, proves wrong-checkout refusal before any metadata/output file is created, then verifies publication against the real checked-out commit. Other cases cover PR merge identity, release/prerelease versions, run ID precision, malformed context/versions, newline injection, and impossible calendar input. Wiring mutations reject dropped metadata dependencies, mismatched image versions, lost metadata checksums, and downstream reconstruction.

The actual version consumers are also checked locally with `1.2.3-rc.1+build.001`: a fresh locked Release solution build succeeds with zero warnings/errors; an independent PE metadata reader verifies the exact revision/version in both compiled host assemblies; and a separate production Vite build emits the same revision/version in its metadata asset. These builds use separate proof output directories and replace no mounted runtime files. This proves that representative prerelease/build metadata survives actual compilation, rather than only the emitter's unit cases. It does not prove an executed release-tag image pipeline or deployable bundle.

```sh
node --test tests/build-metadata.test.mjs tests/integration-suites.test.mjs
node scripts/ci/verify-integration-suites.mjs
```

This is scoped source/CLI/workflow/compiled-consumer evidence. Current exact-image/runtime/release execution, actual release-tag pipeline acceptance, broader metadata coverage of every diagnostic/test artifact under ARCH-11-FR-009, clean-host release verification, and the complete architecture acceptance criteria remain required. Neither local tests nor a generated metadata document establishes full ARCH-11 completion.

Related: [embedded runtime identity](build-identity.md), [mandatory integration groups](integration-ci-groups.md), [run retention](ci-run-retention.md), [documentation index](../README.md).
