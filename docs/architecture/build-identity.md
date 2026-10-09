# Embedded build identity (ARCH-01-AC-002 / ARCH-11)

The [initial metadata job](initial-build-metadata.md) now verifies the actual triggering checkout and supplies the shared build identity. Its original document travels with the checksummed image artifacts into the gated release bundle. Current exact-image acceptance remains separate from local metadata/workflow proofs.

Release builds pass one Git commit and build version to all three Dockerfiles.
API and Worker publication sets `SourceRevisionId` and `Version`; the shared
`Directory.Build.targets` writes these into each host's assembly metadata.
Runtime registration reads the host assembly explicitly. Missing, duplicated or
invalid embedded identity fails startup with a safe diagnostic. Runtime
`STRATAAI_BUILD_REVISION` / `STRATAAI_BUILD_VERSION` variables are ignored and
cannot relabel an image. Environment, database and provider settings remain
external runtime configuration.

The web Docker build supplies `VITE_STRATAAI_BUILD_REVISION` and
`VITE_STRATAAI_BUILD_VERSION`. Vite validates these and emits the immutable
`build-metadata.json` asset. Nginx serves it at `/build-metadata.json`; API reports
its assembly values at `/api/runtime`, Worker at `/runtime`. Each Dockerfile also
sets OCI revision/version labels from the same build arguments, plus source
repository and creation-time labels from the verified canonical metadata.

`test-build-identity.sh` compares the three running release images' responses and
OCI labels with the expected CI commit/version. It then starts the exact API and
Worker images in isolated Demo containers with deliberately false runtime build
variables, requiring the embedded identifiers to remain correct. This check runs
before feature fixtures; a mismatch blocks required-ci and the release bundle.
Images are not rebuilt for these tests or for bundle generation.

## Complete image provenance before export

ARCH-11-FR-040 requires the source repository URL and build timestamp as well as
revision and version. CI supplies `STRATAAI_BUILD_SOURCE` from the initial
metadata repository and `STRATAAI_BUILD_CREATED` from its `createdAt`; all three
Dockerfiles use these arguments in their final runtime-image labels. These
follow the [OCI annotation keys](https://github.com/opencontainers/image-spec/blob/main/annotations.md):
`org.opencontainers.image.source` and `org.opencontainers.image.created`.
The same canonical build timestamp travels with the candidate and its artifacts;
downstream jobs do not generate new provenance or rebuild images.

After the three builds, before image export, CI inspects those exact tags and
runs `scripts/ci/verify-image-labels.py` against the original metadata document.
It requires exactly three distinct image IDs, one expected tag for each host,
and all four matching provenance labels. Missing, duplicate, extra or mismatched
images and missing/different labels fail the build job. Unknown inspection fields
are never printed. Unset source/time arguments on a development build do not
constitute valid release provenance.

The integration matrix and security job repeat the same complete provenance
check immediately after loading all three checksum-verified archives, using
their retained canonical metadata. This refusal happens before topology startup,
SBOM generation or image vulnerability scanning. Sixteen additional workflow
mutations protect both consumers against omitted/misordered inspection, foreign
metadata, skipped/ignored failure and scans before verification. Together with
the metadata tests, the workflow guard suite now passes 151/151. These are gate
regressions; the newer workflow still requires its own successful GitHub run.

Seven Python tests cover actual CLI acceptance/refusal, canonical metadata,
all four labels on each host, malformed/tag/identity failures and non-disclosing
diagnostics. Seventeen additional workflow mutations protect metadata-derived arguments,
the complete inspection, mandatory verification before export and source tests.
The combined workflow/metadata tests pass 135/135. This is source-level gate
evidence; current exact-image execution and full required-ci remain separate.

## Retained verified image identities

The pre-export verifier now emits `image-provenance.json`, containing only schema
version 1, the original canonical public build metadata and the three verified
Docker image IDs. It copies no raw inspection fields, environment variables,
runtime settings or provider details. The build job exports and checksums this
record beside the three image archives and original `build-metadata.json`.

Every integration execution and security job compares the loaded image IDs with
that retained record, in addition to matching all four labels and exact tags.
The security evidence retains the same record. Release input validation requires
matching build and image identities in both artifacts; bundle validation requires
the original record bytes and checksums to survive copying. A replacement record
cannot be made acceptable merely by recalculating bundle checksums.

These are Docker image IDs observed by the build runner, not invented registry
digests or attestations. Their representation depends on the Docker image store
(for example, an OCI index ID versus a configuration ID). CI requires its build
and loading runners to preserve the recorded identities; an identity change is a
gate refusal, not permission to regenerate the record. Operators on another image
store can use the original archives, checksums and canonical build metadata for
traceability. Clean-host deployment acceptance remains separately required.

Nine provenance tests cover CLI output privacy, loaded-ID substitution and record
shape/identity failures. Twenty-one release tests include absent records,
substituted security identity and replacement of both bundle records with
recalculated checksums. Seven added workflow mutations protect record creation,
export/checksums, both loaded-ID comparisons and security/release copying.
Combined workflow/metadata tests pass 164/164. These tests use explicit synthetic
fixtures; they do not establish execution of the current full GitHub pipeline.

The SPA also displays its compiled version and full revision in the application
footer, including authentication and Portal routes. These use the same Vite
build variables validated by the metadata emitter; the footer does not fetch a
runtime label or use browser storage. Long revisions wrap at phone widths.
The ARCH-02-AC-004 browser case compares visible footer text with the deployed
metadata asset and API runtime identity at desktop/phone widths. Local source
checks and browser collection pass. The local exact-image execution below also
passes this complete case at both viewport widths; full release CI is separate.

Local .NET source builds use the available source-control revision and MSBuild
version; builds without source-control identity report `development`. Local web
builds without explicit variables report `development` / `0.0.0-dev`. These are
development identifiers, not release evidence. Production CI requires the exact
commit/build values to match across all release assets.

Identity is operational provenance, not an authorization claim or secret. It
does not itself prove source or image authenticity: archive hashes, exact image
tests, scans and controlled artifact promotion remain necessary. Replacing
published assets or assemblies creates a different artifact and must go through
the release pipeline again.

## Local build-once archive and runtime evidence, 2026-10-09

At implementation commit `33dfad8d36a290189e46a3da4916ad57618fbba7`, all three
application images were built once from the clean checkout with local version
`0.1.0-local.33dfad8d`, the source repository URL and one captured UTC build time.
All four OCI provenance labels matched the local manifest. This manifest is
explicitly local evidence, without a fabricated GitHub workflow identity.

The three exported archives total 504,926,216 compressed bytes. Hash verification
covered all 49 content-addressed blobs and their referenced index, manifest,
configuration and layer objects. Each archive was loaded back without rebuilding;
the three pinned image IDs and provenance labels were unchanged. This Docker
store reports the OCI index digest as image identity; it was checked against the
archive index rather than incorrectly equating it with a configuration digest.

The disposable native fixture then ran those loaded image IDs for web, API and
Worker, without host-mounted compiled web assets or security-header overlays.
Only the Nginx upstream hostname was mapped to the isolated API container.
API and Worker readiness passed; deliberately false runtime revision/version
variables could not alter their embedded identity. The complete UI identity case
passed at desktop and phone widths (1/1, 27.1 seconds), followed by the intact
enabled-provider phase (2/2, 93.0 seconds) and expanded attachment phase
(18/18, 689.2 seconds). All reports have zero unexpected, skipped or flaky cases,
top-level errors and retries. Subsequent HTTP lifecycle/privacy/ownership checks
passed and the process exited successfully. The exact Worker additionally passed
nonroot, contained-root and missing-scratch-capability decoder checks.

Independent cleanup verified zero owned containers, cloned databases, provider
volumes or credential files. The three original running services were preserved.
Post-test verification again matched all image IDs, provenance labels and archive
checksums. Private fixtures, account data, logs and reports remain outside source.

This is bounded local execution with the explicit scanner protocol fixture and
local object provider. It does not prove deployed S3/provider behavior, a real
malware engine, security/SBOM gates or clean-host release-bundle acceptance.
[Implementation CI run](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37911544925)
was still queued when this evidence was recorded; full `required-ci` and retained
release artifacts remain unverified. ARCH-11 and PRD-14 remain open.

The [actual scanner engine runtime check](attachment-scanner-runtime.md) adds a
mandatory exact-Worker verification before export, separate from the existing
protocol-simulator fixture and its completed browser evidence.
