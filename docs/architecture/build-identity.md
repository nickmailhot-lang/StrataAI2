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
sets standard OCI revision/version labels from the same build arguments.

`test-build-identity.sh` compares the three running release images' responses and
OCI labels with the expected CI commit/version. It then starts the exact API and
Worker images in isolated Demo containers with deliberately false runtime build
variables, requiring the embedded identifiers to remain correct. This check runs
before feature fixtures; a mismatch blocks required-ci and the release bundle.
Images are not rebuilt for these tests or for bundle generation.

The SPA also displays its compiled version and full revision in the application
footer, including authentication and Portal routes. These use the same Vite
build variables validated by the metadata emitter; the footer does not fetch a
runtime label or use browser storage. Long revisions wrap at phone widths.
The ARCH-02-AC-004 browser case compares visible footer text with the deployed
metadata asset and API runtime identity at desktop/phone widths. Local source
checks and browser collection pass; exact-image browser execution is pending.

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
