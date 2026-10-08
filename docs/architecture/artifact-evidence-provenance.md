# Capacity and diagnostic artifact provenance — ARCH-11-FR-009/090

Every integration evidence upload now stages its explicitly selected payload with the original `image-artifacts/build-metadata.json` and a `SHA256SUMS` manifest. This covers all 16 artifact scopes: operator metrics; Checklist/activity/notification/watch/search/comment/Card-move/work-lifecycle/deletion/Card-copy/attachment capacity; native Board capacity diagnostics; browser failure evidence; browser performance; and container failure diagnostics. Artifact names and retention periods remain unchanged.

The [payload scope manifest](../../scripts/ci/evidence-artifacts.json) records every allowed source and destination. The [stager](../../scripts/ci/prepare-evidence-artifact.py) validates the canonical metadata schema and matches repository, full commit SHA, workflow run ID and application version against the workflow environment. It copies metadata bytes without reconstructing identity. It never adds fields to strict measurement JSON or rewrites browser traces.

| Existing payload | Retained layout |
| --- | --- |
| One measurement JSON file | Original filename at artifact root, plus metadata/checksums |
| Native Board diagnostic directory | Original directory contents at artifact root, plus metadata/checksums |
| Browser result/report directories | Original `test-results/` and `playwright-report/` paths, plus metadata/checksums |
| Container logs and runner-temporary mail diagnostic | Explicit original basenames at artifact root, plus metadata/checksums |

The container log selection also removes the old extra indentation before `demo-metadata-compose.log` and explicitly places the runner-temporary mail diagnostic at its basename. It avoids an artifact root derived from unrelated absolute directories. Missing individual optional files are not fabricated.

If no selected payload exists, no artifact is staged: metadata alone cannot make absent measurements appear available. Nonempty payloads are published only after all copies and checksums succeed. Existing outputs, invalid/traversing destinations, duplicate/reserved filenames and symbolic links are refused. Hidden files/directories remain excluded, matching the uploader's existing default; every checksum therefore names a retained payload. The helper does not redact payload content or claim a passed test. Existing privacy checks, failure conditions, assertions, cleanup and aggregate gates remain mandatory.

All staging steps use the same `always()`/`failure()` and matrix ownership as their uploads. The workflow coverage verifier checks all 16 declared scopes, exact commands, identity source, adjacent upload ordering and staged paths; widening a selection requires a reviewed manifest change. All 96 existing integration steps retain their commands, environments, shells, conditions and action inputs except the intended upload-path changes. No application image is rebuilt.

Validation: 39 workflow coverage/mutation checks and eight source-report regressions pass. The new staging suite has 11 cases: ten pass on Windows, with symbolic links unavailable there; all 11 pass in an isolated Linux Python runtime. They verify byte preservation, checksum binding, nested browser layout, unselected-file exclusion, absent evidence, identity mismatch, unknown metadata fields, traversal/collisions, no overwrite, private failure output, hidden-file handling, atomic publication failure and symbolic-link refusal.

A separate isolated Linux harness executes all 16 actual workflow staging commands against synthetic selected files and the independently retained canonical build identity. Every command passes, every metadata copy is byte-identical, and all checksum entries verify. Its first extraction also selects an unrelated existing mail-provider preparation step; that harness invocation fails. The corrected extraction selects only the new evidence-staging commands and passes. These are fixture/layout/provenance checks, not current immutable application integration or browser acceptance. Owned proof containers are removed; existing services and volumes are preserved. Actionlint passes; current hosted artifact execution remains required before claiming complete ARCH-11 acceptance.

To inspect a downloaded artifact, open `build-metadata.json` to identify source/run/version, then verify `SHA256SUMS` from its root. This identifies the evidence's build and detects changed payload bytes; it does not substitute for the owning test/job result or prove image authenticity.

Related: [source test artifacts](source-test-results.md), [security evidence identity](security-evidence-identity.md), [integration groups](integration-ci-groups.md), [documentation index](../README.md).
