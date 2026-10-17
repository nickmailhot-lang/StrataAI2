# UX Foundation workstream

[Single consolidated delivery report](delivery-report.md) · [80-PRD dependency/discovery audit](backlog-map.md) · [Feature acceptance criteria](feature-acceptance.md)

This workstream evolves the existing React/TypeScript/Vite/MUI application. It preserves the approved backlog, operational task-management MVP, canonical Organization → Board → List → Card model, stable APIs/schemas and tenant boundaries. The eight PRDs add delivery detail to canonical feature owners; their specification does not claim implementation or acceptance.

## PRDs

- [UX-001: Application Shell and Responsive Navigation](UX-001.md)
- [UX-002: Information Architecture and Role-Based Menus](UX-002.md)
- [UX-003: Shared Design System and Component Library](UX-003.md)
- [UX-004: Actionable Dashboard and Personalized Work Queues](UX-004.md)
- [UX-005: Standardized Forms, Tables, Dialogs, and Feedback](UX-005.md)
- [UX-006: Mobile Responsiveness and Accessibility](UX-006.md)
- [UX-007: Workflow Discoverability and Contextual Guidance](UX-007.md)
- [UX-008: UX Regression Testing and Visual Validation](UX-008.md)

## Current implementation slices

- UX-002: [shared navigation contract](navigation-contract.md), explicit unavailable modules and current Organization Owner/Admin/Member discovery, using the page's existing admitted summary and live withdrawal. No new permission reads or subscriptions.
- UX-003: [design system catalog](design-system.md), named existing tokens, shared PageHeader/ActionRow in Organization discovery/Search/Notifications, wrapping accessible alerts and header offsets that follow text reflow.
- These are implemented slices, not full PRD/product acceptance. See the delivery report for current checks, retained failures and remaining gates. Independent feature/backend work continues outside these shared UI files.

## Current architecture and findings

[Existing route/discovery inventory and protected recovery exceptions](route-inventory.md) records the current global, Organization, Board/Card and Portal route families.

`apps/web/src/app/App.tsx` owns React Router composition. `InternalAppShell.tsx` wraps Organization-scoped routes in `SurfaceAdmission`, renders the fixed MUI AppBar/permanent drawer and protected Outlet, and runs NavigationConfirmation. Before this slice, phones received shortcut icons and no complete drawer; navigation had no selected/current-section state. Desktop navigation includes disabled future Meetings, Correspondence, Documents and Owners & Units entries. They must remain unavailable until real destinations and current capabilities exist.

`/app`, global profile/invitations/lifecycle, authentication and independently admitted deletion/departure/operational-lifecycle routes live outside the shell. Do not move these blindly under ordinary surface admission: unresolved original receipts and terminal lifecycle recovery can outlive normal membership. The denied public Board and Organization-home fallbacks also authorize themselves.

`PortalShell.tsx` owns a separately protected PORTAL surface; it currently exposes an unavailable document-browser placeholder. `BoardScreen` owns canvas/Card overlay routes. Existing feature-specific action rows are inside their routed consumers. `appTheme.ts` is the existing shared design system, including contrast/focus/autofill safeguards. Reuse it and MUI/Emotion/dnd-kit; retain production CSP and shared HTTP Problem handling.

The new mobile drawer shares the existing navigation definitions and remains inside current surface admission. This slice changes no route, API, schema or server permission. An empty Board canvas ARIA naming defect surfaced by Axe is repaired with a semantic region.

## Smallest safe implementation sequence

1. **UX-001 slice 1 (this branch):** additive mobile drawer/current section/skip focus and minimal semantic accessibility fix. Keep existing route composition and business actions. Validate shell/component/controlled browser checks, then real-service regression before integration.
2. **UX-002 route/capability contract:** review current global/Organization/Board/Portal entry points and recovery exceptions. Approve role menus from existing authoritative capabilities. Do not block independent API/Worker/persistence work.
3. **UX-003 reuse baseline:** document appTheme/tokens and current working primitives. Establish representative fixtures; extract only proven duplicated compositions. UX-002 and UX-003 design work may proceed independently, but shared-file edits integrate sequentially.
4. **UX-001 next slices:** integrate approved context labels, breadcrumbs and global account-page composition one route family at a time. A migration flag is required if relocating a working workflow could interrupt discovery, state or recovery. Specify default, both-path checks, rollback and removal criteria before rollout.
5. **UX-005/006 with active features:** migrate one form/directory/dialog/workflow per small PR; fix operational MVP responsive/accessibility blockers first. UX-007 discovery/empty-state guidance follows those same journeys.
6. **UX-004:** contribute presentation and actionability to PRD-54/55 when authorized source records are ready. Do not introduce a second dashboard or advance deferred governance/AI scope ahead of current operational MVP acceptance.
7. **UX-008 throughout:** add targeted interaction/Axe coverage now; approve deterministic visual fixtures and run broader affected/regression/release checks before each integration milestone.

## Continuous delivery and file ownership

Use `ux/ux-001-foundation` in this isolated worktree. The existing main checkout and its active CI/browser changes remain untouched. Shell owner has exclusive write ownership of `app/App.tsx`, `app/InternalAppShell.tsx`, `portal/PortalShell.tsx`, `app/navigationModel.ts`, `theme/appTheme.ts`, `theme/designTokens.ts` and shared `components/PageHeader.tsx` / `ActionRow.tsx` for an integration window. Feature owners change their own modules/tests and consume approved slots; request a sequential shell PR for a new menu item. This is an ownership rule, not authorization to start agents or send messages.

Independent backend/schema/Worker/security repairs, existing feature modules without shared-layout edits and feature contract tests continue. A UI feature may implement against approved slots while waiting for menu integration; it cannot ship standalone navigation. Rebase/cherry-pick only reviewed changes, preserve other active work, and run the required checks on the combined revision. Keep source changes reviewable in a draft PR until applicable gates pass.

This small additive change needs no runtime migration flag. Reverting its commit restores the previous shell without touching persisted data. Route consolidation, role-policy changes or wider token migration must separately assess flag requirements. Do not merge while current gate failures or missing evidence remain.
