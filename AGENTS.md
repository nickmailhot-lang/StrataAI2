# StrataAI2 delivery rules

Continue the approved PRD/ARCH backlog and operational task-management MVP. GitHub requirements and existing architecture contracts remain authoritative.

For every new or active feature, read `docs/ux/feature-acceptance.md` and its row in `docs/ux/backlog-map.md`. Include the applicable UX acceptance criteria in implementation and PR evidence. New PRDs must include the authoring checklist there.

Reuse current MUI/appTheme, shared shells and stable routes. Do not create standalone navigation, a second design system or broad frontend rewrite. Preserve schemas, API/event contracts, tenant/Portal boundaries, original-command recovery and lifecycle exceptions.

Use isolated branches/worktrees. Assign one integration owner to shared App/InternalAppShell/PortalShell/navigationModel/appTheme/designTokens and shared component files; feature work in independent modules continues. Do not modify another checkout's uncommitted work. Any disruptive UX migration needs a reversible flag and tested rollback.

Record implemented, tested and accepted separately in `docs/ux/delivery-report.md`. No combined product-completion percentage. Run targeted checks during development and all applicable required gates before merge. A failed check must be retained and evaluated; never skip, relax or bypass it to close a PRD. A current draft PR with pending evidence is not accepted MVP functionality. Avoid redundant dependency installation, rebuilds, logs and scratch artifacts.

The BoardWindow capacity phase preserves a fixed 24-case functional counterpart in `BoardWindow.test.tsx` and 24 deferred large cases. Put unrelated new UX/accessibility regressions in dedicated test files; do not widen an expected count merely to accommodate a new test.
