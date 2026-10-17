# Incremental delivery progress

Baseline: `e90c6442ccc0b928a38db8793853ef03abe1bf3d`. Counts describe the ledger, not product completion percentages.

PRDs: 0/88 verified Done (80 feature + 8 UX); 12 architecture tickets tracked separately.
Work items: 0/1499 Done. Remaining canonical numbered acceptance criteria: 391; functional obligations: 951; child criteria: 2376. Unverified includes unknown and partially tested work; it does not assert code is absent.

| Kind | Total | Done |
| --- | ---: | ---: |
| Epic | 8 | 0 |
| PRD | 100 | 0 |
| Vertical Slice | 462 | 0 |
| Task | 464 | 0 |
| Test | 465 | 0 |

Implemented but unvalidated: 7. Blocked: 2. Known MVP-critical outstanding: 41; unclassified records: 1291. Weighted progress: not measured.

## 1. Work completed

Reconciled the existing sign-in/Organization/Board/Card implementation and selected the narrow incoming-link sign-in gap.

## 2. Implemented, awaiting validation

PRD-25-VS-03, PRD-25-VS-03-T-01, PRD-25-VS-03-T-01-TEST-01, PRD-25-VS-03-T-02, PRD-25-VS-03-T-02-TEST-01, PRD-25-VS-03-T-03, PRD-25-VS-03-T-03-TEST-01

## 3. Current work in progress

PRD-01-VS-04: generic sign-in entry and safe Board/Card return context.

## 4. Blockers and dependencies

PRD-25-VS-03: Current hosted required-ci, final acceptance review and target-branch integration are pending; local source/delivery checks passed. (owner: delivery-integration-owner)
PRD-25-VS-03-T-03: Current hosted required-ci, final acceptance review and target-branch integration are pending; local source/delivery checks passed. (owner: delivery-integration-owner)
The ledger foundation in draft PR #96 is a prerequisite for tracking integration; current full CI/release and review remain required.

## 5. Next highest-priority deliverable

Validate the restored workflow and role boundaries, then integrate this focused slice through its own review.

## 6. PRD progress changes

PRD-01 and PRD-02 remain open. Existing API, persistence, Board/Card editing and retry behavior are reused.

## 7. Completion evidence

Baseline component probe: 3 failures / 14 passes; valid Board/Card return and retained retry destination fail.
Baseline real Demo API/browser: private Card unavailable state has only Retry; generic Sign in entry is absent. Failed trace/screenshot retained.

## Recent verified completed increments

None recorded under the new acceptance gates.

## Preserved historical evidence

HIST-UX-94: 2800 component cases passed; 24 deferred cases; 24 production-runtime browser cases passed at recorded implementation. Not rerun or promoted to current acceptance. Source: docs/ux/delivery-report.md. Full UX PRD and current release acceptance remain unverified.
HIST-DEVELOPMENT-REFUSALS: Prefreeze fixture and decomposition failures retained and evaluated; final frozen 364-case suite passed without skips or relaxed assertions. Source: docs/delivery/evidence/failure-evaluation.md. Development diagnostics are not release or product acceptance.
