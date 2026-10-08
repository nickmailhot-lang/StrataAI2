# Security input integrity and evidence identity — ARCH-11

Before installing dependencies, auditing packages, loading application images or invoking scanners, the security job downloads the same `strataai-images-<sha>` artifact used by integration. It verifies `SHA256SUMS`, covering all three image archives and the original build metadata document. It then requires the metadata repository, full SHA, image tag, workflow run ID and application version to match the triggering workflow and initial metadata job. Failure stops the required security job before image loading/scanning.

After verification, the job copies the original metadata bytes into `security-artifacts/build-metadata.json`. The existing always-run security evidence upload retains that document alongside any generated CycloneDX SBOMs. Existing SBOM filenames and schemas are preserved. A metadata-only artifact after an early audit failure indicates provenance, not successful scans or a passing security gate. The stable aggregate continues to require security success before release bundling.

The workflow never rebuilds application images in the security job. Checksums detect archive corruption; they do not replace trusted artifact production, scanner results, or controlled promotion. Diagnostic/capacity artifact provenance elsewhere is still a separate ARCH-11-FR-009 requirement.

Validation extracts and executes the actual workflow shell step with synthetic archive bytes and the independently retained canonical source identity. Matching input passes and preserves identical metadata bytes. Four negative cases—corrupted Worker archive, mismatched commit, mismatched workflow run and mismatched version—fail before creating security evidence. An initial Windows fixture writes CRLF checksum lines; the corrected harness uses Unix newlines, matching the hosted Linux checksum producer. These are transport/identity checks, not Docker image loading or vulnerability scan proof.

Thirty-five coverage/mutation tests pass, including refusal of bypassed security checksums, verification moved after image loading, omitted metadata, ignored verification failure and a divergent version. Actionlint validates workflow syntax and expressions. Current hosted security execution remains required before claiming exact-image scanner acceptance.

Related: [initial build metadata](initial-build-metadata.md), [source result artifacts](source-test-results.md), [integration groups](integration-ci-groups.md), [documentation index](../README.md).
