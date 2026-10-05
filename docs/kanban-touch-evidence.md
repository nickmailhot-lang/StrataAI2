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
