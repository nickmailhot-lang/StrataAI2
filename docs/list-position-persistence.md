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
