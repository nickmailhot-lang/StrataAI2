# Large-Board viewport rendering

List keyboard dragging now retains the previous selected destination for the
current drag. Each Left/Right key advances from that destination through enabled
measured targets, while the rendered source still supplies the exact translation
delta. A temporary smooth-scroll lag therefore cannot repeat an already selected
destination. Start, cancellation and drop clear this per-Board navigation state.
The existing source retention, canonical move/receipt handling and strict native
alignment assertions remain intact. See the current
[keyboard recovery and capacity evidence](kanban-release-evidence.md#keyboard-destination-recovery-and-current-capacity-evidence).

The capacity fixture retains content-free source/target geometry if Card or List
keyboard alignment fails. `STRATAAI_E2E_CPU_THROTTLE` optionally runs Chromium at
a rate from 1 to 8; default 1 preserves the mandatory normal-speed scenario.
The retained capacity measurement includes that rate. Throttled observations do
not replace normal-condition performance budgets or current release acceptance.

List-move focus recovery now waits for the canonical List revision to reach the
acknowledged revision and for the control to be admitted and enabled. A stale
pre-move snapshot cannot consume that request before the following Board refresh
disables controls. The request is cleared only after the intended control has
actually received focus. A component regression covers stale enabled state,
the acknowledged revision while disabled, and final admitted focus restoration.
All eight List-position tests, web TypeScript and focused lint pass locally;
native release verification of the phone focus failure remains pending.

PRD-04/06 now use the existing MUI and dnd-kit canvas with actual viewport windowing. No new framework, datastore, service or provider is introduced. Boards with more than 20 Lists mount the visible columns plus two neighboring columns on either side. Lists with more than 100 Cards use a measured vertical viewport with the same bounded overscan. Smaller Boards retain their existing complete layout.

Card row heights are measured through ResizeObserver, including text, indicators and covers. Measurements above the viewport preserve its scroll anchor. Native browser scroll anchoring is disabled in that viewport so the two mechanisms do not compete. Removed rows clamp retained offsets rather than mounting an entire shortened collection. Per-Board horizontal, per-List Card and outer List-section offsets survive a column's unmount/remount. The Board also retains measured Card heights by List and viewport width, so a restored pixel offset identifies the same canonical Card range. A width change invalidates incompatible heights while retaining the canonical anchor. Scope navigation discards this memory.

Stable entity identities retain the focused row, the active drag and its owning List, open Card detail, the Card awaiting returned focus, List command recovery and List creation. Virtualization does not replace canonical snapshots, permission checks, reviewed revisions, original idempotency keys or the existing command owners. Current admission/denial behavior remains with those owners. Optimistic movement still uses the existing canonical preview functions and server reconciliation.

Tab/Shift+Tab at a window boundary mounts and focuses the adjacent canonical row. Nested Card navigation consumes its boundary before the surrounding List viewport, including readonly Lists. Focusing a retained Card after detail closes reveals it through the Card viewport and scrollable List. Pointer auto-scroll remains restricted to the existing marked canvas/List/Card viewports, and dnd-kit continues to measure mounted targets; the active source stays mounted while scrolling. Complete large-Board drag execution remains required proof.

Twelve focused windowing cases exercise bounded work at 200 Lists and 5,000 Cards, measured-height remount/width recovery, retained open work, drag/source ownership, canonical keyboard navigation, nested readonly navigation, shorter snapshots and variable-height anchoring. A separate List-section case checks independent retained outer scrolling and excludes nested Card scroll events. Those tests and the 35 existing BoardScreen cases pass locally. Web typecheck/lint and production build pass. These component results do not establish native browser performance, accessibility or drag acceptance.

The existing mandatory restricted PostgreSQL rank fixture now runs an explicit alternate Playwright configuration against the same immutable release web/API/Worker images. The disposable database fixture has 200 actual Lists, at least 5,000 active Cards in one List and 100,000 archived Cards excluded from the snapshot. It scopes the actual Worker to that Organization and restores normal Worker configuration even on failure. Desktop 1280px and phone 390px cases inspect fresh canonical snapshots, bounded mounted columns/Cards, middle/end scrolling, forward/reverse keyboard navigation, Card-detail return focus, overflow and WCAG checks. No application replies or events are simulated. The original rank, lifecycle, archived-row fingerprint, concurrency and receipt assertions remain mandatory.

The scenarios retain only fixed fixture conditions, counts and observed usable-render duration in their capacity attachment. This observation is not a newly invented large-Board latency budget and does not replace the separate unchanged normal-condition <1.5s readiness, <100ms feedback, p95 <500ms acknowledgment and <200ms cached detail gates. Browser typecheck/collection and shell syntax pass locally; executed large-Board native proof remains pending CI. Diagnostics are retained before subsequent browser suites clear their output directory.

PRD-04/06 remain open. Current immutable-image native capacity, large-Board pointer/keyboard drag across window boundaries, concurrent admission/mutation behavior, mobile timing and the full remaining ticket acceptance must be verified before closure.

### Current native keyboard failure

[CI run 37272505661](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37272505661), revision `049af11`, fails the unchanged adjacent-Card center alignment assertion on both desktop and phone. Reading committed destination rectangles rather than cached droppable rectangles did not resolve the failure. Both traces reach four actual downward key presses; later capacity assertions and the subsequent native suites do not execute.

The retained desktop trace shows the source translation changing from 325px before the fourth key to 413px after settling, while the Card viewport scroll offset changes from 341764px to 341872px. On phone, translation changes from 324.5px to 413.5px while the viewport changes from 341764px to 341873px. The source row remains pinned at its original 341546px layout position. Thus the viewport advances by a full adjacent row, but the applied source translation advances by approximately 20px less. This evidence narrows investigation to source measurement and scroll compensation during window updates; it does not establish which mechanism causes the lost translation. Preserve the 2px alignment predicate, scenario timeouts, and retry settings while repairing the implementation.

A focused local Chromium reproduction using the real MUI, BoardWindow, CardDragItem and KeyboardSensor components reproduces the fourth-key 20.5px error without an API. The installed sensor implementation measures the active source again when ancestors mount/unmount rows; its scroll compensation baseline resets in a subsequent effect during smooth scrolling. The Kanban draggable measurement now retains the pinned source origin for one drag, while measuring other targets normally and honoring actual dimension changes or source node replacement. Cancel/drop clears the retained origin, and a subsequent drag starts fresh. With this production measurement and event lifecycle wired into the reproduction, twelve consecutive targets pass the unchanged <2px alignment predicate at both 1280px and 390px, twice per viewport, including cancellation followed by a new drag and drop. Maximum settled error is 0.5px. These local component/browser observations do not prove persisted moves, Worker delivery, native release-image capacity acceptance, or the remaining pointer/List scenarios; the mandatory CI case remains the release authority.

[CI run 37277533667](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37277533667), revision `cd2bea0`, passed every source gate, immutable image build and security gate. Desktop reached the later eight-key List scenario after the twelve-key Card move and intervening pointer assertions. Its window-expansion assertion incorrectly included a distant retained column at layout position 63840px in the initial boundary; the trace instead shows an initial contiguous window ending at 1344px and newly mounted columns reaching 3696px. The fixture now binds the contiguous initial window around its source, requires the eighth destination to be initially unmounted, and requires a later mounted identity absent from the initial set beyond that boundary. Alignment, persistence, timeouts and retry requirements remain unchanged.

Phone reached a fifth downward key. Before it, source translation was 432.5px, while the fourth canonical target was 434px below the pinned source origin: an already accepted 1.5px remainder. The old getter treated that target as a new positive-direction destination and interrupted the continuing scroll instead of choosing the fifth Card. The getter now treats centers within the existing 2px settled tolerance as the current destination. A focused regression covers this remainder in both vertical directions. Current release-image execution of these two repairs remains required; neither viewport has completed all capacity acceptance.

The native capacity cases also pick up a real middle Card and move across twelve adjacent keyboard targets, beyond the initial mounted buffer. They wait for the actual smooth-scrolling drag rectangle at each canonical target, retain the active source, drop through the actual move producer and require the exact persisted position/revision. Every other Card and every other List remains unchanged; returned source focus must be visible. The shell fixture then checks the complete fingerprint and count of all 100,000 archived records again. Middle-row admission waits for the native scroll event and keyboard activation uses a captured canonical identity rather than a changing positional locator. Browser typecheck/collection and shell syntax pass; execution of these added assertions remains pending.

Before the keyboard move, native cases focus a distant List to release the source's focus retention, require the original List to actually unmount, then revisit it. The original canonical middle Card, Card viewport offset and outer List-section offset must return. Merely scrolling a still-mounted retained source out of view cannot satisfy this check. Executed current-image proof remains pending.

Native capacity cases also hold a real pointer drag at the clipped viewport edge until auto-scroll reveals a fully visible canonical target beyond the initially mounted buffer. The active source must remain attached and mounted Card counts stay bounded. Escape cancellation must issue no move and leave the complete Lists/Cards unchanged. A separate pointer drop binds a currently visible destination after moving away from the scroll edge, requires one actual HTTP 200 move, source revision advancement, exact placement before that destination, unchanged other Cards/Lists and visible returned focus. Both scenarios run at desktop and phone widths through the existing pointer sensor; phone viewport execution is not physical-device touch evidence. Browser typecheck/collection pass locally; executed pointer acceptance remains pending CI.

The same native cases move an actual empty List across eight adjacent keyboard targets beyond the initially mounted horizontal buffer. They observe the source and target rectangles at each step, retain the active source and require newly mounted later columns. The actual List producer must return one successful PATCH; fresh canonical state must place the source immediately before the eighth target with one revision advancement, retaining its Cards and every complete neighboring List/Card record unchanged. The reviewed move control must regain visible focus. These assertions keep the existing scenario timeout and production limits; executed horizontal-window drag proof remains pending.

Large-Board pointer coverage also transfers the actual middle Card into an empty List beyond the initially mounted horizontal buffer. The drag travels toward available columns on either side because the real preceding rank commands can leave the populated List near either end. It requires actual horizontal auto-scroll, bounded mounted Lists, retained source attachment and a reachable List-end drop center in a newly mounted destination. The drop targets the existing named Card end target, then requires one additional successful move, atomic destination placement/revision, unchanged source/destination List records, all unaffected Cards/Lists unchanged and visible focus on the transferred Card. Later List movement compares the resulting authoritative transfer snapshot. Browser typecheck and both configured scenarios are collected locally; executed cross-List capacity proof remains pending.


Run [37332882372](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37332882372)
executes the current keyboard repairs: the complete desktop capacity scenario
passes, including Card/List keyboard moves and persisted pointer transfer. Phone
passes the twelve Card keyboard targets and reaches cross-List pointer transfer,
but fails at line 226 when reselecting a wholly visible column after stopping
edge scroll. Retained artifact 11356672774 has SHA256
`e5542a8925834a98defff8d228837aef50f009e32d30f9693c24c156fdc02d43`.
The trace records scrollLeft 65801 when the newly mounted destination at layout
left 65816.8, width 319.8 is visible. Moving the pointer to the middle settles
scrollLeft at 65761; the same destination remains mounted but its far edge is
clipped on the phone. Reselecting only wholly visible columns returns null.

The fixture now retains the later destination identified during edge scrolling,
then requires its named drop target in the viewport and its actual center inside
the canvas before releasing the pointer. Actual response, placement/revision,
unchanged neighboring records, bounded rows and source/focus assertions remain.
Scenario budgets, key pacing and retries are unchanged. Browser typecheck passes;
immutable-image phone execution of this fixture correction remains pending. The
failed enclosing run is not a green release or full PRD-06 acceptance.


Run [37337412135](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37337412135)
again passes the complete desktop native capacity case. Phone instead fails
inside the earlier wholly-visible-column poll (line 222), before stopping the
pointer. Artifact 11358149612, SHA256
`cb8752cd558e3e454826296cb7ef67d9066a17725298c929feda635f8677f4a6`,
shows horizontal scrollLeft falling from 64809 to 58338 across over twenty
columns. Sampled later rows remain mounted with width 319.8, but their far edges
are clipped in the narrow viewport; the whole-column predicate finds no target.
At scrollLeft 62418, a later column starts at 62458.8, leaving its center visible
while its far edge extends outside the viewport. This differs from the later
post-stop re-selection failure recorded above.

The fixture now identifies the actual named List-end drop surface through its
stable `data-card-list-end` identity and observes its center in the middle half
of the canvas viewport, with a visible vertical intersection. It still requires
an empty canonical destination beyond the original mounted boundary, real edge
scrolling, retained source and bounded rows. After stopping, the target must
remain in the viewport and its measured center must be inside the canvas before
the actual pointer drop. All successful HTTP response, full persisted placement,
revision, unaffected-neighbor and focus assertions remain. No execution budgets,
retries or keyboard predicates change. Typecheck/collection are source evidence;
immutable-image execution of this correction remains pending.


### Phone List keyboard correction after the third key

Run [37338765264](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37338765264)
passed the complete desktop capacity scenario. The phone case passed Card
keyboard movement and cross-List pointer transfer, then failed the per-key
List center alignment assertion on the third ArrowRight. Its retained capacity
artifact (11358714110), verified SHA-256
`fdffcefac35d6c8cb8e20149ff898a0d313f90a65493bc5bb94c5f124fcb7d20`,
shows List transforms progressing through 336px and 672px, then remaining at
672px after the third key while the canvas scrollLeft stays at 672. This is
a per-step movement failure, not the subsequent mounted-window coverage check.

List keyboard targeting now reads committed target DOM positions at the key
boundary, excludes the active source, and advances past targets within the
existing two-pixel settled tolerance. This matches the Card targeting policy.
Focused regression tests cover stale cached target positions, an active source
whose measured position is slightly ahead, and fractional already-aligned
destinations in both directions. Five focused tests pass locally. The native
release assertion, key count, pacing, budgets and retries remain unchanged;
immutable-image execution is still required before claiming this failure fixed.


### Desktop vertical pointer surface admission

Run 37341364055 failed the desktop within-List pointer case while polling for
a later visible destination, before the stop-and-reselect step. Capacity
artifact 11360866452 was downloaded and verified against SHA-256
`4119071007c9c21ccc6e206ad397ae888d3f49ea12c1f29552f5186a3dd1bf40`.
Its desktop trace shows Card scrollTop advancing from 342740 to 343016
while the pointer stays at browser y=832; the retained screenshot shows the
Card viewport clipped below the browser bottom. Focusing the source alone
does not ensure the actual nested scroll edge is reachable.

The within-List pointer fixture now reveals the Card scroll surface through
its actual ancestor scrolling before measuring source and edge coordinates.
It retains the original mounted-boundary requirement, real auto-scroll,
source retention, bounded rows, cancellation without writes, persisted move
placement and unaffected-neighbor assertions. No budgets, retries, key pacing
or geometry tolerances change. Browser typecheck and collection are source
evidence only; immutable-image execution remains pending. The phone failure
in the same older run was at the post-stop cross-List destination lookup,
covered by the separately pending retained-target correction.


### Horizontal overflow consumed by the nested Card viewport

Run 37343517613's phone capacity case reached cross-List dragging but the Board
scrollLeft stayed at 1007. Verified artifact 11362325030, SHA-256
`63bc7f40fd2b16c201585758aaa0a90a3dec22afa33e76b8176631e9291f04fd`,
shows the nested Cards viewport instead scrolling horizontally from 0 to 102
and then 6276, while its vertical offset stays 343512. A translated draggable
creates horizontal overflow in that vertical surface, and the inner-first
auto-scroller consumes it rather than scrolling the Board.

Owned scroll surfaces now declare their responsible axis. Drag start, movement
and completion maintain directional admission: horizontal movement admits the
Board canvas, and vertical movement admits nested Cards/List surfaces. Direction
can change within a drag; cancel/end retire admission. Unowned surfaces remain
excluded. Two focused policy regressions pass; source checks and immutable-image
native execution remain required. This changes production auto-scroll routing,
without reducing the existing native movement or persisted-state assertions.


### Pointer activation waits for current Board admission

Run 37345327320 failed phone pointer activation before its List keyboard case.
Artifact 11361983531 was verified with SHA-256
`7fe20b9135ba99e1f69e433a352d230043794c50320fd623eef8f6209be3a8f1`.
The phone trace records mouse-down at 72371ms and initial movement at 72376ms;
main-frame snapshots at 72353ms, 72370ms and 72378ms show the Board workspace
`aria-busy=true`, becoming false at 72509ms. The screenshot retains text
selection rather than an activated drag. A previously enabled handle did not
prove current admission at the pointer action boundary.

Both within-List and cross-List pointer setup now wait for the existing Board
workspace admission signal to settle and require an enabled handle again before
binding geometry. This uses the existing assertion budget, with no new sleeps,
request retries, larger timeouts or reduced activation/movement assertions.
Browser typecheck and collection remain source evidence; native execution of
the corrected admission boundary is required.


### Desktop List keyboard source center after scrolling

Run 37346893239 passed the complete 390px capacity case (18.9s), but desktop
failed per-key List alignment at line 308. Its verified artifact 11362637540 has
SHA-256 `f590685c519714dfe912cf3540c8a1d1fde70ded7b22ac1622602527c8018bec`.
The desktop trace shows the first List key reaching translateX=336, the second
reaching 672, and the third leaving translateX=672 with translateY=34.5, while
the Board scrollLeft becomes 336. The targets already use committed DOM
positions; the source collision rectangle can lag its separate scroll delta.

List keyboard targeting now uses the committed rendered source center as well
as target centers. Cached source geometry remains the fallback when no DOM node
is available. A regression supplies different live and cached source positions
in both axes and requires the adjacent target with full vertical correction.
The earlier fractional source case now expects the actual live source-to-target
distance. Native two-pixel alignment, all eight keys and persisted List placement
assertions remain unchanged. Current native execution remains required; the
phone pass is scoped to its recorded revision, not a complete release claim.


### Pointer source frame when hovered ancestors change

Run 37349484259 failed both cross-List destination polls. Artifact 11363453738
was verified with SHA-256
`b9cddb274428c339f74fcbefaa69ef74db9f15d79fc504fa4442c04e277745af`.
Phone Board scrollLeft advances from 61787 to 61704 before stopping; desktop
advances from 61824 to 61725. Active Card transforms then acquire Y=-343243
on phone and Y=-343307 on desktop. These offsets correspond to the original
large nested Card scroll frame, not a pointer movement. Initial mounted ranges
are contiguous in both traces, so changing their boundary does not fix this.

The dnd-kit source switches scroll accounting to the hovered destination's
ancestors while retaining the original total offset for its delta. Pointer Card
drags now retain the original source ancestors and compensate the difference
between their current offsets and the hovered ancestors' offsets before the
library applies that delta. Source scrolling still contributes its real change;
added destination offsets do not displace the visual source. Pointer collisions
continue to use pointerWithin and actual pointer coordinates. Keyboard gestures
and List gestures retain their existing path. Cancellation and completion clear
the frame, and another active identity cannot borrow it.

Two focused regressions cover removing/adding ancestors, actual source offset
changes, returning to the original parents, unrelated identities and cleanup.
Forty BoardScreen and scroll-policy tests, web typecheck, focused lint and diff checks pass. Native capacity and the original persistence/alignment
assertions remain mandatory; this correction is not a completed release claim.
