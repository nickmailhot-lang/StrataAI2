# Card move browser evidence

Run 36938885572 at 56a60d5 completed its exact-image integration checks through
authenticated browser execution. Its phone relative-card-move case failed the
unchanged-anchor comparison: createdAt and updatedAt from the initial creation
response retained seven fractional digits, while the later PostgreSQL-backed
Board read returned six. The job log showed identical anchor ID, rank, version,
scope, title, description and lifecycle values. This failure does not prove
that the move changed the anchor.

The case now takes its anchor baseline from a persisted Board read immediately
after creation and before movement. It still compares the entire baseline
object after the move, including timestamps, rank and version. Lost-response
recovery, exact retry key/body, second-client delivery, placement ordering,
focus restoration and mobile overflow assertions are retained. No production
timestamp behavior, deadline or retry policy is changed.

Both browser cases collect locally. Executed repaired-case evidence requires
the Linux release topology; collection alone is not runtime acceptance.

## Observed request and keyboard pickup admission (2026-10-14)

Retained run `38513307007`, shard 1 job `115712797428`, recovers both original
phone move requests and acknowledges the receipt, then fails keyboard pickup:
the drag handle has no active-drag attribute after Space. An earlier run
`38511047542`, job `115698611419`, stops at the original-recovery acknowledgment.
Current unchanged local desktop/phone cases pass **2/2 in 105,902.504 ms**;
neither hosted failure is claimed as locally reproduced or a product root cause.

The fixture now admits initial confirmation and recovery until their respective
request counts advance, using the existing one-observed-request helper. It
never activates again after the POST is observed. Keyboard pickup observes
the actual active-drag attribute inside a bounded five-second admission step,
rechecking after focus before Space so an active drag cannot be toggled. Pickup
does not submit a mutation; movement, cancellation and drop remain unchanged.

The complete changed desktop/phone file passes **2/2 in 106,328.281 ms**, zero
skips, unexpected/flaky results, retries, report errors or diagnostic hooks.
Original key/body and exact two move requests, persisted anchor/rank/version,
second-client delivery, return focus, phone zero-write cancellation and one
committed keyboard move, desktop pointer ordering, canonical reload, viewport
and 120-second deadline assertions remain. Browser types, 224 CI/shard/startup
guards and all 398 cases across the intact shard inventory pass; collection is
not execution. All 2,143 hosted inputs, 1,441 native inputs, 168 frozen browser
files and four served assets match independently. Owned fixtures/credentials/new
support images are removed; original stopped PostgreSQL is preserved.

This is compiled-runtime/controlled-account support, not current immutable/full
release certification. PRD-06 and PRD-08 remain open at **25%** and **35%**
estimated work remaining, respectively. Large-load testing stays disabled
until after the operational MVP milestone.
