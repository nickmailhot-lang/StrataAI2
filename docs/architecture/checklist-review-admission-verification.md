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

### Checklist read reopening admission follow-up

Retained-image CI run 38449472806 failed desktop checklist reads at the final
reopening of `Show checklists`: the expected newly created heading was absent.
That read-only opening now uses the existing bounded `openAdmittedReview` helper
to observe the actual heading. The two-session refresh, lost-response recovery,
exact two mutation requests with the original key/body, typed versions, canonical
progress, accessibility checks and scenario deadline remain unchanged.

Browser type checking, four-case discovery, all 398 cases' shard coverage and
222 CI guard tests pass. These checks do not establish the cause of the original
failure or execute the retained-image scenarios; that execution remains pending.
PRD-13 stays open at **33% estimated work remaining**.

The subsequent small six-case local execution at `c2ac8cdc` passes both complete
checklist-read viewports, including original-key lost-response recovery,
two-session canonical refresh/progress and accessibility assertions. The combined
invocation is **3/6** in 320,267.437 ms because desktop archive recovery and both
List-move cases fail; it is not reported as a passing suite. Actual restricted
PostgreSQL, API, separate Worker and browser transport are used with controlled
account setup and mounted compiled applications. Owned fixtures, credential
files and newly pulled support images are removed. This supplements the
type/discovery checks but does not certify the retained release or complete
PRD-13. Estimated work remaining stays **33%**.

After the archive/List-move request-admission correction, the same complete
three-file invocation passes **6/6 in 318,145.017 ms**, with zero failures,
skips/flaky outcomes or diagnostic source hooks. Both checklist-read scenarios
retain their prior request, progress, two-session and accessibility assertions.
All captured inputs match and owned fixtures/credentials/new support images are
removed. Current retained-image CI and full PRD-13 acceptance remain pending;
estimated work remaining stays **33%**.

## Observe explicit discard before independent reopening

Retained-image run 38488633147, browser partition 2 job 115637762773, fails
the desktop collaboration case at reopening after reconnect: the editor still
contains `Draft during socket outage` instead of `After socket reconnect`.
That path is unchanged on `7b155f49`. The preceding revision check can already
be satisfied by the pushed version before the discard keypress, so it does not
prove that the old editor closed. This does not establish stale server content
or justify silently discarding the draft inside the opening helper.

Both scenario-owned explicit discard points now check enabled focus and observe
the old editor's removal within a bounded five-second phase. Only that existing
review can close; the independent opening, canonical initial-value assertion,
new typing and Save/PATCH stay outside. The editor's strict initial-value check
is retained. The obsolete unused helper import is removed after capture.

Both complete maintained desktop/phone scenarios pass in **133,758.085 ms**,
zero failures/skips/flaky outcomes or diagnostic source hooks. Original two
actors, actual server-409 concurrent conflict, preserved text/completion choice,
canonical versions, socket outage/reconnect, pushed item, committed/lost response,
exact original body/key, membership withdrawal, denied old-receipt replay,
unchanged canonical state, accessibility and 180-second deadline remain. No
mutation is repeated by discard admission, and no large-load tests run.

Browser types, 222 CI guards and 398-case shard coverage pass. During capture,
all 2,143 hosted source/runtime hashes, 1,441 browser-source hashes, 168 frozen
browser files and four web files match. Owned containers, credentials, fixture
volumes and newly pulled support images are removed, preserving original
stopped PostgreSQL data. Read-only mounted compiled applications and controlled
account setup provide supporting interaction evidence; current retained-image,
strict provider and full PRD acceptance remain pending. Estimated PRD-13 work
remaining stays **33%**; the issue remains open.

## Observe complete review retirement (2026-10-14)

Retained run 38512205117, partition 2 job 115707086305, reaches the
independent reopening with the contributor's old draft still present. A
protected read can temporarily remove the text box while retaining its
item-management review. The explicit discard helper now observes removal of
the entire named item-management section and still checks text-box removal
before reopening. The five-second bound, both explicit discard points, strict
canonical initial-value assertion and 180-second scenario deadlines remain.

The unchanged local baseline passes 2/2 in 120,435.002 ms; the hosted failure
is not claimed as locally reproduced. The changed complete desktop/phone file
passes **2/2 in 123,530.732 ms**, with no skips, failures, flaky outcomes or
retries. Conflict/draft preservation, socket recovery, actual committed/lost
response, original body/key, membership withdrawal, denied old-receipt replay,
canonical state and accessibility assertions remain intact.

Browser types, 224 CI guards and intact 398-case shard collection pass. An
independent check confirms all 2,143 hosted inputs, 1,441 native inputs, 168
frozen browser files and four web assets match. Owned containers and private
credentials are removed; the original database remains stopped and preserved.
This controlled compiled-application capture supplements release evidence;
current immutable/full CI and full acceptance remain required. PRD-13 remains
open with estimated work remaining **33%**. Large-load testing stays deferred
until after the operational MVP.

## Item-position peer read admission — 2026-10-14

Retained `965b4bbc` browser partition 2, job `115916927102`, passes 95 cases and
fails the desktop `checklist-item-position.spec.ts` peer's initial item display.
The trace records the Show-items keypress, successful checklist reads and **no
item GET** before the unchanged three-caption assertion times out. The case is
unchanged on current main before this repair.

Only this read-only peer opening now uses the existing `openAdmittedReview`
helper with the first rendered item as its target. It establishes enabled
keyboard focus, observes the actual opening and stops once an item is visible.
The original exact three-caption assertion remains, as do all move controls,
original-key/body replay, exact request counts, live order, final IDs/versions,
accessibility and 90-second scenario deadlines. Mutation submission is unchanged.

The complete unchanged local desktop/phone file passes **2/2 in 108,155.189 ms**;
it is not claimed as local reproduction. The complete changed file passes
**2/2 in 109,035.072 ms**, with two total attempts, no skips, flaky results,
retries, errors or private diagnostic hooks. Browser typing, all 242 CI/startup
guards and collection of 398 cases across 145 intact files pass. Independently
matched 2,145 captured inputs, 170 browser files and four frontend assets.

These are supporting fresh restricted-role PostgreSQL and compiled API/Worker
fixtures with current frontend assets, not a complete immutable-image release.
Owned containers, database/roles, credentials and newly pulled support images
are removed; the original stopped database remains. Current full CI and the
remaining PRD acceptance stay required. Estimated PRD-13 work remaining remains
**33%**; large-load testing stays deferred until after the MVP.
