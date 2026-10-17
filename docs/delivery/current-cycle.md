# Incremental delivery progress

Baseline: `e90c6442ccc0b928a38db8793853ef03abe1bf3d`. Counts describe the ledger, not product completion percentages.

PRDs: 0/88 verified Done (80 feature + 8 UX); 12 architecture tickets tracked separately.
Work items: 0/1522 Done. Remaining canonical numbered acceptance criteria: 391; functional obligations: 951; child criteria: 2369. Unverified includes unknown and partially tested work; it does not assert code is absent.

| Kind | Total | Done |
| --- | ---: | ---: |
| Epic | 8 | 0 |
| PRD | 100 | 0 |
| Vertical Slice | 464 | 0 |
| Task | 472 | 0 |
| Test | 478 | 0 |

Implemented but unvalidated: 33. Blocked: 4. Known MVP-critical outstanding: 70; unclassified records: 1285. Weighted progress: not measured.

## 1. Work completed

Final Card slice is Review after full2835/0/24existing skips, UI7/native21/source372, build/types/lint and complete444-case browser/integration registries. No target integration or parentDone claim.
Current main-targeted Card candidate is draft99 at f474115e; required CI39060697234 is running. Prior main-targeted Board candidate98 and all earlier history remain intact.
Read live PRD-07 and confirm exact frozen-body equality; read UXA-01–08 and its backlog mapping. Existing List UI, server creation/rank, rename/reorder, original-key deduplication and passing Board workflow are preserved.

## 2. Implemented, awaiting validation

PRD-25-VS-03, PRD-25-VS-03-T-01, PRD-25-VS-03-T-01-TEST-01, PRD-25-VS-03-T-02, PRD-25-VS-03-T-02-TEST-01, PRD-25-VS-03-T-03, PRD-25-VS-03-T-03-TEST-01, PRD-01-VS-04, PRD-01-VS-04-T-01, PRD-01-VS-04-T-01-TEST-01, PRD-01-VS-04-T-01-TEST-02, PRD-25-VS-03-T-04, PRD-25-VS-03-T-04-TEST-01, PRD-04-VS-01-T-02, PRD-04-VS-01-T-02-TEST-01, PRD-04-VS-01-T-03, PRD-04-VS-01-T-03-TEST-01, PRD-04-VS-01-T-04, PRD-04-VS-01-T-04-TEST-01, PRD-04-VS-05, PRD-04-VS-05-T-01, PRD-04-VS-05-T-01-TEST-01, PRD-04-VS-05-T-02, PRD-04-VS-05-T-02-TEST-01, PRD-04-VS-05-T-02-TEST-02, PRD-08-VS-04, PRD-08-VS-04-T-01, PRD-08-VS-04-T-01-TEST-01, PRD-08-VS-04-T-01-TEST-02, PRD-08-VS-04-T-01-TEST-03, PRD-08-VS-04-T-02, PRD-08-VS-04-T-02-TEST-01, PRD-08-VS-04-T-02-TEST-02

## 3. Current work in progress

Audit only: current List form/POST does not visibly carry an original actor; server intentionally separates receipt namespaces by actor. A genuine two-account committed-response-loss probe will determine whether the browser command crosses that boundary. No defect claim before result.

## 4. Blockers and dependencies

PRD-25-VS-03: Current hosted required-ci, final acceptance review and target-branch integration are pending; local source/delivery checks passed. (owner: delivery-integration-owner)
PRD-25-VS-03-T-03: Current hosted required-ci, final acceptance review and target-branch integration are pending; local source/delivery checks passed. (owner: delivery-integration-owner)
PRD-01-VS-04: Current complete frontend and affected native regressions pass, including the repaired Board creation. Required current immutable release, acceptance review and origin/main integration remain pending; old failed evidence stays retained. (owner: delivery-integration-owner)
PRD-01-VS-04-T-01: Current complete frontend and affected native regressions pass, including the repaired Board creation. Required current immutable release, acceptance review and origin/main integration remain pending; old failed evidence stays retained. (owner: delivery-integration-owner)
Current99 main-targeted immutable release/review/integration remains pending; current98 older scope does not certify Card changes. Continue independent List evidence work.

## 5. Next highest-priority deliverable

Commit and execute the new List account counterfactual against compiled webccc424c8/API3bf5c2ba. Record exact test and runtime revisions, original key/body and canonical outcome. Create a bounded independent remediation slice only if the intended failure is reproduced.

## 6. PRD progress changes

Preserve all original PRD-07/VS-01 IDs/parents/history; begin the existing audit and add one traceable native Test child. No original implementation is discarded or marked absent.

## 7. Completion evidence

Existing apps/web/src/features/kanban/BoardScreen.tsx List entry/submit; ListRenameControl.tsx and ListPositionControls.tsx remain unchanged.
Existing WorkManagementEndpoints.cs CreateListAsync dispatch and ApiHostTests.Work_retry_keys_deduplicate_concurrent_creation_and_versioned_updates_without_disclosing_revoked_results establish same-actor deduplication and intentional independent actor namespaces.
work/cycle8-live-requirements.json; live source https://github.com/nickmailhot-lang/StrataAI2/issues/8; parent candidate sourcef474115e.

## Recent verified completed increments

None recorded under the new acceptance gates.

## Preserved historical evidence

HIST-UX-94: 2800 component cases passed; 24 deferred cases; 24 production-runtime browser cases passed at recorded implementation. Not rerun or promoted to current acceptance. Source: docs/ux/delivery-report.md. Full UX PRD and current release acceptance remain unverified.
HIST-DEVELOPMENT-REFUSALS: Prefreeze fixture and decomposition failures retained and evaluated; final frozen 364-case suite passed without skips or relaxed assertions. Source: docs/delivery/evidence/failure-evaluation.md. Development diagnostics are not release or product acceptance.
HIST-CYCLE-02-PREFREEZE: Initial probes: 3 failed / 14 passed return components; private Card sign-in entry absent in browser. A test-only TypeScript matcher option was corrected. Original/current broad Board creation failures retained. Source: docs/delivery/evidence/cycle-02-failure-evaluation.md. Historical diagnostics and counterfactual results do not establish current full-workflow acceptance.
