# Phone touch movement

The required browser suite now includes PRD-06-TC-12 at 390x844 using Chromium's
touch input dispatch through the actual drag handle. It moves the second card
before the first, requires exactly one move request and an acknowledged result,
checks persisted order and the moved revision, and compares the entire untouched
neighbor against its canonical PostgreSQL baseline. Fresh Board reads after
reload must show the same order. The document must retain its viewport width.

The same touch sequence then holds the card at the horizontal canvas boundary.
The canvas must auto-scroll to its far edge before any additional write occurs.
The user drops into the now-visible empty list; a second acknowledgment must
persist the destination and revision three, retain the complete original anchor
in its source list, and survive another fresh reload. The test has a 60-second
setup/interaction deadline, without changing any performance budgets.

Playwright collects the test and diff checks pass locally. Execution requires
the exact-image Linux suite; runtime touch evidence is pending. This case does
not prove physical-device behavior, vertical touch boundary scrolling,
screen-reader behavior, or the visual feedback latency requirement.
Those remaining requirements are not removed by this check.
