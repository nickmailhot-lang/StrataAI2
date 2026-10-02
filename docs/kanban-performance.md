# Kanban performance acceptance

The required release browser benchmark uses Chromium at 1280x844, one browser
worker, exact web/API/Worker images behind Nginx and real PostgreSQL. Normal
conditions are three lists and fifty active cards, one signed-in Organization
owner, warm application assets, and a Board snapshot not previously opened in
that browser. The runner hardware is GitHub's ubuntu-latest environment; results
are specific to that deployment, not a promise about every network/device.

Board readiness measures navigation through two current Board reads and an
enabled drag handle, requiring less than 1500ms. Cached detail measures clicking
the already-rendered card link through its enabled title input, requiring less
than 200ms. Twenty sequential keyed card moves alternate lists and validate
every acknowledgment/revision. Their nearest-rank p95 includes HTTP request and
response decoding, requiring less than 500ms. No percentile samples are discarded.

The benchmark attaches kanban-performance.json with fixture conditions and
durations only. No identities, titles, tenant/object IDs, bearer or retry-key
material is retained. The overall setup deadline is separate from the unchanged
performance thresholds; release retries remain zero.

The normal fixture also measures card drop feedback in the browser's monotonic
clock. A captured pointer-up starts the sample; the first frame with the card
in its destination section is followed by a frame boundary before measurement
ends. Its one move request is held before server dispatch until that observation,
so persistence cannot supply the measured update. Feedback must be under 100ms.
The released request must then receive HTTP 200. The attachment includes
feedbackObserved and feedbackMs; an unobserved destination fails the threshold
and records a null duration. This covers normal desktop card drop feedback;
list feedback and mobile feedback still need corresponding timing evidence.

The feedback sample also requires viewport intersection on both axes. After
release, the acknowledgment must return the moved ID, intended destination and
revision two. A fresh canonical Board read must place the card immediately before
the selected anchor, preserve that anchor's complete baseline record, and omit
the moved card from its former list. Thus a fast visual sample cannot pass with
an unrelated successful response or a different persisted position. These reads
occur outside the timed sample and do not relax any budgets.

This test collects locally but requires Linux CI execution. Timing success is
unproved. It does not establish the separate 200-list/5000-card/100000-archived-card
capacity requirement, mobile performance, executed under-100ms visual feedback or actual
screen-reader behavior. Those remain required work before PRD-06 closure.

The required PostgreSQL rank fixture now creates exactly 200 lists through eight
batches of 25 concurrent independent API commands. It requires 200 distinct
ranks, moves 16 lists relative to a current anchor and verifies the other 184
retain their original ranks/revisions. Historical receipt recovery remains
non-reapplying. Card creation retains its separate 128-command concurrency
fixture before SQL populates the existing 5000-card group. Shell syntax/diff
checks pass; executed 200-list correctness evidence is pending Linux CI and
does not establish 200-list rendering performance or archived-card capacity.

The required exact-image PostgreSQL fixture also seeds 100000 archived cards
in the same list as the 5000 active cards. Their ranks exceed all active ranks.
The next API append must use the active tail, and the authorized Board snapshot
must return 200 lists and only the 5001 active cards. Concurrent append and
relative moves retain their existing rank assertions. A count and a fingerprint
of every complete archived row must remain unchanged after those moves.
Shell syntax and diff checks pass; Linux execution is pending. This establishes
no browser rendering or timing evidence until those separate checks execute.
