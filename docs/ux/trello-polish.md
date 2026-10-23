# Trello-style current-build polish

User-approved desktop/mobile wireframes establish the presentation direction for existing MVP workflows: readable Organization context, familiar lists and cards, focused Card details and explicit Search. They add no business scope or navigation destinations.

## First increment: Organization context and Work boards

Owners: PRD-03/04/06; UX-001/002/003/005/006/008 and UXA-01–08. Focused Task/Test: `UX-003-VS-03-T-02` / `UX-003-VS-03-T-02-TEST-01`.

- Reuse the actor-bound Organization-name header already implemented in PR #117, including offline/visibility/access withdrawal. This child branch does not edit InternalAppShell, OrganizationContextLabel, navigationModel or appTheme.
- Present currently admitted Organization and Board links as readable surfaces. Organization role descriptions come only from the existing validated summary. Board tiles retain their canonical links and complete names; they show no invented counts, visibility, progress or preview data.
- Use the existing MUI theme for compact list/Card surfaces, labels before the Card title and discoverable icon drag handles. Preserve their full accessible names, dnd-kit listeners/attributes, focus refs, disabled/role conditions, List scroll memory and BoardWindow geometry/phase obligations.
- Keep every existing watch, move, rename, archive, filter, copy, date, metadata and original-command/recovery control available. Do not collapse inline recovery owners into a hidden menu as a cosmetic shortcut.
- Preserve user-selected Board backgrounds, cover handling, canonical Card/Board URLs, editing, browser Back, private withdrawal, independent lifecycle recovery and Portal boundaries. No request, command, subscription, schema or API contract changes.

The one feature integration owner for this increment edits OrganizationHome, BoardScreen, BoardCardLink, CardDragItem and ListDragColumn. Existing PR #117 shared-shell/context ownership remains unchanged; PR #118 recovery work and PR #119/#120 Search work remain separate. Future integration must preserve every branch's tracking history and failures.

## Safe rollout and remaining scope

Reuse `VITE_STRATAAI_WORK_UX`: default/on applies these additive surfaces; `0` restores the original link rows, Card label order, text drag handles, headings and spacing. Existing recovery/state owners stay mounted. Test both presentations. No data migration or new flag is needed.

The lazy Board drag icon is included in the existing Vite dependency optimization to prevent mid-navigation cold-development graph invalidation. This is a development startup safeguard, not a production React/renderer replacement.

The implementation does not copy the illustrative mockup's sample records, local state or simulated writes into the application.

## Second increment: Card details and Search

Owners: PRD-09/16, Tasks `PRD-09-VS-01-T-05` / `PRD-16-VS-01-T-02` and their child tests; UX-003/005/006/007/008 and UXA-01–08. Prepared on an isolated branch while PR #121 remains frozen for full qualification; merge depends on its qualified integration and this increment's own complete checks.

- Reuse the current Card right panel/full-screen phone dialog. Its admitted Board/List caption uses the existing memoized Card location; no additional reads, scans or new links. Withdraw that caption during protected reads, read failure or access loss. Preserve the exact dialog name, existing refs, dirty-draft guard, focus/scroll return, safe Markdown and every original recovery owner.
- Clarify the core editor with the existing MUI surface/title hierarchy and visible Save/Close footer. Use sentence-case actions without renaming their accessible controls. No secondary command is removed or unmounted.
- Preserve PR #119's Search guidance and PR #120's keyboard-paging changes through their actual commit histories. Lay out the same five fields in a bounded responsive grid; retain native submission, feedback/paging refs, cursor/query/acknowledgment/actor checks, unsent edits and access withdrawal. Result surfaces retain every authorized metadata field and the canonical Card URL; no invented Organization names, counts or client-side filtering.
- Existing Work UX flag `0` restores the previous layout and typography. The singular result-count wording correction applies to both layouts; count meaning and source remain unchanged.

Validate complete affected component files and eight new controlled Card/Search scenarios at 320/390/768/1280px, plus rollback, 200% text, keyboard dirty-edit cancellation/return, paging, errors and denial, and applicable Axe. Inspect actual desktop/mobile captures. Existing real-service Markdown, mutation/recovery, confidentiality, normal performance and immutable release suites remain mandatory. No current parent PRD or MVP closure is implied.

## Required evidence before integration

Retain failed diagnostics and evaluate causes; preserve all assertions, deadlines, retries and the original 24-case functional/capacity counterparts. Run complete affected suites, types, lint, architecture/registration checks, four-width keyboard/reflow/200%-text/Axe, original flag rollback and actual rendered inspection. Controlled-source browser results are separate from real-service roles/persistence/recovery/CSP/immutable-release regression. Fresh full exact-head checks, review and target integration remain mandatory before acceptance.

Use the single [delivery report](delivery-report.md) and machine ledger to distinguish functional implementation, UX integration, test scope and MVP readiness. A design proposal or targeted pass never closes a parent PRD or establishes product readiness.
