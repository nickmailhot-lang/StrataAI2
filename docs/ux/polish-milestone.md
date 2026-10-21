# Current-build UX polish milestone

Approved delivery priority: 2026-10-20, America/Vancouver. This milestone reorders existing work; it adds no business domain, route, API, schema or frontend framework. Use the existing UX-001–008 PRDs, feature owners, UXA-01–08 and incremental delivery ledger. Preserve existing work and historical failures.

## Priority and current reconciliation

Source inspected: main `db5762265b3dfd19aa28b03ed24ceec2fe2ec265`. This is a bounded source/evidence audit, not a claim of a complete usability study or a zero-defect build.

| Priority | Existing journey / observed gap | Next bounded action |
| --- | --- | --- |
| 1 | Organization → Work → Board/Card → return: internal header shows an identifier on desktop and hides context on phones | Continue the separately identified Organization-name/context increment with one shared-shell owner, authoritative admission and immediate withdrawal; do not add competing metadata polling here |
| 1 | Header Search Cards → first use / empty results → revise query: current page offers filters and counts but no next-step explanation | Current independent slice: add first-use and acknowledged-empty guidance using existing controls; distinguish an empty page with a cursor from a completed search |
| 1 | Settings / configuration / membership → error or conflict → deliberate recovery | Preserve integrated settings feedback and original-command recovery; audit remaining consumers individually before extracting any new form pattern |
| 2 | Supported phone/tablet/desktop and text zoom → keyboard task → return | Validate each touched journey at 320/390/768/1280px, with focus/draft/access-loss checks and actual rendered inspection |

Core Board/List/Card recovery now has newer integration/acceptance evidence on main. Reuse those capabilities and their exact evidence. An old failed PR #95 head does not establish a current-build regression; its later merged changes and current-source checks remain authoritative.

## Current slice: safe Search Cards guidance

Owner: PRD-16 (live issue #17 inspected 2026-10-20), linked UX-007-VS-02, UX-003/005/006/008. This improves the existing authorized Card search, not the deferred broader PRD-56 domain search.

- Entry remains the current header Search Cards control; results retain their current Board/Card links and browser return context.
- Before a search, explain the existing controls without issuing a request or moving focus.
- After a validated empty page, explain the next permitted step. A continuation cursor must offer continued paging guidance rather than falsely asserting the search is complete.
- After the final empty page, suggest adjusting existing filters or the explicit archived scope. Do not assert inaccessible Cards exist or reveal names/counts outside the admitted response.
- Preserve draft criteria, native submission, actor checks, epochs/abort behavior, source acknowledgments, pagination and current error handling. Guidance never resets fields, changes scope, submits a search or grants access.
- Hide successful-empty guidance while loading, on failed reads and on definitive denial. Existing privacy withdrawal stays authoritative.

## Acceptance gate before visible scope expansion

The priority journeys must have a consistent authorized entry, clear context/actions, canonical outcome and return, recoverable failures, supported responsive/keyboard operation, passing applicable tests and recorded UX review. Keep unresolved usability debt visible. A merged slice, source count or automated accessibility pass does not close an entire UX PRD or establish product acceptance.

Net-new user-facing features stay behind this milestone. Required security/reliability work and independent backend work supporting the milestone may continue. Use small isolated PRs, one shared-file owner and a reversible migration flag if a change could interrupt existing behavior. Reverting this guidance slice needs no data migration.

Required gates remain TypeScript, lint, unit, relevant integration/roles, affected Playwright, responsive/zoom, accessibility, regression and immutable release checks. Retain every failed result and its disposition; do not weaken checks. Keep the PR in draft and unmerged while required evidence is missing.

Functional completion, UX completion, test completion and MVP readiness remain separate in the single `delivery-report.md` and machine ledger. No product-completion percentage is inferred from this milestone.
