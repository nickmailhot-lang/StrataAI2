# Incremental delivery progress

Baseline: `e90c6442ccc0b928a38db8793853ef03abe1bf3d`. Counts describe the ledger, not product completion percentages.

PRDs: 0/88 verified Done (80 feature + 8 UX); 12 architecture tickets tracked separately.
Work items: 0/1521 Done. Remaining canonical numbered acceptance criteria: 391; functional obligations: 951; child criteria: 2368. Unverified includes unknown and partially tested work; it does not assert code is absent.

| Kind | Total | Done |
| --- | ---: | ---: |
| Epic | 8 | 0 |
| PRD | 100 | 0 |
| Vertical Slice | 464 | 0 |
| Task | 472 | 0 |
| Test | 477 | 0 |

Implemented but unvalidated: 33. Blocked: 4. Known MVP-critical outstanding: 66; unclassified records: 1288. Weighted progress: not measured.

## 1. Work completed

Preserved the complete preceding3bf5c2ba frontend pass:2834/0/24 existing deferred skips,1002.41s. Original-account core task/tests are Review; no parentDone.
Reproduced all three UX gaps against the compiled app, retaining exact geometry, native3fail and component5pass/1fail.
Implemented Card-specific error focus/centering when the original operation still owns focus, preserving deliberate independent navigation. Persist200% root text through navigations/reload in the native fixture and assert unobscured canonical return. Focused UI7/7 passes at ccc424c8d3cef6407268f428c6af87e7c5c45531.
Current native21/21 passes with all new viewport/focus/persistent-zoom assertions. Mobile refusal is now y72/header56, focused and fully visible; both original-key account probes preserve one canonical Card.
Inspected all six actual current viewport captures. Persistent200% tablet reload/return and every focused Card are unobscured; no shell change was needed. Builds/types/lint and focused UI7 pass; unchanged API16 remains explicitly bound to its prior3bf5c2ba source.

## 2. Implemented, awaiting validation

PRD-25-VS-03, PRD-25-VS-03-T-01, PRD-25-VS-03-T-01-TEST-01, PRD-25-VS-03-T-02, PRD-25-VS-03-T-02-TEST-01, PRD-25-VS-03-T-03, PRD-25-VS-03-T-03-TEST-01, PRD-01-VS-04, PRD-01-VS-04-T-01, PRD-01-VS-04-T-01-TEST-01, PRD-01-VS-04-T-01-TEST-02, PRD-25-VS-03-T-04, PRD-25-VS-03-T-04-TEST-01, PRD-04-VS-01-T-02, PRD-04-VS-01-T-02-TEST-01, PRD-04-VS-01-T-03, PRD-04-VS-01-T-03-TEST-01, PRD-04-VS-01-T-04, PRD-04-VS-01-T-04-TEST-01, PRD-04-VS-05, PRD-04-VS-05-T-01, PRD-04-VS-05-T-01-TEST-01, PRD-04-VS-05-T-02, PRD-04-VS-05-T-02-TEST-01, PRD-04-VS-05-T-02-TEST-02, PRD-08-VS-04, PRD-08-VS-04-T-01, PRD-08-VS-04-T-01-TEST-01, PRD-08-VS-04-T-01-TEST-02, PRD-08-VS-04-T-01-TEST-03, PRD-08-VS-04-T-02, PRD-08-VS-04-T-02-TEST-01, PRD-08-VS-04-T-02-TEST-02

## 3. Current work in progress

Complete frontend runs alone at frozenccc424c8, session12590; no app/backend/browser edits or competing broad validation. All scoped Card criteria are verified, but the slice and TaskT02 remainTesting until current full/release/review/integration gates pass.
Draft99 remains the independent Card candidate; main-targeted #98 requiredCI39055149611 has passed source/build/security and is executing immutable container workflows. Older ledger-only CI39044307940 completed successfully at16bf945d and remains historical, not current feature evidence.

## 4. Blockers and dependencies

PRD-25-VS-03: Current hosted required-ci, final acceptance review and target-branch integration are pending; local source/delivery checks passed. (owner: delivery-integration-owner)
PRD-25-VS-03-T-03: Current hosted required-ci, final acceptance review and target-branch integration are pending; local source/delivery checks passed. (owner: delivery-integration-owner)
PRD-01-VS-04: Current complete frontend and affected native regressions pass, including the repaired Board creation. Required current immutable release, acceptance review and origin/main integration remain pending; old failed evidence stays retained. (owner: delivery-integration-owner)
PRD-01-VS-04-T-01: Current complete frontend and affected native regressions pass, including the repaired Board creation. Required current immutable release, acceptance review and origin/main integration remain pending; old failed evidence stays retained. (owner: delivery-integration-owner)
Required current main-targeted releaseCI, final review and target integration remain pending. Do not weaken or bypass them.

## 5. Next highest-priority deliverable

Read current full frontend session12590 and preserve exact counts/outcome. If it passes, run final source/delivery/browser/integration registry checks, commit/push final evidence and update draft99. Continue the next ready core MVP audit (List creation original-account binding) while current required integration work proceeds.

## 6. PRD progress changes

Required TaskT-02 and its two tests are Implemented→Testing. The original identity work, failed evidence and scoped criteria are preserved. AC-04/05 and complete slice remain unverified pending current UX/release evidence.

## 7. Completion evidence

EVID-CYCLE-07-NATIVE-BEFORE:3/3 fail; COMPONENT-BEFORE:5pass/1fail; BEFORE-GEOMETRY:both focusBODY, mobilealerty8/header56.
EVID-CYCLE-07-COMPONENT-AFTER:7/7 pass at ccc424c8d3cef6407268f428c6af87e7c5c45531; no assertion/deadline/retry relaxation.
EVID-CYCLE-07-NATIVE-21, VIEWPORT-ACTOR-PROOF and VISUAL: current sourceccc424c8; explicit compiled API3bf5c2ba/backend equality; no new server-pass claim or UX waiver.

## Recent verified completed increments

None recorded under the new acceptance gates.

## Preserved historical evidence

HIST-UX-94: 2800 component cases passed; 24 deferred cases; 24 production-runtime browser cases passed at recorded implementation. Not rerun or promoted to current acceptance. Source: docs/ux/delivery-report.md. Full UX PRD and current release acceptance remain unverified.
HIST-DEVELOPMENT-REFUSALS: Prefreeze fixture and decomposition failures retained and evaluated; final frozen 364-case suite passed without skips or relaxed assertions. Source: docs/delivery/evidence/failure-evaluation.md. Development diagnostics are not release or product acceptance.
HIST-CYCLE-02-PREFREEZE: Initial probes: 3 failed / 14 passed return components; private Card sign-in entry absent in browser. A test-only TypeScript matcher option was corrected. Original/current broad Board creation failures retained. Source: docs/delivery/evidence/cycle-02-failure-evaluation.md. Historical diagnostics and counterfactual results do not establish current full-workflow acceptance.
