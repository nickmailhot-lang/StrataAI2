# StrataAI2 delivery rules

Continue the approved PRD/ARCH backlog and operational task-management MVP. GitHub requirements and existing architecture contracts remain authoritative.

For every new or active feature, read `docs/ux/feature-acceptance.md` and its row in `docs/ux/backlog-map.md`. Include the applicable UX acceptance criteria in implementation and PR evidence. New PRDs must include the authoring checklist there.

Reuse current MUI/appTheme, shared shells and stable routes. Do not create standalone navigation, a second design system or broad frontend rewrite. Preserve schemas, API/event contracts, tenant/Portal boundaries, original-command recovery and lifecycle exceptions.

Use isolated branches/worktrees. Assign one integration owner to shared App/InternalAppShell/PortalShell/navigationModel/appTheme/designTokens and shared component files; feature work in independent modules continues. Do not modify another checkout's uncommitted work. Any disruptive UX migration needs a reversible flag and tested rollback.

Record implemented, tested and accepted separately in `docs/ux/delivery-report.md`. No combined product-completion percentage. Run targeted checks during development and all applicable required gates before merge. A failed check must be retained and evaluated; never skip, relax or bypass it to close a PRD. A current draft PR with pending evidence is not accepted MVP functionality. Avoid redundant dependency installation, rebuilds, logs and scratch artifacts.

The BoardWindow capacity phase preserves a fixed 24-case functional counterpart in `BoardWindow.test.tsx` and 24 deferred large cases. Put unrelated new UX/accessibility regressions in dedicated test files; do not widen an expected count merely to accommodate a new test.

Production browser scenarios that wait for a new Board's delivery must own that Organization's existing `scopedBoardWorker` fixture and restore it in `finally`. A local external scope controller does not prove the immutable CI fixture is configured. Preserve the original delivery predicate, bounded authority, roles and deadlines; do not enable global discovery or relax assertions to make setup pass.

## Incremental delivery contract

Use `docs/delivery/progress.json` as the machine-readable work-item ledger and `docs/delivery/README.md` for the operating procedure. Retain existing PRD/ARCH/UX/FR/AC/TC identifiers and historical records. Every new work item has a unique ID and one parent: Epic → PRD → Vertical Slice → Task → Test. Link tests to specific acceptance IDs. Do not restart implementation because an audit has not yet verified it.

Before starting a feature cycle, reconcile the selected slice's source/test evidence, read its UXA and backlog mapping, and add only the actual missing work. A Task must fit one focused cycle; split larger or uncertain implementation work before starting it. Proposed slice boundaries and audit tasks are not implementation estimates. Demonstrate authorized entry → primary operation → canonical persisted result → return, including failure and access-withdrawal behavior. Technical-only slices must demonstrate their real consumer/system result.

Use Ready → In Progress → Implemented → Testing → Review → Done. Use `node scripts/delivery-progress.mjs transition ID "STATE" "REASON"` to append lifecycle history. Blocked records require a reason, responsible owner and prior resume state; dependencies alone do not prove a block. Rework from Testing/Review returns to In Progress with the failure reason. Preserve failed/skipped/blocked/not-run results separately from passing results. Add exact source revision, implementation paths and hashed result artifacts before claiming Implemented/Tested. Never rewrite prior history or evidence.

Done requires every required child Done, verified acceptance, applicable passing tests, security and role checks, no known critical regressions, UX validation (or a reasoned system-only applicability record), review, build/demo evidence, target-branch integration and updated tracking. The ledger validator checks actual Git ancestry for Done. Do not close a parent PRD or use a closing issue keyword in a slice PR unless its full original obligations pass. A merged PR alone is not full feature acceptance.

Run `npm run delivery:validate` and `npm run delivery:test` with applicable existing gates; CI runs the same checks and preserves history against the PR base. The delivery check adds to ARCH-11; it does not replace source, RLS, browser, security or immutable-release gates. Keep incomplete evidence visible in a draft PR. Continue independent slices without waiting for the parent PRD.

After every cycle, append a `cycles` record and generate `npm run delivery:report` (or append `-- --json`). Report completed increments, implementation awaiting validation, work in progress, blockers/dependencies, next deliverable, PRD changes and evidence. Count each hierarchy kind separately. Report unclassified MVP scope and unverified acceptance explicitly. No percentages without a frozen denominator; weighted progress requires recorded estimates and a frozen weighted baseline.
