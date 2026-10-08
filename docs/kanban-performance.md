# Kanban performance acceptance

## Local desktop and phone diagnostic, 2026-10-07

The unchanged two-scenario benchmark was executed locally against a frozen compiled
Production API, restricted PostgreSQL and the current Nginx configuration. The web
source initially matched the existing frozen bundle. An initial `127.0.0.1` fixture
failed before measurement because the request client did not return the Production
Secure cookie; the subsequent fixture uses the same `localhost` origin as CI.
Cookie policy, rate limiters, retries and all timing budgets remain unchanged.

That baseline failed desktop readiness at 2164.4 ms and phone movement feedback
at 129.4 ms. Automatic drop commands now defer the destination/position review
controls while the first write is saving. The saving status and provisional
placement remain visible; a lost response restores the original immutable review
and its original-key recovery. The new component regression fails before this
change and passes afterward. All 23 selected move/drag tests pass, as do web
TypeScript, targeted lint and production build checks.

The fresh frozen web bundle's complete follow-up invocation still fails both
scenarios. It retains all twenty mutation samples per viewport and reports:

| Viewport | Usable Board (<1500 ms) | Drop feedback (<100 ms) | Cached detail (<200 ms) | Mutation p95 (<500 ms) | Outcome |
| --- | --- | --- | --- | --- | --- |
| 1280x844, mouse | 1069.5 | **131.1** | 182.7 | 279.5 | Failed feedback |
| 390x844, Chromium touch | 1072.6 | 84.7 | **255.0** | 164.7 | Failed cached detail |

These are scoped Windows-hosted Chromium/cached-runtime observations, not the
documented GitHub Ubuntu retained-image deployment or a green release claim.
The changed readiness numbers alone do not establish a causal improvement from
the move-control change. Desktop feedback and phone cached detail remain explicit
performance work; no threshold, failure outcome or percentile sample was relaxed.
The two disposable API/web containers are removed after execution, preserving
the existing runtime and volumes.

The reporter now labels runs without both CI context and a valid source revision
as `unverified runtime`, retaining a valid source revision when supplied without
certifying its images. Invalid revisions are withheld rather than reflected.
The trusted release workflow retains its exact-image label and build-once/image
verification gates. All eleven reporter privacy/validation regressions pass.
Local reports and even a passed individual timing do not close PRD-04/05/06.

## Drag registration and readiness observation follow-up, 2026-10-07

Card and List drag/drop node callbacks now retain their identity across ordinary
renders, avoiding a detach/re-register cycle when admission becomes disabled.
Two regressions fail before this correction and pass afterward, while still
checking changed disabled inputs and cleanup on unmount. All 43 selected drag,
move, independent-scroll and virtual-window tests pass; web TypeScript, targeted
lint and the production build also pass.

The first follow-up benchmark failed readiness at 2079.5 ms on desktop and
2094.7 ms on phone. Trace timing showed the qualified second Board response
arriving between default polling checks, followed by nearly a second of observer
delay. The benchmark now observes that response directly through the existing
qualified counter, preserving both required current-screen successful reads,
the enabled drag-handle check and the five-second observation deadline. Four
tracker tests pass, including previous-screen exclusion and timeout/close cleanup.
No timing target is changed or subtracted from the measured duration.

The final complete invocation against the frozen web/compiled Production API,
restricted PostgreSQL and Nginx still fails both scenarios:

| Viewport | Usable Board (<1500 ms) | Drop feedback (<100 ms) | Cached detail (<200 ms) | Mutation p95 (<500 ms) | Outcome |
| --- | --- | --- | --- | --- | --- |
| 1280x844, mouse | 1189.3 | **115.5** | **261.9** | 181.3 | Failed feedback and detail budgets |
| 390x844, Chromium touch | 927.0 | **126.4** | **339.8** | 180.3 | Failed feedback and detail budgets |

Assertions stop at feedback, but the retained report includes cached-detail
measurements and all twenty mutation samples per viewport; those detail values
also exceed their budget. Report provenance remains `unverified runtime` with
no asserted source revision. This Windows-hosted cached-runtime diagnostic does
not prove current retained-image acceptance or a causal timing improvement from
stable registration. Both disposable containers were removed; saved volumes and
the existing three services remain available. Feedback, cached detail and full
release/capacity acceptance remain outstanding.

## Canvas column reuse follow-up, 2026-10-07

BoardScreen now memoizes its derived canvas columns by authoritative snapshot,
qualified filter and Card/List move previews. Opening/closing a dialog or changing
status no longer recreates every column and invalidates virtual-window layout.
The new screen regression fails on column identity before this correction; the
final fixture verifies reuse across dialog state and immediate replacement when
a refreshed server snapshot changes the Card title. Existing provisional placement
and uncertain-result rollback checks still pass. All 73 selected screen, filter,
move-preview and virtual-window tests pass, as do TypeScript, targeted lint and
the fresh production build.

The unchanged full desktop/phone benchmark through the frozen compiled Production
API, restricted PostgreSQL and Nginx still fails both scenarios:

| Viewport | Usable Board (<1500 ms) | Drop feedback (<100 ms) | Cached detail (<200 ms) | Mutation p95 (<500 ms) | Outcome |
| --- | --- | --- | --- | --- | --- |
| 1280x844, mouse | 1125.2 | **120.4** | **275.3** | 210.0 | Failed feedback and detail budgets |
| 390x844, Chromium touch | 962.0 | 92.7 | **336.6** | 175.4 | Failed detail budget |

All twenty mutation samples per viewport are retained. Desktop assertions stop
at feedback; its reported detail measurement also fails the unchanged budget.
This scoped Windows-hosted cached-runtime report has revision null and topology
`unverified runtime`. One timing run does not establish a causal performance
improvement or retained-image acceptance. Both disposable containers are removed,
and existing services and volumes are preserved. Feedback, cached detail and
full release/capacity acceptance remain outstanding.

## Cached editor observation diagnostic, 2026-10-07

The previous desktop/phone traces split detail opening into approximately
69.4/95.4 ms for the click and 172.7/199.0 ms waiting for the enabled title field.
The Card dialog already has zero transition duration. A separate desktop-only
diagnostic observes the actual enabled title input through a DOM MutationObserver,
without changing the standard benchmark, its assertions or the product bundle.
In its completed corrected invocation, the first enabled input appears at 221.2 ms
on the browser clock, the click returns at 61.1 ms on the test clock, and the
original enabled assertion returns at 241.8 ms. The browser observation starts
before the test sends the click, so these clocks are not identical baselines and
their difference is not a precise rendering cost or subtractable timing credit.

This evidence does not justify replacing or relaxing the existing detail budget.
The diagnostic also fails movement feedback at 132.8 ms. It is one instrumented
Windows-hosted desktop/cached-runtime run, not standard two-viewport acceptance
or a current-image release claim. The temporary diagnostic fixture and both
containers are removed; numeric evidence and the diagnostic source are retained
outside the repository. Actual editor readiness and movement feedback remain work.

## Cached detail CPU profile and modal lifecycle guard, 2026-10-07

A desktop-only CPU diagnostic samples cached detail opening at a 100-microsecond
requested interval. Its source-map bundle's executable text matches the frozen
normal bundle after removing the source-map comment. The 868.3 ms captured interval
contains 1,184 samples and includes idle/native/instrumentation time. The largest
mapped self-time source is React DOM at 118.0 ms; MUI transition utilities account
for 16.1 ms. These sampled source times are diagnostic estimates, not component
inclusive render costs or the standard benchmark's timing measurements. Under
profiling, first-enabled observation is 497.7 ms and the original assertion returns
at 536.3 ms; feedback also fails at 109.8 ms. Profiling overhead and the different
clock baselines preclude a timing acceptance or improvement claim.

Installed MUI Fade source performs an initial layout read even when timeout is
zero. A local attempt to skip appearance eliminates that read but fails the added
actual close/focus regression: the modal lifecycle does not retire correctly and
the canvas remains hidden from accessibility queries. That attempt is reverted.
The retained regression opens the cached editor, closes it through the UI and
requires the original canvas link to become accessible and focused again. Future
performance changes must preserve this modal retirement and focus contract.

Temporary profiling source and containers are removed. Private raw CPU/trace data,
the source map and numeric source-timing evidence remain outside the repository.
The standard benchmark and all production dialog/admission behavior are unchanged;
feedback, editor readiness and current-image acceptance remain outstanding.

## Immediate cached-detail transition follow-up, 2026-10-07

Card details now use the same transition state machine underlying MUI Fade,
with zero duration and no animation-related synchronous layout read. MUI Dialog,
its backdrop, modal manager, focus trap and all Card controls remain in place.
The transition forwards the child ref and focus/HTML attributes and normalizes
enter/exit callbacks to their actual DOM node. Appearance still runs the enter
callback; exit still retires the modal before restoring Board focus. The already
locked `react-transition-group` and its types are now explicit web dependencies,
with their existing versions unchanged.

The Board regression fails on the original Fade with two container layout reads.
It now checks opening, closing and reopening in StrictMode, an enabled cached
editor, zero such reads, modal retirement and restored accessible canvas focus.
A separate real-MUI regression checks focus containment, Escape closure and
default opener restoration across two disclosures, preserving MUI's injected
focus handler and tabindex. The final single component-suite invocation passes
all 79 selected transition, Board screen, drag, move and virtual-window tests.
TypeScript, targeted lint and the fresh production build also pass.

The initial unchanged desktop/phone benchmark against the first frozen bundle
still failed. All twenty mutation samples per viewport were retained:

| Viewport | Usable Board (<1500 ms) | Drop feedback (<100 ms) | Cached detail (<200 ms) | Mutation p95 (<500 ms) | Outcome |
| --- | --- | --- | --- | --- | --- |
| 1280x844, mouse | **1614.6** | **108.7** | **244.4** | 230.4 | Failed readiness, feedback and detail budgets |
| 390x844, Chromium touch | 1117.2 | 97.5 | **328.1** | 242.2 | Failed detail budget |

The final frozen bundle's single combined invocation finishes with three scroll
cases passed and both performance cases failed. The desktop/phone mouse and
phone touch scroll cases prove real boundary scrolling, cancellation without a
move write, unchanged canonical persisted Lists/Cards, and keyboard-opened Card
detail closure restoring its canvas link and both scroll offsets. The unchanged
timing scenarios again retain all twenty actual mutation samples per viewport:

| Viewport | Usable Board (<1500 ms) | Drop feedback (<100 ms) | Cached detail (<200 ms) | Mutation p95 (<500 ms) | Outcome |
| --- | --- | --- | --- | --- | --- |
| 1280x844, mouse | 1009.5 | **114.2** | **261.2** | 161.9 | Failed feedback and detail budgets |
| 390x844, Chromium touch | 1073.0 | 86.8 | **266.1** | 190.3 | Failed detail budget |

These are Windows-hosted Chromium/compiled-source diagnostics through restricted
PostgreSQL and Nginx, with revision null and topology `unverified runtime`.
Removing a forced read does not establish a causal latency improvement or meet
the outstanding performance requirements. No metric, threshold, feature control
or admission boundary was relaxed. PRD-06 remains open with **32%** estimated work
remaining, a planning estimate rather than an acceptance score.
Both disposable API/web containers are removed after the terminal invocation,
preserving the original three running services, images and saved volumes.

## Shared input-style follow-up, 2026-10-07

The application theme now gives the root CssBaseline ownership of MUI's
non-empty autofill/cancellation keyframes, preserving standard/WebKit rules and
the existing input animation selectors while disabling per-field global-style
injection. All inputs and feature controls remain mounted as before; this does
not defer part of the Card editor to produce a faster readiness measurement.
The root shared-rule regression fails before this change. Its final version
verifies stable rules across field mount/unmount in StrictMode and filled-label
handling with synthetic animation notifications. All 59 selected theme, shell,
authentication, recovery, editor and focus tests pass, as do web/browser
TypeScript, targeted lint and the production build.

Both new desktop/phone native input-style cases pass against the frozen
production bundle through Nginx. They verify actual stylesheet/computed-style
contracts, label association and autofill/cancellation notifications, with no
additional keyframe rules when registration fields or recovery forms mount.
These notifications exercise the MUI listener and do not simulate a browser
password manager. Shared ownership is documented in the
[web SPA boundary](architecture/web-spa-boundary.md#shared-mui-input-styles).

The subsequent unchanged two-viewport Kanban benchmark still fails both cases,
retaining all twenty actual mutation samples per viewport:

| Viewport | Usable Board (<1500 ms) | Drop feedback (<100 ms) | Cached detail (<200 ms) | Mutation p95 (<500 ms) | Outcome |
| --- | --- | --- | --- | --- | --- |
| 1280x844, mouse | **1559.2** | **109.2** | **287.6** | 181.2 | Failed readiness, feedback and detail budgets |
| 390x844, Chromium touch | 1055.9 | **123.9** | **284.5** | 164.6 | Failed feedback and detail budgets |

This is scoped Windows-hosted Chromium, a compiled Production API under the
restricted PostgreSQL login, and Nginx, with revision null and `unverified runtime`
provenance. Removing duplicate style injection does not prove a causal timing
improvement or current retained-image acceptance. Budgets and sample accounting
are unchanged. PRD-06 remains open with **32%** estimated work remaining, a
planning estimate. Both disposable API/web containers are removed; the original
three services, saved volumes, images and build caches are preserved.

## Current large-Board runtime correction

Exact-image run [37253072119](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37253072119)
at `1655ef8` passed restricted PostgreSQL rank allocation and the complete genuine
image pipeline, then failed both capacity browser cases at their initial List
scroll. Its retained Board response contained 200 Lists and 5,017 active Cards
in List index 194. The adopted MUI CSS-variable theme returns spacing as
`calc(2 * var(--mui-spacing, 8px))`; parsing that as a number produced `NaN`
row positions, so the target List never mounted. BoardWindow now measures the
resolved browser gap and keeps its initial geometry finite. A regression uses
the adopted CSS-variable theme and a resolved 24px gap to scroll to canonical
List 194 with bounded mounted rows. Current native capacity execution remains
pending; this correction does not establish interaction or timing acceptance.


The required release browser benchmark uses Chromium at 1280x844 and 390x844, one browser
worker, exact web/API/Worker images behind Nginx and real PostgreSQL. Normal
conditions are three lists and fifty active cards, one signed-in Organization
owner, warm application assets, and a Board snapshot not previously opened in
that browser. The runner hardware is GitHub's ubuntu-24.04 environment; results
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

The phone case retains the same three Lists, fifty Cards, real cross-List move and twenty alternating keyed mutation samples. It uses Chromium touch dispatch through the actual drag handle, holding at the left canvas boundary until native auto-scroll reveals the original destination. The named destination announcement must identify that target before touch release. Pointer/mouse/touch release starts one browser-clock feedback measurement; persistence remains held until its visual update is observed. Readiness <1500ms, feedback <100ms, cached detail <200ms and mutation p95 <500ms remain unchanged. The desktop case continues to use mouse input. This measures browser touch behavior on the documented runner, without a physical-device claim.

The reporter retains the phone result separately as `normal-phone-kanban`, including the fixed 390x844 viewport and `chromium-touch` input, its original outcome and every mutation sample. It rejects missing/wrong phone input labels, arbitrary viewports, invalid sample sets and inconsistent p95 values; other performance fixtures retain their original desktop-only validation. Private/arbitrary payload fields are stripped. Eight reporter regressions, browser TypeScript and both benchmark scenario collection checks pass locally. Actual phone timing and current immutable-image execution remain pending; adding or collecting a case does not establish its latency acceptance.

The performance reporter now retains a separate `browser-performance-{sha}`
artifact on successful and failed CI runs. It copies only approved numeric
durations, all twenty mutation samples, fixed fixture sizes/conditions, original
budgets, a stable metric label, test outcome and the CI revision into
`artifacts/browser-performance/kanban.json`. Arbitrary attachment fields, test
titles, errors, identities and content are omitted. Invalid/missing measurements
remain absent; unobserved feedback remains explicitly null. A failed test stays
failed in this report. An empty report does not establish performance acceptance.
The privacy/validation regressions run in source CI. Fully green exact-image run
36978417479 at `3e0d033` produced artifact 11216517737 (764-byte ZIP), retained
through 2026-12-31 with SHA-256
`036805b988651f92608270ee05c2a2f42959fb737962b332d6eebdc99d56aba0`.
The inspected schema-1 JSON matches that exact revision and retains all twenty
samples. Both metrics have passed outcomes: normal desktop Board readiness
428.588ms, cached detail 101.521ms, card feedback 45.100ms and mutation p95
28.395ms; the separate empty-List fixture measured List feedback at 31.000ms.
Every original budget passed. The container suite passed 54 browser scenarios
with one conditional skip, plus the separate mobile Worker-delivery case; all
nine CI jobs and immutable release promotion passed. This run predates routing
migration 027 and the new List rename UI, which require their own current proof.

The normal fixture also measures card drop feedback in the browser's monotonic
clock. A captured pointer-up starts the sample; the first frame with the card
in its destination section is followed by a frame boundary before measurement
ends. Its one move request is held before server dispatch until that observation,
so persistence cannot supply the measured update. Feedback must be under 100ms.
The released request must then receive HTTP 200. The attachment includes
feedbackObserved and feedbackMs; an unobserved destination fails the threshold
and records a null duration. This covers normal desktop card drop feedback;
List feedback now has the separate executed evidence below; mobile feedback
still needs executed timing evidence.

The separate desktop list-feedback case uses two empty lists at 1280x844 and
holds the keyed PATCH before dispatch. Pointer release starts a browser-clock
sample ending after a frame boundary with the moved list first and visible.
It requires under 100ms, exactly one write, unchanged canonical order while held,
HTTP 200 with revision two, an unchanged complete neighbor, and persisted order
after reload. The attachment contains fixture conditions and timing only.
This case passed in fully green exact-image runs 36970048153 (`57c9b16`) and
36972597174 (`5d1d894`). Successful runs before the reporter retained the strict
test result but not its numeric attachment, so no exact feedback duration is
claimed for those runs. It does not
provide mobile, large-board or physical-device performance evidence.

The feedback sample also requires viewport intersection on both axes. After
release, the acknowledgment must return the moved ID, intended destination and
revision two. A fresh canonical Board read must place the card immediately before
the selected anchor, preserve that anchor's complete baseline record, and omit
the moved card from its former list. Thus a fast visual sample cannot pass with
an unrelated successful response or a different persisted position. These reads
occur outside the timed sample and do not relax any budgets.

The exact-image Linux browser case passed in run 36963451064 at
5ecf15aa47b017f0fc871b9272bb3d1b08ad7d06. Its retained
kanban-performance.json reports Board readiness 507.43ms, observed optimistic
drop feedback 59.80ms, cached detail 140.90ms and mutation p95 51.07ms across
all twenty samples. These meet the unchanged normal desktop budgets above.
The whole run failed a separate phone list keyboard case, so these measurements
are scoped executed evidence, not a green release claim. Large-board rendering,
mobile timing and actual screen-reader behavior remain required work before
PRD-06 closure.

The required PostgreSQL rank fixture now creates exactly 200 lists through eight
batches of 25 concurrent independent API commands. It requires 200 distinct
ranks, moves 16 lists relative to a current anchor and verifies the other 184
retain their original ranks/revisions. Historical receipt recovery remains
non-reapplying. Card creation retains its separate 128-command concurrency
fixture before SQL populates the existing 5000-card group. Shell syntax/diff
checks pass. Run 36947681754 at f8a7dea reports successful completion of
container-integration job 110654281749 step 33, which directly runs this script.
The checked script at that commit requires all 200-list, unchanged-184-list,
5000-card, concurrent move and non-reapplying receipt assertions above. This is
step-level executed correctness evidence; that run later failed browser checks.
The same fixture passed step 33 in run 36963451064, which also failed a separate
browser check. It does not establish
200-list rendering performance or archived-card capacity.

The required exact-image PostgreSQL fixture also seeds 100000 archived cards
in the same list as the 5000 active cards. Their ranks exceed all active ranks.
The next API append must use the active tail, and the authorized Board snapshot
must return 200 lists and only the 5001 active cards. Concurrent append and
relative moves retain their existing rank assertions. A count and a fingerprint
of every complete archived row must remain unchanged after those moves.
These assertions executed successfully in step 33 of run 36963451064 against
the exact release images and restricted PostgreSQL runtime. This establishes
database correctness at that capacity, not browser rendering or timing evidence
for that large fixture.

The mandatory PRD-12 dated-Board benchmark uses three Lists and 50 dated Cards
created through the exact release API. Only application assets are warm. Usable
readiness includes fresh Board reads, an enabled Card drag control and an
accessible authoritative due-state description after the real profile read,
with the original <1500 ms budget. Cached detail opening retains <200 ms. Twenty
changing date commands use fresh revisions/idempotency keys and retain p95
<500 ms, including response decoding. The scenario attaches all samples and
fixture conditions before budget assertions; no retries or raised budgets apply.
Existing movement feedback benchmarks remain mandatory and unchanged.

The performance reporter retains a separate `normal-desktop-card-dates` metric
in the existing revision-bound artifact, whitelisting numeric measurements and
fixed fixture fields only. It validates all 20 samples and independently checks
p95; invalid topology, undated fixtures or malformed samples produce no entry.
Reporter tests and scenario parsing pass locally. Actual dated-Board timing is
pending exact-image CI and does not establish 200-List/5000-Card rendering or
virtualization acceptance.

## Bounded date formatter reuse

The normal 50-dated-Card regression constructed 300 `Intl.DateTimeFormat`
instances before the repair. Date helpers now share an LRU cache capped at 64
formatter configurations, keyed by locale, timezone and formatting purpose.
The same 50-Card classification workload reuses two configurations. Card values,
account profiles, formatted results and deadline states are never cached; current
clock, precision, completion and timezone validation still drive each result.
Eviction and independent locale/timezone/date-only/timed formatting are covered.

All 82 focused date cases across six suites, web typechecking, targeted lint and
the isolated production build pass. The unchanged dated-Board native benchmark
was executed against frozen before/after web builds with the same Production
API/restricted PostgreSQL topology. Both executions failed the original budgets:

| Local native build | Usable Board (target <1500 ms) | Cached detail (target <200 ms) | Date mutation p95 (target <500 ms) |
| --- | ---: | ---: | ---: |
| Before formatter reuse | 3275.78 ms | 419.79 ms | 171.70 ms |
| After formatter reuse | 2180.30 ms | 368.04 ms | 172.42 ms |

These are single, separate local executions, not a controlled attribution of
latency gains or exact-release evidence. Native commands retain all 20 changing
dates, expected versions, receipts and response-decoding checks. Neither Board
readiness nor detail opening meets its budget. Performance, large-data rendering
and current immutable CI acceptance remain open; PRD-06 stays at **29% estimated
work remaining** and PRD-12 at **25%** (planning estimates).

## Timing instrumentation and retained trace conditions

A local dated-Board rendering diagnostic on the unchanged compiled app found
about 214 ms in JavaScript callbacks, 9 ms in layout and 22 ms in style updates.
These overlapping trace categories are not additive latency measurements. A
separate sampled CPU profile identified Playwright accessibility snapshot
traversal alongside React/MUI work. A source-map diagnostic build had identical
minified application code after removing its map comment; it was not deployed.
Neither instrumented diagnostic proves a normal-condition latency budget.

The four timing fixture files now retain command/network traces on failure
without per-action DOM/accessibility snapshot serialization or trace screenshots.
The independently configured failure screenshot remains enabled. Ordinary
functional/accessibility scenarios keep their existing full traces. All setup,
commands, current admission, source-version, receipt, placement, paging and
canonical-state assertions remain; no threshold, retry count or timeout changed.
Cached Card-title checks identify the named Card-details dialog before its enabled
textbox. The same timing begins before the actual link click and ends only after
that authoritative editable control is enabled.

Each performance attachment declares `commands-and-network` tracing. The
privacy-safe reporter retains that fixed condition, accepts explicit historical
`dom-snapshots`, rejects unknown or malformed conditions, and reports absent
historical instrumentation as null. It preserves failed outcomes, every mutation
sample and all original budgets. Two new failing-before reporter cases now pass
with all 13 reporter regressions; browser typechecking passes.

The first dated-Board execution without trace snapshots measured 962.21 ms usable
Board, 218.52 ms detail and 82.50 ms mutation p95. Scoping the editable title to its
actual Card dialog then measured 945.64/201.16/75.25 ms. Both executions still
failed the unchanged <200 ms detail budget. These are separate local measurements,
not current immutable release evidence or proof of full performance acceptance.

The final three-case native invocation ended with two passes and one setup
failure. Both complete desktop workflows pass with the declared trace condition:

| Final local native fixture | Usable Board | Cached detail | Painted move feedback | Mutation p95 |
| --- | ---: | ---: | ---: | ---: |
| 50 dated Cards, desktop | 969.30 ms | 171.30 ms | Not part of this fixture | 89.46 ms |
| 50 Cards, desktop Kanban | 699.08 ms | 171.73 ms | 98.90 ms | 158.11 ms |

The phone Kanban case failed during its untimed `/app` asset-warming navigation
with Chromium `net::ERR_NO_BUFFER_SPACE`; it produced no latency samples and no
retained performance entry. This is missing mobile evidence, not a passing or
failed timing sample. Existing held-write/canonical-position/revision/reload and
twenty real changing mutation assertions remain enforced in the passing desktop
case. The reporter labels this execution `unverified runtime`; local compiled
Production API/restricted PostgreSQL execution does not prove retained release
images. Checklist and List-feedback fixtures have source/typechecking coverage
for the new trace condition but were not rerun in this increment. Full mobile,
large-data and current immutable CI acceptance remain open. PRD-06 remains at
**29%** and PRD-12 at **25% estimated work remaining** (planning estimates).

## Current mobile evidence and canonical Card lookup

At `b96b2cb5`, a complete three-case native invocation passed mobile Kanban and
phone List feedback, while Checklist detail opening failed its original budget.
The original Chromium buffer error did not recur in this invocation:

| Fixture before Card lookup refactor | Outcome | Usable Board | Cached detail | Painted feedback | Mutation p95 |
| --- | --- | ---: | ---: | ---: | ---: |
| Phone Kanban, 50 Cards | Passed | 656.93 ms | 147.34 ms | 60.30 ms | 116.05 ms |
| Phone List drop, two empty Lists | Passed | Not sampled | Not sampled | 50.50 ms | Not sampled |
| Desktop Checklist, 50 Cards/63 items | Failed | 948.65 ms | 209.22 ms | 43.70 ms | 99.42 ms |

The phone workflows retain touch activation, actual viewport intersection, one
held keyed write, unchanged canonical placement while held, intended acknowledgment,
unchanged neighbor and persisted order after reload. Checklist reads retain the
50/13 item pages, full 63-item progress and twenty changing revisioned commands.

Board detail now resolves its selected Card and parent List once per canonical
snapshot/selection. The lookup stops when found instead of flattening all Card
collections; ten repeated lifecycle checks and the date-editor parent identity
reuse that same location. Access, active Board/List, busy/recovery and protected
read flags keep their existing semantics. No formatted result, account authority
or future snapshot is cached. The extracted original flattening strategy failed
the new test because it materialized a later 5,000-Card collection after finding
the requested Card. Three lookup cases cover early termination, changed canonical
parent/revision, archive and missing selection/scope. All 70 focused Board/editor/
drag/date cases pass. A strengthened three-case integration suite also passes,
showing unrelated dialog state reuses the lookup and fresh snapshots recompute it.
Typechecking, targeted lint and the isolated production build pass.

The complete unchanged Checklist benchmark on the new compiled build still
failed: usable 952.56 ms, detail **201.74 ms**, creation feedback 40.60 ms and
mutation p95 100.80 ms. Item pages measured 122.49/125.38 ms. The <200 ms detail
budget remains unmet; no retry or threshold change was made. The older passing
phone measurements above are not new-build or immutable-image proof. All native
results use compiled local Production API/restricted PostgreSQL, with Checklist
delivery through a separate scoped Worker; the reporter marks this topology
unverified. Temporary owned services were removed after execution. Full current
immutable/mobile/large-data acceptance remains required. Estimated work remaining
stays **29% for PRD-06** and **35% for PRD-13** (planning estimates).

## Phone List feedback coverage

The two-empty-List feedback fixture now also runs at 390x844 with Chromium touch input. It reveals the moving List, activates its actual handle, and uses the left canvas boundary to auto-scroll until the anchor center is reachable. Touch release starts the same browser-clock sample used by the desktop pointer case. The first painted optimistic order must intersect the viewport and meet the unchanged <100ms budget while the keyed PATCH is held before dispatch. The fixture requires exactly one write, unchanged canonical ordering while held, the intended acknowledgment at revision two, an unchanged neighboring List, and persisted order after reload. Touch cancellation and session cleanup run on failure.

The reporter retains this result as `phone-list-feedback`, with fixed touch input and viewport, original outcome and the 100ms budget. It rejects phone records without touch input, preserves missing feedback as null, and strips private or arbitrary fields. All nine reporter regressions and browser typechecking pass locally. Native execution against the exact release images remains pending; fixture coverage does not prove the mobile latency target or physical-device behavior.


## Current Card backdrop and desktop Checklist result

The Card Dialog now applies its existing zero-duration immediate transition to
its MUI Backdrop as well as its content. MUI Fade's unconditional scroll-position
layout read was still present on the backdrop; a new regression failed with one
read before repair and passes with zero afterward. Native MUI containment/closure
and the existing return-focus lifecycle are preserved.

The complete unchanged desktop Checklist benchmark failed at 206.08 ms cached
detail before this repair and passed at 149.41 ms afterward. Both runs completed
all original functional steps and twenty mutation samples. The passing run also
measured readiness 957.96 ms, feedback 44.70 ms, mutation p95 98.13 ms and 50/13 item
pages at 127.44/120.35 ms. Two transition cases, two selected Board draft/focus/Card
switching cases, types, lint and the production build pass. These independent
local measurements do not estimate a controlled speedup or establish phone,
large-Board or current immutable-release acceptance.

The privacy-safe reporter explicitly marks the runtime unverified. The readonly
compiled API, current Nginx/CSP, restricted schema-111 PostgreSQL copy and separate
fixture-scoped compiled Worker were real; owned test containers/database were
removed afterward. See the [full acceptance evidence](architecture/checklist-acceptance.md#cached-detail-backdrop-layout-repair).
Estimated work remaining is 29% for PRD-06 (unchanged) and 33% for PRD-13 (planning
estimates). Release-wide acceptance still prevents closure.
