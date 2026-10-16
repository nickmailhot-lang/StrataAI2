# Frontend capacity phase and active MVP correctness checks

Large-load testing stays deferred until after the operational MVP. A source
audit found that the default frontend suite still allocated 5,000 Cards and
200 Lists in the Board window component fixture. The earlier backend and
retained-image phase gates did not cover this component fixture.

The [original capacity suite](../../apps/web/src/features/kanban/BoardWindow.capacity.test.tsx)
now requires the exact `STRATAAI_POST_MVP_LOAD_TESTS=1` opt-in. Its 24 original
test bodies, dataset sizes, assertions and deadlines remain intact. Dataset
allocation moves into its gated `beforeAll`, so even collection does not create
the large arrays. With the setting absent or `0`, Vitest explicitly reports
these 24 cases as deferred; they do not count as completed capacity acceptance.

The [always-active functional suite](../../apps/web/src/features/kanban/BoardWindow.test.tsx)
uses 128 Cards and 32 Lists, just above the production virtualization cutoffs
of 100 and 20. All 24 corresponding functional cases remain active: bounded
mounting, retained scroll, pinned work, active drag ownership, canonical keyboard
boundaries, disabled-handle recovery, deliberate focus departure, snapshot
clamping, nested read-only focus, measured heights, width changes, theme spacing
and authoritative item updates. Fixture-specific end IDs and scroll positions
match this smaller dataset. Production behavior and the original mounted-row
bounds are unchanged; these cases establish correctness, not capacity.

The focused native Vitest invocation with flag0 passes all 24 functional cases
and reports exactly 24 deferred capacity cases. Web types and zero-warning lint
pass. The [phase policy checks](../../tests/web-capacity-phase.test.mjs), invoked
by the existing required [web architecture command](../../tests/web-architecture.test.mjs),
collect the actual source without running test bodies or lifecycle hooks.
All seven phase/HTTP-boundary checks pass. They verify exact opt-in, no large
allocation during collection in either mode, active small cases in every mode,
the original critical case names, and detection of eager allocation/default
activation. Opt-in verification does not execute the capacity workload.

The first Windows launch incorrectly ran from the frontend folder: preparation
failed to locate the edit helper, but the next command still executed the old
capacity component file once (24 passing cases). That run violated the requested
sequencing and is retained separately from phase-disabled verification. The
corrected launch applies and verifies the edit before starting tests. An initial
collection error from nesting Vitest's hoisted mock is also repaired by keeping
the original mock at module scope; the final focused report has no suite errors.

Current immutable-image/full CI and each issue's original criteria still govern
closure. PRD-06 remains open at **25% estimated work remaining**. Capacity
deferral does not reduce its scope or certify the operational MVP.
