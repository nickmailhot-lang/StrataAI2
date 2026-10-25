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

## First hosted run and repair

Run 40136553493 on proposed head `0e8467e16732822437f6c83607b6d5ba242b30ac` failed. Selector/contracts/secret scan, metadata, .NET source quality and the complete PostgreSQL job passed. The full web job failed in `release_artifacts_test.py`: its current-document assembly test still parsed the legacy `ci.yml` compatibility entrypoint. The missing assembly marker raised IndexError. The subsequent missing web-unit report and aggregate failures are consequences; image/security/browser/bundle execution did not qualify this head. The original run remains retained at https://github.com/nickmailhot-lang/StrataAI2/actions/runs/40136553493.

Repair: point the existing assembly test at `full-sanity.yml`, retaining its real shell-snippet execution, exact-source links and bundle-integrity assertions. Also run its 47-case suite in the early selector preflight to catch this class of wiring regression before long integration work. No release/runtime assertions or failure conditions were weakened.

Local repair checks: 47 release-artifact tests and 156 other report/evidence/recovery/provider/image-label tests pass (203 total). All 36 feature CI checks, Actionlint and whitespace checks pass. An additional attempt to run `release_auth_policy_test.py` fails locally because Docker Compose is absent (17 tests, 22 failing assertions/subtests); this is a local environment blocker, not claimed passing evidence. That unchanged suite still runs in hosted full sanity. Updated-head hosted qualification remains pending.
