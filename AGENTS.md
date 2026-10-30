# StrataAI2 delivery rules

## Current priority: polish the working MVP

The 2026-10-20 user-approved UX-first milestone takes priority over expanding visible product scope. Follow `docs/ux/polish-milestone.md`: polish existing priority workflows before starting net-new user-facing features. Continue necessary security/reliability fixes, tests and independent backend work that supports this milestone. Preserve the approved backlog and all implementation; do not restart, duplicate or silently drop work. Keep functional, UX, test and MVP acceptance separate.


Continue the approved PRD/ARCH backlog and operational task-management MVP. GitHub requirements and existing architecture contracts remain authoritative.

For every new or active feature, read `docs/ux/feature-acceptance.md` and its row in `docs/ux/backlog-map.md`. Include the applicable UX acceptance criteria in implementation and PR evidence. New PRDs must include the authoring checklist there.

Reuse current MUI/appTheme, shared shells and stable routes. Do not create standalone navigation, a second design system or broad frontend rewrite. Preserve schemas, API/event contracts, tenant/Portal boundaries, original-command recovery and lifecycle exceptions.

Use isolated branches/worktrees. Assign one integration owner to shared App/InternalAppShell/PortalShell/navigationModel/appTheme/designTokens and shared component files; feature work in independent modules continues. Do not modify another checkout's uncommitted work. Any disruptive UX migration needs a reversible flag and tested rollback.

Record implemented, tested and accepted separately in `docs/ux/delivery-report.md`. No combined product-completion percentage. Run targeted checks during development and the impact-selected `required-ci` gate before merge under `docs/architecture/feature-ci.md`. Full qualification remains mandatory for release/MVP acceptance and risk-selected changes. A failed check must be retained and evaluated; never skip, relax or bypass it to close a PRD. A current draft PR with pending evidence is not accepted MVP functionality. Avoid redundant dependency installation, rebuilds, logs and scratch artifacts.

The BoardWindow capacity phase preserves a fixed 24-case functional counterpart in `BoardWindow.test.tsx` and 24 deferred large cases. Put unrelated new UX/accessibility regressions in dedicated test files; do not widen an expected count merely to accommodate a new test.

Production browser scenarios that wait for a new Board's delivery must own that Organization's existing `scopedBoardWorker` fixture and restore it in `finally`. A local external scope controller does not prove the immutable CI fixture is configured. Preserve the original delivery predicate, bounded authority, roles and deadlines; do not enable global discovery or relax assertions to make setup pass.

## Incremental delivery contract

Use `docs/delivery/progress.json` as the machine-readable work-item ledger and `docs/delivery/README.md` for the operating procedure. Retain existing PRD/ARCH/UX/FR/AC/TC identifiers and historical records. Every new work item has a unique ID and one parent: Epic → PRD → Vertical Slice → Task → Test. Link tests to specific acceptance IDs. Do not restart implementation because an audit has not yet verified it.

Before starting a feature cycle, reconcile the selected slice's source/test evidence, read its UXA and backlog mapping, and add only the actual missing work. A Task must fit one focused cycle; split larger or uncertain implementation work before starting it. Proposed slice boundaries and audit tasks are not implementation estimates. Demonstrate authorized entry → primary operation → canonical persisted result → return, including failure and access-withdrawal behavior. Technical-only slices must demonstrate their real consumer/system result.

Use Ready → In Progress → Implemented → Testing → Review → Done. Use `node scripts/delivery-progress.mjs transition ID "STATE" "REASON"` to append lifecycle history. Blocked records require a reason, responsible owner and prior resume state; dependencies alone do not prove a block. Rework from Testing/Review returns to In Progress with the failure reason. Preserve failed/skipped/blocked/not-run results separately from passing results. Add exact source revision, implementation paths and hashed result artifacts before claiming Implemented/Tested. Never rewrite prior history or evidence.

Done requires every required child Done, verified acceptance, applicable passing tests, security and role checks, no known critical regressions, UX validation (or a reasoned system-only applicability record), review, build/demo evidence, target-branch integration and updated tracking. The ledger validator checks actual Git ancestry for Done. Do not close a parent PRD or use a closing issue keyword in a slice PR unless its full original obligations pass. A merged PR alone is not full feature acceptance.

Run `npm run delivery:validate` and `npm run delivery:test` with applicable existing gates; CI runs the same checks and preserves history against the PR base. The delivery check adds to ARCH-11. The user-approved 2026-10-24 split in `docs/architecture/feature-ci.md` governs when source, RLS, browser, security and immutable-release gates run; never claim deferred full checks passed from a feature-only run. Keep incomplete evidence visible in a draft PR. Continue independent slices without waiting for the parent PRD.

After every cycle, append a `cycles` record and generate `npm run delivery:report` (or append `-- --json`). Report completed increments, implementation awaiting validation, work in progress, blockers/dependencies, next deliverable, PRD changes and evidence. Count each hierarchy kind separately. Report unclassified MVP scope and unverified acceptance explicitly. No percentages without a frozen denominator; weighted progress requires recorded estimates and a frozen weighted baseline.

## Local-first development and agent coordination

Read `docs/development/local-first.md` and `docs/development/multi-agent.md`.
Install repository hooks with `npm run dev:hooks`; preserve existing custom hooks.
Use `npm run verify:local -- --base origin/main --mode fast` while iterating and
run final `npm run verify:local -- --base origin/main` before committing. Only a
final pass on the exact staged/working tree authorizes commit and feature-branch
push. New edits, partial staging or a changed base invalidate evidence. Never use
`--no-verify`, force-push, alter evidence or weaken a check to obtain a pass.
Local feature evidence does not replace impact-selected required-ci, full-sanity,
review, product acceptance or exact-revision release qualification.

Claim one task/branch/worktree and nonoverlapping paths through `dev:agents`.
One integration owner controls shared contracts, shells/theme, manifests/locks,
migrations, CI and canonical delivery records. Retain claims through reviewed
handoff; never touch another checkout. Integrate one candidate at a time in a
dedicated worktree, test the combined tree before its merge commit, then push a
reviewable feature branch. The canonical fixed-port runtime slot is exclusive
across worktrees; use unique projects/volumes/tags and never clean unrelated Docker
resources. Reusable role prompts are under `docs/prompts/`.

GitHub connector/API publication does not execute local Git hooks. Before such
writes, explicitly validate the final local receipt and exact staged/working tree;
publish only that tree and intended parent using a new branch or expected-head
update. Fetch and compare the published tree afterward. Preserve the original
local commit if connector commit metadata differs; never substitute untested source.
