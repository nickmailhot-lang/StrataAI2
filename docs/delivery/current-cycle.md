# Incremental delivery progress

Baseline: `e90c6442ccc0b928a38db8793853ef03abe1bf3d`. Counts describe the ledger, not product completion percentages.

PRDs: 0/88 verified Done (80 feature + 8 UX); 12 architecture tickets tracked separately.
Work items: 0/1521 Done. Remaining canonical numbered acceptance criteria: 391; functional obligations: 951; child criteria: 2373. Unverified includes unknown and partially tested work; it does not assert code is absent.

| Kind | Total | Done |
| --- | ---: | ---: |
| Epic | 8 | 0 |
| PRD | 100 | 0 |
| Vertical Slice | 464 | 0 |
| Task | 472 | 0 |
| Test | 477 | 0 |

Implemented but unvalidated: 30. Blocked: 4. Known MVP-critical outstanding: 66; unclassified records: 1288. Weighted progress: not measured.

## 1. Work completed

Previous independent Board recovery candidate aa64e0a2 is pushed in main-targeted draft #98; current required CI run 39055149611 is in progress. Local source d35f775e passes complete frontend 2828/0/24 existing deferred skips, API10/components139/native15 and source contracts372.
Read live PRD-08 and confirm its body matches the frozen source. Existing Card creation/title/rank/rename implementation and Board workflow are retained. Read UXA-01–08 and PRD-08 mapping.
Both valid Card counterfactuals fail at the intended refusal assertion: [201,201], same key/body, different created IDs and two canonical Cards. Initial fixture wording failures are retained separately.
Implemented focused Card original-account entry/retry guard at 9bfc8a1b951b571f9aa70640f0b6e27656fde15e. New server cases fail 5/6 before the guard (legacy passes); all six new component cases fail before the UI repair. Intermediate missing entry-error feedback is fixed without weakening assertions.
Final compiled source 3bf5c2ba: 16 API cases and 21 native workflows pass, zero failures/skips/retries; both genuine Card account probes return [201,401], same key/body and one original canonical Card. Builds/browser types pass; prior Git-identical daf028e2 passes all145 affected components plus web types/lint.
Inspected all10 actual Card screenshots. Manual review identified bounded missing zoom/feedback acceptance instead of promoting automated passes to full UX acceptance. Prior native19/2, typing and Windows file-lock failures are retained separately.
Complete frozen3bf5c2ba frontend passes2,834 tests /0 failures /24 existing deferred skips,192 files, exit0 in1002.41s. Original-account identity task/tests advance to Review with verified scoped criteria; parent Card slice and fullUX acceptance remain open.

## 2. Implemented, awaiting validation

PRD-25-VS-03, PRD-25-VS-03-T-01, PRD-25-VS-03-T-01-TEST-01, PRD-25-VS-03-T-02, PRD-25-VS-03-T-02-TEST-01, PRD-25-VS-03-T-03, PRD-25-VS-03-T-03-TEST-01, PRD-01-VS-04, PRD-01-VS-04-T-01, PRD-01-VS-04-T-01-TEST-01, PRD-01-VS-04-T-01-TEST-02, PRD-25-VS-03-T-04, PRD-25-VS-03-T-04-TEST-01, PRD-04-VS-01-T-02, PRD-04-VS-01-T-02-TEST-01, PRD-04-VS-01-T-03, PRD-04-VS-01-T-03-TEST-01, PRD-04-VS-01-T-04, PRD-04-VS-01-T-04-TEST-01, PRD-04-VS-05, PRD-04-VS-05-T-01, PRD-04-VS-05-T-01-TEST-01, PRD-04-VS-05-T-02, PRD-04-VS-05-T-02-TEST-01, PRD-04-VS-05-T-02-TEST-02, PRD-08-VS-04, PRD-08-VS-04-T-01, PRD-08-VS-04-T-01-TEST-01, PRD-08-VS-04-T-01-TEST-02, PRD-08-VS-04-T-01-TEST-03

## 3. Current work in progress

Begin required T-02 after the frozen full pass: add failing viewport/focus and persisted200% reload assertions against the current compiled app before implementation.
Current #98 main-targeted releaseCI39055149611 remains pending; draft99 preserves independent Card work. No PRD/sliceDone.

## 4. Blockers and dependencies

PRD-25-VS-03: Current hosted required-ci, final acceptance review and target-branch integration are pending; local source/delivery checks passed. (owner: delivery-integration-owner)
PRD-25-VS-03-T-03: Current hosted required-ci, final acceptance review and target-branch integration are pending; local source/delivery checks passed. (owner: delivery-integration-owner)
PRD-01-VS-04: Current complete frontend and affected native regressions pass, including the repaired Board creation. Required current immutable release, acceptance review and origin/main integration remain pending; old failed evidence stays retained. (owner: delivery-integration-owner)
PRD-01-VS-04-T-01: Current complete frontend and affected native regressions pass, including the repaired Board creation. Required current immutable release, acceptance review and origin/main integration remain pending; old failed evidence stays retained. (owner: delivery-integration-owner)
Current main-targeted release CI, review and target integration remain pending for #98. Card command identity is under investigation, not yet accepted.

## 5. Next highest-priority deliverable

Reproduce Card refusal alert geometry/focus and the tablet reload text-size gap. Keep API source3bf5c2ba unchanged for this frontend-only follow-up; verify any later compiled web/runtime source mapping explicitly. Implement the smallest repair, run applicable red/green checks and inspect updated renders, then final source/full/release gates.

## 6. PRD progress changes

PRD-08-VS-01 audit begins; no original implementation is discarded and no parent PRD is accepted.
Create required PRD-08-VS-04 with four measurable criteria, one bounded task and three test scopes. PRD-08-VS-01 and all original source obligations stay intact.
Preserve all original four Card criteria; append AC-05 and required bounded T-02 with native/component children for the manual-review findings. No original requirement, history or implementation is discarded.

## 7. Completion evidence

tests/browser/card-creation-account.spec.ts at a0c60261; raw execution work/cycle6-card-before.txt; live requirement equality work/cycle6-live-requirements.json; current integration run https://github.com/nickmailhot-lang/StrataAI2/actions/runs/39055149611.
EVID-CYCLE-06-CARD-COUNTERFACTUAL and EVID-CYCLE-06-CARD-PROOF: both desktop/mobile genuine account replacement duplicate one original Card request.
Original and intermediate failures retained as EVID-CYCLE-06-API-BEFORE, COMPONENT-BEFORE and COMPONENT-FEEDBACK; no test skipped/relaxed.
Current final API16/native21 at3bf5c2ba; prior components145 at explicitly identified Git-equivalent daf028e2; exact-source identity, actor proof and10-image manual review. Complete frontend and UX follow-up pending.
EVID-CYCLE-06-FULL-FRONTEND:2,834/0/24 existing skips at3bf5c2ba,1002.41s.

## Recent verified completed increments

None recorded under the new acceptance gates.

## Preserved historical evidence

HIST-UX-94: 2800 component cases passed; 24 deferred cases; 24 production-runtime browser cases passed at recorded implementation. Not rerun or promoted to current acceptance. Source: docs/ux/delivery-report.md. Full UX PRD and current release acceptance remain unverified.
HIST-DEVELOPMENT-REFUSALS: Prefreeze fixture and decomposition failures retained and evaluated; final frozen 364-case suite passed without skips or relaxed assertions. Source: docs/delivery/evidence/failure-evaluation.md. Development diagnostics are not release or product acceptance.
HIST-CYCLE-02-PREFREEZE: Initial probes: 3 failed / 14 passed return components; private Card sign-in entry absent in browser. A test-only TypeScript matcher option was corrected. Original/current broad Board creation failures retained. Source: docs/delivery/evidence/cycle-02-failure-evaluation.md. Historical diagnostics and counterfactual results do not establish current full-workflow acceptance.
