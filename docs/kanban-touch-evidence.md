# Phone touch movement

The required browser suite now includes PRD-06-TC-12 at 390x844 using Chromium's
touch input dispatch through the actual drag handle. It moves the second card
before the first, requires exactly one move request and an acknowledged result,
checks persisted order and the moved revision, and compares the entire untouched
neighbor against its canonical PostgreSQL baseline. Fresh Board reads after
reload must show the same order. The document must retain its viewport width.

The same touch sequence then holds the card at the horizontal canvas boundary.
The canvas must auto-scroll horizontally until the destination center is inside
the viewport before any additional write occurs. The test waits for the named
destination announcement before releasing the touch input.
The user drops into the now-visible empty list; a second acknowledgment must
persist the destination and revision three, retain the complete original anchor
in its source list, and survive another fresh reload. The test has a 60-second
setup/interaction deadline, without changing any performance budgets.

The complete case passed in exact-image Linux browser run 36963451064 at
5ecf15aa47b017f0fc871b9272bb3d1b08ad7d06, including both acknowledged moves,
unchanged canonical neighbor checks and both reloads. The whole run failed a
separate phone list keyboard case; this is scoped touch evidence, not a green
release claim. This case does
not prove physical-device behavior, vertical touch boundary scrolling,
screen-reader behavior, or the visual feedback latency requirement.
Those remaining requirements are not removed by this check.

The boundary-scrolling release fixture now also runs at 390x844 with Chromium
touch dispatch. It requires the actual Card handle to enter its pressed drag
state, vertical scrolling inside the source List, then horizontal scrolling of
the Board canvas. Native touch cancellation must end the drag without a move
request; a fresh PostgreSQL-backed Board read must preserve the complete List
and Card baseline. Opening and closing Card details afterward must restore the
focused link and both scroll offsets. The existing desktop and phone mouse
cases remain, with the same assertion and execution budgets.

Browser type checking and collection of all three boundary cases pass locally.
Execution against the immutable release images remains pending. This adds
coverage for vertical touch scrolling and cancellation; it does not establish
physical-device behavior, touch performance, or complete PRD-06 acceptance.

## Observed touch pickup admission — 2026-10-16

Run `38910209597` at `d236ae8f`, full-browser shard 2, fails the phone touch
case at its original five-second destination-visible poll. The first reorder
persists successfully. After reload, the handle passes its enabled check, but
a protected Board GET begins before the second touch-down. Decoded retained
snapshots show the handle disabled immediately before that input; later
snapshots show no active drag. The GET runs at monotonic `3479339.833` for
32.543 ms; touch-down is dispatched at `3479368.740–3479371.968`. Consequently,
this trace does not establish a failure of scrolling during an active drag.
The diagnostic archive `12088281005` has its advertised digest and all **26**
payload checksums verified, with byte-identical canonical build metadata from
artifact `12057722184`. Archive SHA-256:
`fc3d967841d900d2ffbc0e25377d160efd84fa5203ca85dfe60719648392212b`.

The [complete phone test](../tests/browser/kanban-touch.spec.ts) now observes
`aria-pressed=true` after native touch pickup. Before pickup it establishes
current enabled focus and reads current geometry. A bounded five-second
admission loop cancels only an unadmitted held contact before another pickup;
it stops once the drag is active. Admission never sends `touchEnd` or a move
request. Both original twelve-step paths, the two original drops, exact write
counts, acknowledgments, canonical neighbor/order/version checks, both reloads,
real boundary scrolling and destination announcement remain. The original
60-second case and five-second scrolling deadlines are unchanged. Application
scrolling, sensors and permission checks are unchanged.

The complete phone case passes on its first attempt in **9.754 seconds**
(**36.491 seconds** for the full invocation with fixture pacing), with no
failures, flakes, skips or report errors. Four controlled protocol checks using
the actual extracted pickup implementation cover normal admission, an ignored
first contact, an already-active drag and activation during focus. They verify
no admission drop and no further input after pickup; the ignored contact is
cancelled before the successful pickup. All **11** existing admission-helper
checks, browser types and both browser/integration coverage verifiers pass.

The native fixture uses fresh restricted PostgreSQL 17.11 with all **149**
migrations, current mounted API/Worker assemblies and current Vite assets on
cached runtime images. All **1,286 source inputs, 71 binaries and four served
assets** match; the API and Worker identities remain unchanged. Owned cleanup
restores the original Docker inventory and removes **3,739 bytes** of temporary
configuration. No original database is used; no image is built, pulled or
imported, and no load test runs. Preceding `e88c6514` assembly metadata,
optional email verification and a controlled mail provider limit this to
supporting local evidence. Current immutable-image CI, physical-device/assistive
behavior, performance and full PRD-06 acceptance remain open. PRD-06 stays at
**25% estimated work remaining**, an unchanged planning estimate.
