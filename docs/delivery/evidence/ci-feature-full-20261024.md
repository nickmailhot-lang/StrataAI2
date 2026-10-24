# CI profile split validation — 2026-10-24

Scope: working-tree changes based on main `f1cd09ee80e91379a575a051835784d829af9565`; not hosted qualification or a measured duration claim. The added ARCH-11-VS-94 hierarchy remains In Progress and its acceptance criteria remain Unverified.

## Local results

- Combined feature/integration/delivery/metadata/dependency checks: 394 passed, zero failures/skips (`node --test tests/feature-ci.test.mjs tests/integration-suites.test.mjs tests/delivery-progress.test.mjs tests/build-metadata.test.mjs tests/ticket-dependencies.test.mjs`). This includes 36 feature CI checks and 290 existing full-integration safeguards.
- Full integration verifier: four groups, seven isolated executions, 130 registered steps retained.
- Generated feature runtime drift check passes.
- Actionlint 1.7.11 validates the workflows with ShellCheck disabled (ShellCheck is not installed).
- Browser fixture TypeScript passes (`npm run typecheck:browser`).
- Delivery ledger validation and report generation pass; no prior lifecycle/evidence is rewritten or accepted anew.
- Git whitespace/diff validation passes.

## Retained development failures and fixes

The first combined contract run had four failures: three delivery checks refused the removed historical `.github/workflows/ci.yml` implementation path, and one new runtime guard incorrectly treated the literal filename `test-board-permission-matrix.sh` as an unresolved GitHub matrix expression. The compatibility workflow now preserves the historical path while routing manual requests to full sanity. The new guard checks the actual expression syntax. The corrected combined run passes all 394 checks. No product/runtime assertion, full-suite mutation test, retry policy or accepted historical evidence was relaxed.

The actionlint archive extraction initially reported a container ownership error after writing its executable; the downloaded 1.7.11 executable was verified and ran successfully. Ordinary Git HTTPS push lacks command-line credentials in this workspace; publication uses the connected GitHub API.

## Required follow-up evidence

Hosted CI must validate this exact proposed head, including cached image builds and real-service runtime. Docker and .NET are not installed in this workspace, so those checks have not been executed locally. No wall-time improvement is claimed until representative leaf and shared/security PRs are compared against full sanity at the same revisions. Release/MVP acceptance remains separate. No deployment or branch-protection administration is performed by these source edits.
