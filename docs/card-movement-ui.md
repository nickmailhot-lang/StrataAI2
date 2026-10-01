# Card movement controls

Card details expose Move card only when the authoritative Board snapshot grants
move permission, the Board is active, and the current source list is active.
Snapshot reads/errors and another pending edit disable the control. A pending move
also disables editing/closing through the shared Board busy state. Server policy,
version checks and lifecycle checks remain authoritative.

The inline review selects an active list on the current Board and explains that
the card appends to its end. An empty destination cannot submit; Cancel move
returns focus to Move card. Lists are named regions in the Board view.
Closing card details restores focus to the card's current link, including after
it moved between lists; if it is unavailable, the Board refresh control is the
fallback. The command
uses the reviewed card version and omits rank so the server allocates it inside
the owning transaction. No cached optimistic position is presented as persisted.

A result must bind card, Organization, Board and selected list; contain a valid
30-digit rank and a strictly newer safe integer revision before acknowledgment.
The Board then reads current placement. The acknowledgment is historical: another
authorized edit can have moved the card again. This message does not guarantee
the card remains at the acknowledged destination.

A missed/invalid response preserves exactly the original destination, reviewed
version and retry key in memory. An explicit Retry this move reuses that request,
even if a live snapshot already reflects a newer version. Destination changes
and cancellation are unavailable until that uncertainty is resolved; no refresh,
live notification or timeout automatically repeats a write. Admission/validation/
conflict/rate-limit rejection blocks another move until explicit current-Board
recovery. A changed review before its first submission is also blocked. A 15-second
deadline covers transport/body parsing, and unmount aborts/fences late responses.
No invitation proof or browser-persisted mutation state is introduced.

Nine control cases cover reviewed destinations/acknowledgment, lost response with
newer live state, five malformed/cross-scope acknowledgments, stale/conflicting
review and timeout/unmount. Board-screen coverage also checks absent controls for
permission/Board/list denial and reload after append acknowledgment. The focused
30-case control/Board suites pass, and the full web suite passes all 289 cases
across 26 files. Production build and lint pass locally.

Two required release-browser cases cover desktop and phone keyboard submission,
a committed-but-lost response, another authorized client's live destination
update, explicit same-body/key retry and persisted version remaining 2. They use
the actual release API and Organization-scoped immutable Worker, not mocked
business data. Local collection passes; execution remains pending.

This implements same-Board append review, not complete PRD-06/08. Pointer drag/drop,
keyboard positional reordering, cross-Board movement/copy, list movement and the
remaining lifecycle/large-data/performance/telemetry requirements remain open.
Neither these source tests nor collection establish full accessibility or latency
acceptance. The adopted MUI, API/Worker and PostgreSQL architecture is preserved.
