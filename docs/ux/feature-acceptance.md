# UX acceptance addendum for all new and active feature PRDs

This is additive acceptance criteria for PRD-01 through PRD-80 and every new feature PRD. Existing GitHub issue bodies, FR/AC/test IDs, architecture and security rules stay authoritative. Resolve any apparent conflict before implementation; never relax existing consent, retry, lifecycle or authorization behavior to satisfy a visual change. The [backlog matrix](backlog-map.md) identifies each current feature's applicable UX dependencies, entry point, roles, remediation and extra usability tests.

## Required feature acceptance criteria

- UXA-01 — Identify the owning PRD and linked UX PRDs; use the current shared shell, MUI theme, page/action patterns and approved route slots. A new module cannot implement standalone navigation or a second page layout system.
- UXA-02 — Document and test entry → permitted primary task → canonical persisted outcome → return for every affected role. Include first-use/empty, loading, error, denied and stale-context behavior. Proposed future entry points must become working links only after route and capability implementation.
- UXA-03 — Match controls and discovery to current server-authorized capabilities. Recheck direct URLs/APIs, cross-Organization denial and Portal separation. Withdraw protected content/actions on role/scope/session loss; never infer access from a menu label or preserve stale names/counts.
- UXA-04 — Preserve existing schema/API/event contracts, deep links, Board/Card context, browser Back, draft recovery, explicit destructive consent, versions and idempotent original-command behavior.
- UXA-05 — Complete the affected workflow at 320/390/768/1280px, with supported text zoom/reflow, usable targets and no page overflow; intentional canvas/table scroll must remain local and discoverable.
- UXA-06 — Meet PRD-23 WCAG 2.2 AA requirements: names/labels, landmarks/headings, keyboard equivalence, visible unobscured focus, dialog focus/return, non-color status and appropriate announcements. Record automated Axe and manual review separately.
- UXA-07 — Standardize feedback using existing components: field-specific validation, safe support reference, explicit pending/success/error and permitted retry. Preserve uncertain-outcome recovery; no automatic destructive replay.
- UXA-08 — Record current revision, exact test scope/results, visual review and role-based end-to-end acceptance in the delivery report. A targeted/component pass cannot stand in for integration, full regression or product acceptance.

For a server-only change, document the affected consumer and why a specific UI gate is not applicable; retain all applicable security, contracts and regression tests. “Not run”, “blocked”, “skipped”, “failed” and “not applicable with reason” are distinct states. None counts as a passing test.

## Gates before integration

Every PRD implementation must pass TypeScript (web and browser fixtures when affected), lint, unit, relevant integration, affected Playwright workflows, role-based access checks, responsive layout checks, accessibility validation and impact-selected regression tests under [feature CI](../architecture/feature-ci.md). Shared shell/theme, security and unmapped changes expand to full qualification; release/MVP acceptance still requires the complete exact-revision real-service matrix. Preserve mandatory build-once immutable release checks under ARCH-11; do not start redundant image builds just for a UI iteration.

Retain initial failed evidence. Record case/command, revision, cause, product impact, responsible owner, resolution and rerun evidence before merge. Do not narrow a suite, increase retries, skip assertions, bless visual drift or bypass required checks to claim completion. Keep the PR in draft while required evidence is incomplete.

## New PRD authoring checklist

Include scope, canonical owner/non-duplication boundary, UX Foundation dependencies, role/capability matrix, discoverable entry and return route, user journeys, tasks, numbered acceptance criteria (including UXA IDs), automated tests, failure states, migration/rollback, and separate functional/UX/test/MVP acceptance status. The eight UX PRDs provide examples. Their adoption does not redefine operational MVP scope in PRD-26.

## Definition of completion

Implemented: required behavior exists at a recorded revision. Tested: specified automated checks ran and passed at that revision. Accepted: responsible product/UX review verifies priority journeys and all applicable evidence; record reviewer/date. These are separate statements.

Functional completion = verified implemented required business capabilities / agreed in-scope required capabilities.
UX completion = integrated usable required journeys / agreed in-scope required journeys.
Test completion = required passing checks / all required checks for the recorded revision, with skips/blockers/failures reported.
MVP readiness = accepted priority end-to-end workflows / agreed priority workflows, plus required release gates.

Freeze denominators and weights before reporting a percentage. Unknown scope/evidence means “not measured”, never zero or an invented estimate. Do not average these four metrics into a product-complete percentage. Legacy remaining-work estimates stay historical planning estimates.
