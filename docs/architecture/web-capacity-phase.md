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
All **289 CI coverage guards** pass; the existing required web architecture
command includes the new phase checks.

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

After the correction is pushed as `ca541f8d`, 41 older queued main runs are
inspected individually. Cancellation is requested only for the 12 whose
frontend job has not started; all 12 frontend jobs are subsequently confirmed
completed/cancelled. The other 29 runs have already completed their frontend
job and are preserved. Their original source contains the ungated component
fixture. Run-level finalization remains separate from the cancelled frontend
jobs; cancellation is not a passing CI/release result. The new candidate's
required pipeline remains active, with the repository phase variable verified0.

The [complete frontend invocation](source-test-results.md#complete-mvp-phase-frontend-baseline--2026-10-16)
subsequently passes **2,689 cases** with exactly the reviewed **24 deferrals**,
zero failures/todo, all 184 expected files and zero mismatches across 379 captured
inputs. It takes695.105seconds with two file workers and no retry. The linked
record explains the private audit's status-label correction without a test rerun.

## Nonallocating Card-location guard and current baseline — 2026-10-16

The normal [location regression](../../apps/web/src/features/kanban/boardCardLocation.test.ts)
previously defined an unused callback that could create 5,000 Cards if a later
collection was read. Successful execution asserted it was never called. The
callback now fails immediately without allocating any collection; the same
three current-parent, moved-parent and missing-selection cases pass. Production
lookup code and the 24 deferred capacity cases remain unchanged. Web types,
zero-warning edited-test lint and all seven required architecture/phase cases
pass. This strengthens the no-load boundary without relaxing any assertion or
moving normal correctness checks into the deferred phase.

Before this test-only edit, the [complete current frontend baseline](source-test-results.md#current-complete-mvp-frontend-baseline--2026-10-16)
passes 2,707 active cases with exactly 24 reviewed capacity deferrals across all 185
expected files. All 381 captured inputs match. It uses two workers, unchanged
per-case deadlines and no retries; no capacity body runs. The changed location
file is checked separately after the edit. Current immutable/full CI and original
acceptance remain required; PRD-06 stays open at **25% estimated work remaining**.
