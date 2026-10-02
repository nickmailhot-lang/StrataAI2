# Phone touch movement

The required browser suite now includes PRD-06-TC-12 at 390x844 using Chromium's
touch input dispatch through the actual drag handle. It moves the second card
before the first, requires exactly one move request and an acknowledged result,
checks persisted order and the moved revision, and compares the entire untouched
neighbor against its canonical PostgreSQL baseline. Fresh Board reads after
reload must show the same order. The document must retain its viewport width.

Playwright collects the test and diff checks pass locally. Execution requires
the exact-image Linux suite; runtime touch evidence is pending. This case does
not prove physical-device behavior, cross-list touch movement, touch boundary
scrolling, screen-reader behavior, or the visual feedback latency requirement.
Those remaining requirements are not removed by this check.
