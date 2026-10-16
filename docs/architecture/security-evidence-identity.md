# Security input integrity and evidence identity — ARCH-11

## SBOM image and build binding — 2026-10-15

The release verifier previously checked each application SBOM's tag/name and
inventory presence, but did not compare its declared image ID or OCI build
labels with the tested-image record. All **49 new substitution subcases** fail
against that baseline. Rewriting checksums cannot establish that a differently
identified SBOM describes the tested image.

The verifier now requires one Trivy ImageID and unique source, revision, version
and creation labels matching canonical build metadata for web, API and Worker.
Missing, duplicate, malformed or different identity fields fail with the fixed
`different_sbom_provenance` code. This applies to security inputs and both copies
in the assembled bundle. Config/manifest ID differences require a cryptographic
binding through the original saved archive: blob hashes, sizes, labels and both
IDs must match. Docker inspection still requires its loaded manifest descriptor;
SBOM metadata has no such descriptor. No matching label/tag alone authorizes an
ID conversion, and no image is rebuilt or re-inventoried by this check.

Security now requires **Verify SBOM image and build identities** after all SBOM
generation and before final evidence retention. It rechecks image/security
checksums and canonical identity with the same release input verifier. Failure
blocks security and therefore `required-ci`. Load steps retire only their own
generated inspection JSON after verification, restoring the retained input
directory's original checksummed file set. Always-run evidence retention remains
available after failure. Collector SBOM validation retains its separate receiver
schema contract.

All **47 release-artifact tests**, **18 image-provenance tests** and **282 CI
guards** pass, including both config/manifest conversion directions, changed
saved bytes, duplicate properties, substituted bundle copies and skipped,
bypassed, ignored or misordered validation. Workflow lint passes.

Independent inspection of exact `32236a3ddd74d0fbad32f921b3efbb174e53040d`,
[run 38805351041](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/38805351041),
verifies security artifact **12020104537** (**95,789 bytes**, SHA-256
`7918c5ee4d398d485d0246fce999799900551b0673bdd91cfbae612946d5deba`)
and canonical metadata artifact **12007117883** (**420 bytes**, SHA-256
`d3182234a1ef1f5ead7ff501cbaeafafbae804426342b6d63bdf40885c8593db`).
All six internal payload checksums match, metadata bytes are identical and
version is `0.1.0-1806`. The untouched actual artifact passes the new SBOM checks;
all three same-tag image-ID substitutions pass the prior checker but fail the
new checker, even with recalculated checksums. Both validator sources remain
unchanged during this inspection. The temporary extracted directory and
**1,839,083 bytes** are removed. No application images are downloaded, loaded or
built, no Docker/database/provider is used and no load test runs.

The actual image archives are not independently compared in this small-artifact
inspection. Checksums/declared identities rely on trusted artifact production;
scanner execution, vulnerability policy, full integration and complete release
acceptance remain required. The candidate's security job succeeds, but its
commands job fails on a registration HTTP 503 in the Work-event stage; other
integration groups remain live/queued at inspection. That failure requires its
own diagnosis and prevents a full-green claim. ARCH-11 and MVP remain open.

Before installing dependencies, auditing packages, loading application images or invoking scanners, the security job downloads the same `strataai-images-<sha>` artifact used by integration. It verifies `SHA256SUMS`, covering all three image archives and the original build metadata document. It then requires the metadata repository, full SHA, image tag, workflow run ID and application version to match the triggering workflow and initial metadata job. Failure stops the required security job before image loading/scanning.

After verification, the job copies the original metadata bytes into `security-artifacts/build-metadata.json`. The existing always-run security evidence upload retains that document alongside any generated CycloneDX SBOMs. Existing SBOM filenames and schemas are preserved. A metadata-only artifact after an early audit failure indicates provenance, not successful scans or a passing security gate. The stable aggregate continues to require security success before release bundling.

The workflow never rebuilds application images in the security job. Checksums detect archive corruption; they do not replace trusted artifact production, scanner results, or controlled promotion. Diagnostic/capacity artifact provenance elsewhere is still a separate ARCH-11-FR-009 requirement.

Validation extracts and executes the actual workflow shell step with synthetic archive bytes and the independently retained canonical source identity. Matching input passes and preserves identical metadata bytes. Four negative cases—corrupted Worker archive, mismatched commit, mismatched workflow run and mismatched version—fail before creating security evidence. An initial Windows fixture writes CRLF checksum lines; the corrected harness uses Unix newlines, matching the hosted Linux checksum producer. These are transport/identity checks, not Docker image loading or vulnerability scan proof.

Thirty-five coverage/mutation tests pass, including refusal of bypassed security checksums, verification moved after image loading, omitted metadata, ignored verification failure and a divergent version. Actionlint validates workflow syntax and expressions. Current hosted security execution remains required before claiming exact-image scanner acceptance.

Related: [initial build metadata](initial-build-metadata.md), [source result artifacts](source-test-results.md), [integration groups](integration-ci-groups.md), [documentation index](../README.md).
