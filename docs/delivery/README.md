# Incremental delivery operating procedure

The current work-item ledger is [progress.json](progress.json). The [baseline audit and capability breakdown](baseline-audit.md) covers 80 canonical feature PRDs, 12 architecture tickets and 8 local UX specifications. Requirements remain authoritative in GitHub and the UX Foundation; [requirements-snapshot.json](requirements-snapshot.json) freezes their inspected definitions, states, timestamps and source bodies. [baseline-audit.json](baseline-audit.json) records candidate code, test and historical document references at baseline `e90c6442`.

This initial audit establishes scope and traceability across the entire inventory. It does not certify every existing implementation. Explicit identifier matches cannot establish semantic completeness, and missing matches cannot establish missing code. Completed historical implementation is retained; full acceptance reconciliation is a small, tracked task under each proposed capability. The original issue #4 remains a duplicate of PRD-03; it is not an extra PRD. ARCH-01's closed GitHub state remains separate from verified Done under this ledger.

## Hierarchy and sizing

Use Epic → PRD → Vertical Slice → Task → Test. Preserve PRD/ARCH/UX IDs. New child IDs use `PRD-25-VS-03-T-02-TEST-01`; the single parent is `parentId`. Acceptance keys are namespaced by owner because original FR prefixes are sometimes reused across PRDs. Tests use `verifies` to link exact acceptance keys, with original test IDs retained as aliases rather than duplicated identities.

The proposed business boundaries group capabilities such as sign-in/session expiry, Card movement with persistence and convergence, or exact-version Minutes review. These are neither estimates nor instructions to rebuild existing work. Architecture slices must demonstrate the real consumer/system result; an endpoint or migration alone does not satisfy the exit criterion. Each existing slice starts with one bounded evidence audit. Its result identifies what already works, what remains unverified, and the actual missing implementation. Add only those gap tasks, each at most one focused development cycle. Split a task whenever uncertainty or size exceeds that limit. Keep a demonstrable build at every integration milestone.

PRD dependencies are preserved as contracts, including the original dependency cycles. They do not require every parent PRD to close before delivering a slice. Record the precise prerequisite capability or external condition when it actually blocks work. Do not equate an open dependency with an automatic blocker.

## Lifecycle and evidence

Use Ready → In Progress → Implemented → Testing → Review → Done. Record Blocked with reason, responsible owner and exact prior resume state. Testing/Review may return to In Progress with a retained failure and rework reason. Done may reopen with a reason. Each transition appends history:

```text
npm run delivery:validate
npm run delivery:report
npm run delivery:report -- --json
node scripts/delivery-progress.mjs transition PRD-25-VS-03 "Blocked" "Current candidate CI evidence unavailable"
```

Evidence records include exact source revision, method/command, timestamp, result, repository artifact path and SHA-256. Verified criteria also need evidence IDs, reviewer and date. Implemented records need source revision and paths. Do not promote a historical pass, skipped/deferred test, unavailable artifact or source match into a current acceptance result. Retain failures and explain their scope, cause, impact, owner, correction and rerun.

Before Done, all required children and acceptance must pass; tests, security/roles, regressions, build/demo, UX, review and tracking must have applicable evidence. The validator verifies that the implementation commit is an ancestor of the recorded integration commit and that the integration commit is in the target branch. Fetch the target branch before asserting Done. Non-applicability needs a concrete consumer-specific reason and cannot waive tests, acceptance, regressions, integration, tracking or review. Validation checks the recorded evidence and Git graph; reviewers remain responsible for truth of the evidence, security conclusions and product acceptance.

Use [UXA-01–08](../ux/feature-acceptance.md) and the owning [backlog row](../ux/backlog-map.md). For each feature validate discoverability, navigation/return, 320/390/768/1280px workflows, accessibility, role loss, errors and canonical persistence. Keep UI/tenant/Portal boundaries and existing API/event/schema/recovery contracts. The current reporting slice has repository consumers and no browser or product UI change; its applicability reason is stored on the slice. It does not grant any application permission.

## Progress calculations

`npm run delivery:report` computes all totals from ledger records. It separates 80 feature + 8 UX PRDs from 12 architecture tickets and shows each hierarchy kind. Test/audit records are work items, not delivered business capabilities. Canonical numbered ACs, original FR obligations and child criteria are separate denominators; do not add duplicated child criteria into a product-completion percentage. Proposed boundaries and gap decomposition change counts, so these totals are a planning snapshot, not a frozen effort baseline.

Zero newly verified Done means zero certified under these gates; it does not erase existing implementation. Historical results and merged increments remain visible separately. Remaining acceptance means unverified, which includes unknown, partial, failed or historical-only evidence; it does not mean entirely unimplemented.

MVP classification is deliberately tri-state. Known critical slices have a recorded release-scope basis; deferred governance/AI work is not silently made critical by PRD-26's broad roadmap dependencies. Unclassified work is reported explicitly, so a known-critical count is a lower bound until classification is complete. Source: [operational task-management milestone](../architecture/operational-mvp-milestone.md). Preserve existing deferred capacity tests and all normal functional/release gates.

No completion percentage is generated. Estimates are currently absent. Weighted progress remains unmeasured until a reviewed scope baseline and all required estimates exist; counts cannot substitute for effort weights.

## Development cycle and integration

1. Choose the highest-priority incomplete operational-MVP capability, reconcile its evidence and define one observable outcome.
2. Add focused gap tasks and tests, reuse current architecture and UX, implement and run applicable checks.
3. Record failures, exact source/evidence identity and acceptance independently. Update each slice when it earns its state; do not wait for its parent PRD.
4. Append a cycle record with completed work, implementation awaiting validation, WIP, blockers/dependencies, next deliverable, PRD changes and evidence. Generate the report from the ledger.
5. Keep a draft PR while required evidence is incomplete. Existing ARCH-11 source/container/security/RLS/browser gates still apply. After successful review, integration and acceptance, record Done. Closing keywords must never close a parent from a partial slice.

CI validates structure and baseline completeness, executes positive/refusal contracts and rejects historical record loss against the pull request base. These controls add to the existing required metadata job. A future intentional requirements change needs an explicit reviewed baseline migration retaining prior definitions/history; do not rerun the initial import over the current ledger. Read the live requirements before starting a slice and record subsequent changes additively. This frozen snapshot is not an automatic ongoing GitHub synchronizer or an approved product-scope reduction.

## Initial cycle

`PRD-25-VS-03` is the first system increment: inspect the retained backlog → calculate progress → refuse unsupported Done → retain evidence and cycle report. Its three one-cycle tasks cover the baseline, validator/report and CI/agent enforcement. It adds no product route, API, database migration or runtime service. This delivers a useful development-system capability while the next product slice is selected from an actual demonstrated operational-MVP gap.
