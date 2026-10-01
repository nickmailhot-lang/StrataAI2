# List position persistence

PATCH /lists/{id} retains existing rename/explicit-rank behavior and additionally
accepts beforeListId or moveToEnd=true with name and version. Relative positions
reject self/empty anchors, explicit rank mixed with a relative position, and
simultaneous before/end. Current permission, active Board/list and version checks
precede allocation. Existing request fingerprints remain unchanged when neither
position field is supplied; new fields bind relative-position retry receipts.

PostgreSQL resolves the active anchor and predecessor (or end) with scoped,
bounded-result queries after the Board lock, excluding the moving list. The
tenant/Board/version-bounded update, LIST_MOVED audit/event/job and receipt share
the owning command transaction. Normal moves do not renumber siblings. Missing
anchors produce a conflict; exhausted intervals return rank_space_exhausted.
The in-memory store resolves the same positions under its gate.

API coverage checks relative insertion, append, mixed/self positions, key reuse,
missing anchors, stale versions, sibling preservation and historical replay after
a later move. The required release PostgreSQL fixture checks sixteen independent
concurrent list positions and durable replay without reapplying an old rank.
Build and shell syntax are checked locally; runtime execution remains pending
Linux CI. List UI/drag/drop, keyboard equivalents, provisional list feedback,
rebalance, performance and complete PRD acceptance remain unfinished.

## Reviewed list controls

The Board now offers list-position review for active, currently movable lists
with a valid canonical revision. Users choose End of Board or Before a current
active sibling, then confirm. The captured name/version/position and retry key
remain fixed after uncertainty, including a newer canonical rename. Historical
acknowledgments are checked for list/Organization/Board scope and valid newer
revision/rank before reading current ordering. Rejection blocks a new review;
vanished unsubmitted positions require a current choice. Saving/body parsing has
a 15-second deadline, and unmount aborts/fences pending work. No automatic read
repeats a write. Cancellation and acknowledgment restore keyboard focus after
current ordering is usable.

Local verification: all 300 web tests across 28 files pass, with the final five
list-control cases passing after focus handling was updated. Production build,
typecheck and lint pass. Two required release-browser cases collect for desktop
and phone keyboard placement, persisted rank/revision, preserved sibling rank,
focus recovery and ordering after reload. Their execution remains pending Linux
CI. Pointer/list drag/drop, provisional list feedback, virtualization, rebalance,
measured performance and full accessibility acceptance are still incomplete.

## Provisional ordering

The first reviewed submission immediately projects the list into its selected
position while saving. The projection preserves canonical list/card objects,
ranks and revisions, and is discarded on completion or unmount. A saving status
identifies ordering as provisional. Uncertain or rejected results return to
canonical ordering and read current state; explicit acknowledgment recovery does
not project a historical position over later edits. Missing/archived positions
or unavailable movement permission produce no speculation.

Source coverage checks immutable prepend/relative/end projections, denied and
missing scope, clearing after uncertainty, no projection on historical recovery,
and Board-level ordering/rollback without an automatic second write. The full
302-case web suite passes before the final Board integration case; final focused
control/Board coverage and release CI provide the remaining verification. This
does not prove measured sub-100ms feedback, drag/drop or complete acceptance.

## List drag/drop

The Board now uses the installed dnd-kit core context with pointer and keyboard
sensors, dedicated list drag handles, sibling drop targets and an end target.
Dragging uses a transform; only the handle disables touch scrolling. Dropping
before a sibling or at the end submits through ListPositionControls, sharing its
version checks, retry receipt, preview, deadline and canonical reconciliation.
The drag-start name/version is captured; a newer canonical revision blocks the
drop and requests current ordering. Cancel/outside/self drops do not write.
Unavailable/archived scope has no handle, and a list awaiting acknowledgment or
conflict recovery has its handle disabled. Card links remain separate targets.

Source verification: the 305-test full web suite passes, with final focused
control/Board tests checking direct admitted drop, stale drop rejection and
recovery fencing. Production build/typecheck/lint pass. The desktop release
browser case additionally drags a list before its sibling and checks actual
persisted order/versions before reload; both desktop/phone cases collect.
Runtime pointer/browser execution, boundary auto-scroll and full keyboard/screen
reader/performance acceptance remain pending. Card drag/drop, rebalance and
virtualization are unfinished. No PRD closure follows from source or collection.

Keyboard dragging now targets the next enabled, measured column for Left/Right,
rather than using fixed pixel increments. Target centers account for responsive
widths and the narrower end target; missing measurements and boundary presses
do not move. Three source cases verify these behaviors. The release browser
cases at desktop and phone widths also cancel a keyboard drag with Escape and
require zero writes/unchanged persisted lists, then complete a keyboard drop and
check the moved revision, unchanged sibling rank and reload. These cases collect;
their runtime execution and assistive-technology behavior remain unverified.

API usage follows the installed types and the official
[dnd-kit draggable guidance](https://dndkit.com/legacy/api-documentation/draggable/).
