# Kanban performance acceptance

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

## Phone List feedback coverage

The two-empty-List feedback fixture now also runs at 390x844 with Chromium touch input. It reveals the moving List, activates its actual handle, and uses the left canvas boundary to auto-scroll until the anchor center is reachable. Touch release starts the same browser-clock sample used by the desktop pointer case. The first painted optimistic order must intersect the viewport and meet the unchanged <100ms budget while the keyed PATCH is held before dispatch. The fixture requires exactly one write, unchanged canonical ordering while held, the intended acknowledgment at revision two, an unchanged neighboring List, and persisted order after reload. Touch cancellation and session cleanup run on failure.

The reporter retains this result as `phone-list-feedback`, with fixed touch input and viewport, original outcome and the 100ms budget. It rejects phone records without touch input, preserves missing feedback as null, and strips private or arbitrary fields. All nine reporter regressions and browser typechecking pass locally. Native execution against the exact release images remains pending; fixture coverage does not prove the mobile latency target or physical-device behavior.
