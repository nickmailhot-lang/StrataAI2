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

This implements same-Board move review, not complete PRD-06/08. Pointer drag/drop,
cross-Board movement/copy, list movement and the
remaining lifecycle/large-data/performance/telemetry requirements remain open.
Neither these source tests nor collection establish full accessibility or latency
acceptance. The adopted MUI, API/Worker and PostgreSQL architecture is preserved.

## Relative position review

The review now offers End of list or Before a current destination card, excluding
the moving card. Choosing another destination resets the position to End of list.
The request binds the optional beforeCardId to the reviewed version and retry key;
it never computes ranks from a potentially stale client snapshot. The API resolves
current neighbors under the destination lock. A vanished anchor blocks an
unsubmitted review until a current position is selected. After an uncertain
response, explicit retry preserves the exact original position and key even when
the anchor disappears or the moving card has a newer live version.

Two additional control cases cover relative-position recovery and vanished-anchor
reselection. All 291 web tests across 26 files pass locally, including the focused
32 control/Board cases; production build and lint pass. The required phone browser case now selects Before Position anchor
with the keyboard and checks persisted ordering and unchanged anchor rank after
retry; the desktop case retains append coverage. Browser collection passes;
actual execution of this extension remains pending Linux release CI. These
controls provide a keyboard-accessible position choice but do not establish full
drag/drop equivalence, complete WCAG acceptance, virtualization or latency targets.

## Provisional placement while saving

Submitting a reviewed move immediately presents the card in the selected list
and relative position while the request is pending. This projection does not
change the canonical snapshot, rank, revision or request fingerprint. The card
details remain bound to canonical data; a status explicitly identifies placement
as provisional. Same-list moves do not duplicate the card, and inaccessible or
vanished destinations/anchors produce no speculative placement.

Completion removes the projection. Success reads current placement; a rejected
or uncertain response also reads the current Board, while uncertainty retains
the original request/key for explicit recovery. No read automatically retries a
write. Existing denied-scope clearing and unmount fencing still apply. Source
coverage checks projection immutability, same-list insertion, invalid scope,
pending feedback, uncertainty cleanup/read recovery and Board-level placement
before acknowledgment. This adds card feedback for reviewed moves; list feedback,
drag/drop and measured sub-100ms acceptance remain unfinished.

Local verification: all 295 web tests across 27 files pass. The final updated
21-case Board suite also passes, including provisional placement before a
deferred acknowledgment. Production build, typecheck and lint pass. Exact-image
browser execution for this change remains pending CI.

Recovery requests deliberately retain canonical placement instead of projecting
the original destination again. A retry may acknowledge an already completed
historical move after a later edit; its status therefore describes acknowledgment
recovery. Control coverage verifies that the original submission publishes one
projection and same-key recovery after a newer live version publishes no second
projection. Both outcomes still read current placement.

## Card drop command integration

CardMoveControls accepts a card-scoped drop request containing the drag-start
revision, destination list and optional before-card position. It submits through
the same version-bound command, provisional feedback, deadline, acknowledgment
validation and exact-key recovery path as reviewed keyboard movement. A newer
card revision blocks a fresh drop and requests current state. An unresolved
intent cannot be replaced by a later drop. A recovery callback exposes that
fence to the canvas drag handles.

Fifteen focused control tests pass, including drop delivery with a lost
response/exact retry and stale drag-start rejection without a write. Typecheck,
lint and production build pass.

## Canvas card dragging

Dedicated card handles and before-card/end-of-list targets now share the Board's
dnd-kit context. Namespaced targets and filtered collision detection separate
card moves from list moves. Only the handle disables touch scrolling; card
links retain detail navigation. Empty active lists expose an end target.
Drag-start revisions come from canonical state. Cancel, self and outside drops
do not submit. Current Board/list scope and move permission gate targets;
loading, write activity, open card dialogs and unresolved recovery fence handles.
The stable canvas move control remains outside reparented cards and uses the
existing preview and canonical reconciliation. Named card announcements describe
requested positions and require move-status confirmation.
Changing Board scope retires drop events so returning cannot resubmit them.
After acknowledgment and current-read completion, canvas moves restore focus to
the moved card's current link (or Board refresh if the card is unavailable).
Navigating to another Board or opening card details retires this focus request.
The desktop browser drag scenarios also require that current link to be focused;
runtime execution remains pending.

The desktop release browser scenario now drags into an empty source list, then
drags a newly created sibling before the moved card in the same list. It checks
persisted order/revisions and reload. Both card and list browser cases collect;
runtime pointer execution, mobile drag, boundary auto-scroll, assistive
technology and large-board performance remain unverified. No full PRD closure
follows from these source changes.

Source verification: the full web suite passed 313 tests in 31 files; final
focused Board/announcement cases passed 25 tests. Typecheck, lint and production
build pass. Four card/list release browser cases collect.

## Boundary scrolling

List columns now have a viewport-relative maximum height and vertical overflow.
The shared drag context explicitly permits auto-scroll only for the Kanban
canvas and list containers. Desktop/phone release scenarios create six columns
and twelve cards, require actual overflow in both directions, hold a card at
each boundary and require the corresponding scroll offset to increase. They
then cancel and require zero move writes, unchanged persisted list/card records
and no page-level horizontal overflow. Both scenarios collect locally; actual
auto-scroll execution and large-board performance remain pending Linux CI.

## Keyboard card targets

Card keyboard dragging now moves to measured targets rather than fixed pixel
increments: Up/Down stays in the current column; Left/Right chooses the nearest
vertical target in the adjacent available column. It ignores list targets,
disabled/unmeasured targets and out-of-range directions. Three source cases
verify navigation, the end target and boundaries. The phone release case now
cancels a keyboard card drag with zero writes/unchanged records, then completes
a move into an empty list, requires one write, current-link focus, persisted
revision, unchanged anchor and reload. Browser execution remains pending.
Pointer collision detection now requires containment for both cards and lists;
outside drops cannot select the nearest target. Keyboard collision uses centers
so targets remain selectable despite different card/target heights.

Recovery coverage also delivers a second drop after an uncertain first drop and
a newer canonical placement. It requires no automatic replacement write, a
disabled unchanged destination, the original retry body/key and no repeated
projection of historical placement. This source check does not prove executed
browser recovery acceptance.

