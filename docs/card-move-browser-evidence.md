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
