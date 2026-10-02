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

This test collects locally but requires Linux CI execution. Timing success is
unproved. It does not establish the separate 200-list/5000-card/100000-archived-card
capacity requirement, mobile performance, under-100ms visual feedback or actual
screen-reader behavior. Those remain required work before PRD-06 closure.
