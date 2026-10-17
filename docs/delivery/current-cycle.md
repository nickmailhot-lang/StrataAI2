# Incremental delivery progress

Baseline: `e90c6442ccc0b928a38db8793853ef03abe1bf3d`. Counts describe the ledger, not product completion percentages.

PRDs: 0/88 verified Done (80 feature + 8 UX); 12 architecture tickets tracked separately.
Work items: 0/1503 Done. Remaining canonical numbered acceptance criteria: 391; functional obligations: 951; child criteria: 2370. Unverified includes unknown and partially tested work; it does not assert code is absent.

| Kind | Total | Done |
| --- | ---: | ---: |
| Epic | 8 | 0 |
| PRD | 100 | 0 |
| Vertical Slice | 462 | 0 |
| Task | 466 | 0 |
| Test | 467 | 0 |

Implemented but unvalidated: 13. Blocked: 4. Known MVP-critical outstanding: 45; unclassified records: 1291. Weighted progress: not measured.

## 1. Work completed

Implemented the smallest demonstrated link/sign-in gap: private/missing Board entry offers generic Sign in; validated authentication returns to the original canonical Board/Card without granting access.
Validated six complete return workflows, all four widths, 200% text at tablet, lost-response original-key recovery, canonical Card edit/reload, Back/Close and outsider/Portal denial.
Fixed the ledger history subprocess buffer limit and added an actual-retained-ledger CLI regression; 372 source/tracking contracts pass. No work item has met all Done gates.

## 2. Implemented, awaiting validation

PRD-25-VS-03, PRD-25-VS-03-T-01, PRD-25-VS-03-T-01-TEST-01, PRD-25-VS-03-T-02, PRD-25-VS-03-T-02-TEST-01, PRD-25-VS-03-T-03, PRD-25-VS-03-T-03-TEST-01, PRD-01-VS-04, PRD-01-VS-04-T-01, PRD-01-VS-04-T-01-TEST-01, PRD-01-VS-04-T-01-TEST-02, PRD-25-VS-03-T-04, PRD-25-VS-03-T-04-TEST-01

## 3. Current work in progress

PRD-01-VS-04 and its implementation task are Blocked on broader regression/release/review/integration; two focused Test records are in Review.
PRD-25-VS-03-T-04 and its Test are in Review; foundational PR #96 remains open.

## 4. Blockers and dependencies

PRD-25-VS-03: Current hosted required-ci, final acceptance review and target-branch integration are pending; local source/delivery checks passed. (owner: delivery-integration-owner)
PRD-25-VS-03-T-03: Current hosted required-ci, final acceptance review and target-branch integration are pending; local source/delivery checks passed. (owner: delivery-integration-owner)
PRD-01-VS-04: Focused sign-in return is validated locally; unchanged broad Board creation tests fail on current and original Demo applications. Full root-cause resolution, current Production/immutable CI, review and origin/main integration remain pending. (owner: delivery-integration-owner)
PRD-01-VS-04-T-01: Focused sign-in return is validated locally; unchanged broad Board creation tests fail on current and original Demo applications. Full root-cause resolution, current Production/immutable CI, review and origin/main integration remain pending. (owner: delivery-integration-owner)
Focused sign-in return is validated locally; unchanged broad Board creation tests fail on current and original Demo applications. Full root-cause resolution, current Production/immutable CI, review and origin/main integration remain pending.
The original Board workflow also fails, but its results vary by viewport and timing; this is evidence of an existing issue, not a completed root-cause analysis or permission to waive current failures.
Local topology is compiled Demo API + Vite. Current PostgreSQL/RLS, immutable release images, managed-provider and Production acceptance are not certified.

## 5. Next highest-priority deliverable

PRD-04-VS-01-T-02: reproduce and diagnose loss of the Board creation dialog during live admission, then implement the smallest safe fix with the original workflow assertions and deadlines intact.

## 6. PRD progress changes

PRD-01 gains a locally validated narrow implementation under VS-04; full parent acceptance remains open.
PRD-02 authentication, retry and acknowledgment contracts are preserved. PRD-04 gains an explicit evidence-backed creation-dialog follow-up.
PRD-25 gains a validated history-size fix. No feature PRD or parent slice is closed.

## 7. Completion evidence

9b26f63d4c1665b97f2b6d7950ea33c8c2be2d4d: full frontend 2819 passed, 0 failed, 24 existing deferred capacity skips; none promoted to passing acceptance.
9b26f63d4c1665b97f2b6d7950ea33c8c2be2d4d: focused real Demo API browser 6/6 passed; complete unchanged Board workflow 0/3 passed on the frozen current run; original comparison 1/3 passed.
Web/browser types, lint and web build passed; developer inspected all four actual captures, including 768px at 200% text.
Browser registry: 432 cases / 158 intact files / four shards. Registry verification is not browser execution.
d8ef877cd991eb2a2e64df98e825556dfd613734: 372 source/ledger/architecture contracts passed; retained history-size failure and fixed invocation both recorded.
Hashed artifact references are in progress.json and docs/delivery/evidence/cycle-02-*.txt. Failed browser traces/screenshots remain in the local review evidence package.

## Recent verified completed increments

None recorded under the new acceptance gates.

## Preserved historical evidence

HIST-UX-94: 2800 component cases passed; 24 deferred cases; 24 production-runtime browser cases passed at recorded implementation. Not rerun or promoted to current acceptance. Source: docs/ux/delivery-report.md. Full UX PRD and current release acceptance remain unverified.
HIST-DEVELOPMENT-REFUSALS: Prefreeze fixture and decomposition failures retained and evaluated; final frozen 364-case suite passed without skips or relaxed assertions. Source: docs/delivery/evidence/failure-evaluation.md. Development diagnostics are not release or product acceptance.
HIST-CYCLE-02-PREFREEZE: Initial probes: 3 failed / 14 passed return components; private Card sign-in entry absent in browser. A test-only TypeScript matcher option was corrected. Original/current broad Board creation failures retained. Source: docs/delivery/evidence/cycle-02-failure-evaluation.md. Historical diagnostics and counterfactual results do not establish current full-workflow acceptance.
