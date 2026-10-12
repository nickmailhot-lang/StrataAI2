# Nested checklist review admission during collaboration

The retained `baad6b8c` full-browser trace fails opening “After socket reconnect.”
Its successful directory and item responses contain Card version 6, Checklist
version 5 and the expected current text. This does not establish stale server
data. The item viewer deliberately rejects a response when its captured scope
or parent access admission is no longer current.

The complete current local baseline reproduces a missing item editor on desktop
while reopening “Owner canonical change”; mobile passes. The full invocation
has 1/2 passed in 108,062.846 ms, with zero skips/flaky cases. This is the same
nested review boundary at an earlier reopening, not a claim that it duplicates
every timing detail of the hosted socket-reconnect failure.

The existing five-second opening loop now admits the complete directory → item
review → edit form. It can close and reload an explicitly rejected read only
before any editable draft is visible. It requires the editor's initial value
to equal the requested canonical text before returning. User typing, completion
changes, Save/PATCH commands and original retry recovery remain outside the
loop. It does not discard a submitted intent or newly typed draft. Application
version/access guards and the 180-second scenario deadline remain unchanged.

The complete repaired repository file passes desktop/mobile in 114,456.361 ms,
with zero failures/skips/flaky cases and no diagnostic source injection. Both
real clients, server-409 dirty-draft conflict and retained completion, explicit
discard/revision admission, socket outage/reconnect, pushed current item,
committed/lost response, original key/body, revoked membership/denied retry,
unchanged canonical state, viewport and tagged accessibility checks remain.
All 75 checklist/item-management component cases pass, along with browser
type-checking, a zero-warning solution build and 213 immutable CI guards.

Independent checks find zero mismatches across 2,088 hosted source/runtime hashes,
1,414 native source hashes and four web assets. All owned containers, temporary
database/roles and credential files are removed; original stopped PostgreSQL
data is preserved. The fixtures use actual restricted PostgreSQL/RLS, API,
separate Worker and browser transport. Explicit lost-response faults remain;
optional-verification account setup does not prove managed mail. Mounted compiled
runtimes/assets do not certify the current build-once retained candidate.

Current complete immutable CI and all broader
[PRD-13 acceptance](checklist-acceptance.md) remain required. Estimated work
remaining stays **33%**; the issue remains open.

Related: [complete browser scenarios](../../tests/browser/checklist-collaboration.spec.ts),
[item-viewer access/version guards](../../apps/web/src/features/kanban/ChecklistItemManageControl.tsx).
