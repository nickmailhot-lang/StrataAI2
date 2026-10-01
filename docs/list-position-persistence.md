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
