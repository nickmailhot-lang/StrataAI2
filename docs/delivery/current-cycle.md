# Incremental delivery progress

Baseline: `e90c6442ccc0b928a38db8793853ef03abe1bf3d`. Counts describe the ledger, not product completion percentages.

PRDs: 0/88 verified Done (80 feature + 8 UX); 12 architecture tickets tracked separately.
Work items: 0/1507 Done. Remaining canonical numbered acceptance criteria: 391; functional obligations: 951; child criteria: 2377. Unverified includes unknown and partially tested work; it does not assert code is absent.

| Kind | Total | Done |
| --- | ---: | ---: |
| Epic | 8 | 0 |
| PRD | 100 | 0 |
| Vertical Slice | 462 | 0 |
| Task | 468 | 0 |
| Test | 469 | 0 |

Implemented but unvalidated: 19. Blocked: 4. Known MVP-critical outstanding: 49; unclassified records: 1291. Weighted progress: not measured.

## 1. Work completed

Identified and reproduced stale launcher focus after recovered Board cancellation: both new controlled cases fail on the previous implementation. Four-line repair passes both development cases. No Done claim.

## 2. Implemented, awaiting validation

PRD-25-VS-03, PRD-25-VS-03-T-01, PRD-25-VS-03-T-01-TEST-01, PRD-25-VS-03-T-02, PRD-25-VS-03-T-02-TEST-01, PRD-25-VS-03-T-03, PRD-25-VS-03-T-03-TEST-01, PRD-01-VS-04, PRD-01-VS-04-T-01, PRD-01-VS-04-T-01-TEST-01, PRD-01-VS-04-T-01-TEST-02, PRD-25-VS-03-T-04, PRD-25-VS-03-T-04-TEST-01, PRD-04-VS-01-T-02, PRD-04-VS-01-T-02-TEST-01, PRD-04-VS-01-T-03, PRD-04-VS-01-T-03-TEST-01, PRD-04-VS-01-T-04, PRD-04-VS-01-T-04-TEST-01

## 3. Current work in progress

Focus repair is merged into the canonical isolated branch and is Testing. Actual running-app cancellation and final full frontend remain pending.
API-host verification remains running; report platform and post-MVP capacity skips separately.

## 4. Blockers and dependencies

PRD-25-VS-03: Current hosted required-ci, final acceptance review and target-branch integration are pending; local source/delivery checks passed. (owner: delivery-integration-owner)
PRD-25-VS-03-T-03: Current hosted required-ci, final acceptance review and target-branch integration are pending; local source/delivery checks passed. (owner: delivery-integration-owner)
PRD-01-VS-04: Focused sign-in return is validated locally; unchanged broad Board creation tests fail on current and original Demo applications. Full root-cause resolution, current Production/immutable CI, review and origin/main integration remain pending. (owner: delivery-integration-owner)
PRD-01-VS-04-T-01: Focused sign-in return is validated locally; unchanged broad Board creation tests fail on current and original Demo applications. Full root-cause resolution, current Production/immutable CI, review and origin/main integration remain pending. (owner: delivery-integration-owner)
Full frontend/regression, current immutable release, review and target integration are still open. No assertion or timeout is changed.

## 5. Next highest-priority deliverable

Wait for API-host verification to finish, then run both failing BoardScreen files and the affected Organization/Surface tests in an uncontended focused invocation. Preserve all assertions/timeouts. Build and run the new cancellation journey, then final full frontend sequentially.

## 6. PRD progress changes

PRD-04 gains one bounded UXA-06 task/test. Original identifiers, earlier outcomes and parent scope remain intact.

## 7. Completion evidence

work/cycle4-focus-before.txt: 2 failed / 2 (intended red regression). work/cycle4-focus-after.txt: 2 passed / 2; source freeze and native rerun pending.
fc1d347d complete frontend: 2821 passed / 4 failed / 24 existing deferred skips; all four failures remain a required gate.

## Recent verified completed increments

None recorded under the new acceptance gates.

## Preserved historical evidence

HIST-UX-94: 2800 component cases passed; 24 deferred cases; 24 production-runtime browser cases passed at recorded implementation. Not rerun or promoted to current acceptance. Source: docs/ux/delivery-report.md. Full UX PRD and current release acceptance remain unverified.
HIST-DEVELOPMENT-REFUSALS: Prefreeze fixture and decomposition failures retained and evaluated; final frozen 364-case suite passed without skips or relaxed assertions. Source: docs/delivery/evidence/failure-evaluation.md. Development diagnostics are not release or product acceptance.
HIST-CYCLE-02-PREFREEZE: Initial probes: 3 failed / 14 passed return components; private Card sign-in entry absent in browser. A test-only TypeScript matcher option was corrected. Original/current broad Board creation failures retained. Source: docs/delivery/evidence/cycle-02-failure-evaluation.md. Historical diagnostics and counterfactual results do not establish current full-workflow acceptance.
