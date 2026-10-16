# PRD-13 acceptance audit

The authoritative requirements are GitHub issue #14. This audit preserves the
full ticket scope; implementing command controls is not sufficient for closure.
It includes the source, interaction and measurement coverage added after item ordering.

## Executed release-candidate evidence (2026-10-15)

Candidate `4cb80014a9b9e863cdc68c9718e510886cade78b`,
[run 38674444706](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/38674444706),
passes all 16 jobs, including build-once images, seven integration executions,
security, required-CI and release assembly. This audit inspects actual completed
job logs rather than inferring Checklist coverage from the green run.

All **21 original Checklist case titles across ten files** match individual
passing log entries. The ten files are unchanged between this candidate and
the audit's parent revision `5845da8b`. Collection identifies the expected cases;
the matching completed job lines establish execution. The candidate predates
later shared deployment/readiness repairs, so it does not certify current-main
images or the complete MVP milestone. Historical investigations below retain
their original evidence boundaries; this section and the matrix supersede
their statements that the original browser cases have never executed.

| Executed scope | Completed exact-image evidence |
| --- | --- |
| Reads, empty/full progress, Checklist create/rename and live two-session refresh | [checklist-reads.spec.ts](../../tests/browser/checklist-reads.spec.ts), desktop/phone, two passes |
| Checklist order and confirmed deletion with exact original-key recovery | [position](../../tests/browser/checklist-position.spec.ts) and [deletion](../../tests/browser/checklist-delete.spec.ts), four passes |
| Item create/edit/complete/uncomplete/order/delete, completion attribution and live progress | [create](../../tests/browser/checklist-item-create.spec.ts), [edit](../../tests/browser/checklist-item-edit.spec.ts), [position](../../tests/browser/checklist-item-position.spec.ts), [delete](../../tests/browser/checklist-item-delete.spec.ts), eight passes |
| Concurrent drafts/server conflict, real socket outage/reconnect, committed lost reply and revoked access | [collaboration](../../tests/browser/checklist-collaboration.spec.ts), two passes |
| Pending rename across Card/List archive, restore and deletion | [parent lifecycle](../../tests/browser/checklist-parent-lifecycle.spec.ts), four passes |
| Small normal-condition feedback, paged items and latency budgets | [performance](../../tests/browser/checklist-performance.spec.ts), one pass |

The 20 functional cases pass in
[full shard 2, job 116241955454](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/38674444706/job/116241955454).
That shard reports 96 passed and two conditional skips; none of the 20 Checklist
cases is skipped. The normal benchmark passes in
[full shard 4, job 116241955348](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/38674444706/job/116241955348).
The commands job additionally reports canonical creation/reads/progress,
original receipt recovery, atomic rollback, rank exhaustion and lifecycle
admission passed. The PostgreSQL job reports forced tenant RLS, composite
parent integrity, ordering, attribution/timestamps, tombstone progress and
rollback passed. Those broad contract summaries do not independently establish
every unnumbered PRD requirement.

The [retained normal-performance artifact](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/38674444706/artifacts/11956055514)
is independently checked in memory: **2,318 bytes**, SHA-256
`bfb11228db454a1b86886d0201580e77515a17f6b8c3e999e343946a4b7ba094`,
with embedded metadata and measurements bound to this exact SHA/run. Its
Checklist fixture has three Lists, 50 Cards, two Checklists, 63 items and 20
actual mutation samples; it is a small normal-condition benchmark, not a
large-load test. All original budgets pass:

| Measurement | Result | Budget |
| --- | ---: | ---: |
| Usable Board | 571.882 ms | <1,500 ms |
| Cached detail, browser-painted editable | 117.300 ms | <200 ms |
| Pending creation feedback | 51.800 ms | <100 ms |
| Mutation acknowledgment p95, 20 samples | 55.305 ms | <500 ms |

The first and second item-page observations are 107.019 and 100.997 ms, with
the original 50/13 seek-page assertions. No additional page budget is invented.
The 20 samples independently reproduce the reported p95. Large capacity,
full deployed telemetry and current-main release acceptance remain outstanding.
The Checklist-specific Board-context gap identified by this audit is addressed
in the separate scoped verification below. PRD-13 stays open at
**33% estimated work remaining**, unchanged planning estimate. All 27 named
capacity stages remain deferred; no new browser, container or load test is run
by this evidence audit and no Docker resource is created.

## Checklist-specific Board context verification (2026-10-15)

The earlier shared Kanban scroll case closes Card details without a Checklist
operation. It does not establish the Checklist-specific context requirement.
The new [context scenarios](../../tests/browser/checklist-board-context.spec.ts)
use six Lists and 12 Cards at desktop 1280px and phone 390px. Each starts with
nonzero horizontal Board and vertical List offsets, opens the selected Card by
keyboard, edits/completes one existing item through its actual MUI controls,
and closes details by keyboard. It requires the original Card link to regain
focus, both exact offsets to survive, the Board URL to return and only one
document navigation. Exactly one item PATCH and zero Card-move POSTs are allowed.

The canonical read verifies the edited text, item identity/completion actor and
time, Card/Checklist/item versions 4/3/2, and one retained item. The final complete
List/Card graph must equal the original except for that Card's expected version
and update time; order, identity and other content cannot change. Tagged Axe and
viewport-overflow checks pass. No product behavior, existing assertion or
deadline is relaxed; read-only opening uses the existing bounded helper and the
save has one activation. The original 21 candidate cases remain unchanged.

The complete new file passes **2/2 in 93,642.659 ms**, one attempt per case,
zero skipped/flaky/unexpected outcomes or report errors. Browser types and all
244 CI coverage guards pass. Collection assigns all **400** cases exactly once
across the four full shards (103/98/99/100), including these two; collection is
not execution proof. The ordinary native capture uses archive-verified retained
`24f5624` images, fresh PostgreSQL/pgvector with 149 migrations, restricted
API/Worker roles and an actually verified disposable account for the scoped
Worker. Current Nginx configuration is mounted read-only. API verification is
optional and mail disabled; the Worker retains its verified-account policy.

All 173 captured browser inputs and three web configuration hashes match.
Original image IDs are checked and the existing database is unused. Owned
cleanup restores full Docker inventory and removes **1,716,497 temporary bytes**.
No images are built, downloaded or imported, and no large-load test runs.
These cases supply supporting functional context proof, not current-main
immutable-image or complete WCAG/provider/release certification. That CI,
post-MVP capacity and full deployed telemetry remain required. PRD-13 remains
open at **33% estimated work remaining**, unchanged planning estimate.

## Functional requirements

| Requirement | Current implementation | Evidence and remaining verification |
| --- | --- | --- |
| CHECK-FR-001/002 | Multiple persisted ordered Checklists, stable scoped identity/title/rank/time/version | Candidate `4cb80014` passes canonical command/storage contracts and desktop/phone reads/position cases above. Current-main complete release acceptance remains required. |
| CHECK-FR-003 | Ordered scoped items with completion actor/time | Candidate item-edit cases check `completedBy`, `completedAt`, retention through rename and nulling on uncompletion; ordering and command/storage contracts pass. Complete current release/permission coverage remains required. |
| CHECK-FR-004 | Checklist create/rename/position/admin-confirmed cascade delete | Candidate desktop/phone read/create/rename, positioning and confirmed-deletion/original-receipt cases pass. Current-main full CI remains required. |
| CHECK-FR-005 | Item create/edit/complete/uncomplete/position/admin-confirmed delete | All eight candidate desktop/phone item command cases pass, including exact committed/lost-response recovery and live peers. Current-main full CI remains required. |
| CHECK-FR-006/007 | Progress uses all active items independent of the current page; empty is 0% | Candidate two-session/empty-progress cases and 63-item/50+13 seek-page benchmark pass. Supported large-capacity acceptance is explicitly deferred until post-MVP. |
| CHECK-FR-008 | No item member assignment or due fields | Domain/schema/API shapes and item editors; item completion remains independent of Card due completion |

## Interaction and scenario coverage

| Scenario | Current evidence | What is still required |
| --- | --- | --- |
| TC-01/02 primary/empty | Candidate exact-image desktop/phone read/create/rename/empty-progress cases pass | Revalidate against current-main immutable images and remaining full-ticket scope |
| TC-03/04 input/authorization | Domain validation, host permission/tenant/public-read cases, image RLS and post-wait visibility checks | Confirm current exact-image release gate; preserve disclosure-before-validation fencing |
| TC-05 permission changes | Candidate desktop/phone collaboration passes dirty-draft withdrawal, unresolved committed item recovery, revoked membership, refused reads/new writes/original-key replay and unchanged owner-visible state | Current-main complete CI and remaining role/visibility matrices |
| TC-06/07 timeout/idempotency | Candidate exact-image browser cases pass original actor/body/key/revision recovery after real committed/lost responses across Checklist/item commands | Current-main immutable CI and complete retry/expiry/permission acceptance |
| TC-08 concurrent clients | Candidate desktop/phone collaboration passes a held submitted request, actual competing owner change, server 409, retained dirty choices and explicit discard/current canonical review | Current-main immutable CI and remaining concurrency matrices |
| TC-09 disconnect recovery | Candidate collaboration passes real proxied socket closure, rejected reconnects, missed completion recovery and a later live change without page navigation | Current-main immutable CI and complete transport/role acceptance |
| TC-10 lifecycle | Four candidate Card/List desktop/phone cases pass committed pending rename, archive/restore/replay fencing and deleted-scope withdrawal | Current-main immutable CI and shared PRD-18 retention acceptance |
| TC-11/12 keyboard/mobile | Candidate 1280/390px cases execute keyboard/focus and tagged Axe checks; the separate retained-image context cases below pass actual item editing, unchanged Board scroll, restored Card focus and one-document navigation | New context cases still require current-main immutable CI; broader WCAG/current-release acceptance remains required |
| TC-13 large data/performance | Candidate normal benchmark passes 50/13 item pages/full progress, 50-Card readiness, cached detail, immediate creation feedback and 20 actual completion mutations; retained measurements independently checked above | Full 200-List/5,000-active/100,000-archived capacity acceptance is explicitly post-MVP. Large client behavior and current-main acceptance remain required; small context proof below is not large-board evidence. Normal 1500/200/100/500 ms budgets are unchanged. |

## Events, telemetry and shared dependencies

Application commands emit the required Checklist/item event types through the
existing transactional outbox and per-child append-only audit. Card aggregate
versions invalidate authorized Board readers; raw Checklist text is not in work
events. Existing image fixtures cover late audit/event/queue failures and
transactional rollback. These are source/host/image facts, not proof that all new
client interactions satisfy realtime acceptance.

`BoardSharingTelemetry` maps all Checklist read/command routes to fixed operator
request counters and duration histograms, with bounded outcome/error-code/keyed-
attempt labels. It excludes tenant/object/actor IDs, content, raw URLs and keys.
HTTP reads and keyed attempts do not prove client feature opens or user-visible
retries. Production client controls now report fixed open/use/exception/retry,
conflict, validated result/timing and completed transport-recovery observations
through the bounded authenticated aggregate endpoint. The API has an optional
configured OTLP export path. Deployed collection, dashboards, render-exception
coverage and complete telemetry acceptance remain incomplete, as described by
the shared telemetry documents. Audit remains authoritative for business history.

The ticket also depends on PRD-08/22 and cross-PRD lifecycle/copy behavior.
The candidate execution above supersedes the original pending-browser status;
full current-main release acceptance and the remaining complete PRD requirements
are still unproven. No PRD-13 closure is claimed. Historical evidence below must
be read at its captured revision rather than as current release certification.

The Checklist benchmark holds creation before server submission and measures the
first rendered pending status/disabled submit control from the captured submit
event. This measures immediate feedback, not optimistic server success. It then
allows the actual request and checks its canonical revision. Twenty alternating
completion commands must each change Card/Checklist/item revisions; p95 includes
real HTTP and acknowledgment parsing. Item-page latency is recorded separately
without inventing a ticket budget. Retained evidence includes only fixed fixture
sizes, timings, sample arrays, budgets and executed status, never object IDs or
Checklist text. Seven reporter tests and ten creation-control tests pass locally;
the benchmark has now executed successfully at candidate `4cb80014`, as recorded
above. Capacity, complete telemetry and current-main release acceptance remain open.

`scripts/ci/test-checklist-capacity.sh` is a separate mandatory container stage
using the already-built release API/PostgreSQL/Nginx topology. It creates an
isolated authenticated owner/Organization/Board through API, then seeds canonical
disposable scale rows directly in PostgreSQL. Seeding is not audited-command
acceptance evidence. It asserts all 200 Lists and 5000 active Cards in the Board
snapshot, 100000 archived Cards in the Organization, bounded first/second/final
archive seek pages, 50/13 Checklist item pages, whole-Checklist progress,
unchanged protected/audit/event/receipt state after reads and one actual
versioned completion. Fixed-scope timings and results are retained for 90 days
with the exact commit; IDs, titles, keys and credentials are excluded. Shell
syntax passed locally. Docker is unavailable locally, so capacity execution and
measurements remain pending CI. This API fixture does not prove large client
rendering, normal UI latency, production capacity or telemetry acceptance.

Checklist client observations now cover disclosure, validated reads, mutation
outcomes/timings, explicit receipt retries and caught exception/conflict categories
through a bounded authenticated/CSRF-protected aggregate endpoint. See
`docs/kanban-telemetry.md` for protocol and remaining operator/realtime/render
coverage. This partial instrumentation does not close PRD-13. Latest source run
37105725659 at cefeef3 passed .NET/PostgreSQL but failed one Checklist rename
recovery callback assertion (864 web tests passed). The assertion now awaits the
existing React effect; its expected value and exact original-body/key checks are
unchanged. Image/capacity/security stages were skipped in that failed run.

Capacity run 37105533441 at 55da719 passed source, image-build and security gates,
but container job 111154138044 failed after the capacity fixture assertions at
JSON evidence generation: jq requires parentheses around arithmetic object
values. The uploaded evidence artifact was consequently empty and proves no
successful capacity stage. Commit 3962bbf repairs those expressions and generates
into scratch before publishing the final file. The exact expression now produces
valid JSON and expected millisecond conversion locally; shell syntax also passes.
The mandatory capacity/browser/release gates remain required and unproven.
Full local web regression after client instrumentation and callback repair: 869 passed across 66 files; zero failures. This is local web evidence, not Linux host/image acceptance.

Completed transport-recovery observation is now explicit in the existing Board
stream, distinct from ordinary pending/reset delivery states. Checklist disclosure
reports it only while open, with no identity/scope/event fields. Focused stream
and disclosure regression: 21 passed. This follows the 869-test full-suite result
above; executed latest-image proof remains pending CI. Collector/export/dashboard
and render-exception coverage still prevent full telemetry acceptance.

Linux run 37107129291 at b99c988 subsequently passed source-quality/web/.NET/
PostgreSQL gates. Job 111157751220 executed 246 Domain and 249 API-host tests,
including client batch/authentication/CSRF/bounds/listener/rate-limit regressions.
Images are still building; no successful full release acceptance is claimed.
Latest focused Board screen/disclosure/stream regression: 48 tests passed across four files. Typecheck, lint and production build passed after the reconnect change.

Configured OTLP export now connects the two existing API meters to an optional
operator receiver, using only fixed service/build resource metadata. Eight new
configuration/transport/exclusion/outage tests compile; see the operator metrics
document for pending deployed collector/dashboard proof. Run 37106945461 at
3962bbf failed a separate definite-conflict recovery callback assertion before
React's effect; the unchanged true assertion now awaits that effect. All 42
Checklist manager tests pass locally. Source gates at 5d592d1 passed; its
container/security stages are live. No release acceptance or closure is claimed.

Run 37110958673 at bfa4ba5 passed PostgreSQL and .NET but failed the Checklist
create original-intent retry fixture's recovery callback assertion immediately
after the retry control appeared (871 web cases passed). The callback is delivered
by a React effect, so the same true/false assertions now await delivery. Exact
body/key, scope re-admission, newer version, telemetry and actual POST count
assertions remain intact. No timeout increase or production retry change is made.
Focused execution is pending.

All 10 focused Checklist create cases pass after awaiting the same recovery
callback assertions. The retry case still proves two actual commands with
identical original body/key across newer snapshot and temporary re-admission.

Executed exact-image capacity evidence at e9b5d15, run 37109396890/container job
111165055788, retained artifact 11269995985: 200 Lists, 5,000 active and 100,000
archived Cards, 63 Checklist items/page size 50. Archive pages 50/50/1, item pages
50/13, progress 31->32 after a versioned completion, and unchanged state on reads
all passed. Measured Board read 61.858 ms, first/last archive 18.844/18.389 ms,
first item read 33.25 ms in this CI environment. These are retained measurements,
not production latency guarantees or the browser budgets. The downloaded JSON
matches the exact revision and passed state; the previous empty jq artifact is
not used as evidence. The same image's pinned Collector config and ingestion
checks passed with all six fixed/privacy flags true in artifact 11269482094.
Actual browser/keyboard/recovery/capacity budgets and all remaining full-ticket
requirements still govern closure; the run is still in its browser stage.

## Browser regression repair evidence

Run 37139663138 completed with failures in both checklist read widths and feedback observation. A fully parsed, same-scope checklist page with a newer Card revision now requests a bounded parent re-admission and withholds the mismatched contents. Repeating the same mismatch across temporary access refresh does not create an automatic refresh loop; older and foreign pages do not invalidate the current parent. Component cases verify recovery after the authoritative parent revision advances and refusal of older/foreign pages. The browser feedback observer now follows the actual form submission on the stable document across form replacement and requires immediate busy status plus a disabled or removed resubmit control. The original 100 ms feedback, 200 ms detail, 1500 ms usability and 500 ms mutation p95 budgets remain enforced. Local full web validation passes 959 tests in 73 files plus typecheck/lint/build. Updated browser scenarios are discovered but their repaired exact-image execution is pending; PRD-13 remains open.

Archived Card detail now mounts the existing scope/revision-fenced checklist disclosure. Retained titles, progress and item text remain readable without child mutation controls. The Card/List archive browser fixture now opens those disclosures before restore and still verifies deletion purges retained content. Focused archived/checklist/attachment component validation passes 28 cases with typecheck and lint. Browser execution remains pending.

## Current Checklist benchmark and Card lookup evidence

The full normal Checklist browser case now executed twice against compiled local
Production API/restricted PostgreSQL and the separate scoped delivery Worker.
Both executions passed 50/13 scoped item pages, full 63-item progress, actual held
creation/busy feedback and twenty real revisioned completion commands. Both failed
the unchanged <200 ms cached-detail budget: 209.22 ms before and 201.74 ms after
the canonical Card lookup refactor. Final Board readiness was 952.56 ms, creation
feedback 40.60 ms and mutation p95 100.80 ms; first/next item pages 122.49/125.38 ms.
These independent single runs do not establish a controlled latency improvement.

The refactor avoids flattening all Cards and reuses one memoized canonical
Card/List location across existing controls. The extracted old lookup strategy
fails a new early-termination regression; all 70 focused source cases and the
strengthened three-case integration suite pass, plus types/lint/build. Existing
admission, revision, active-parent and recovery flags remain. See the
[full measurements and scope limits](../kanban-performance.md#current-mobile-evidence-and-canonical-card-lookup).
Local execution is not current exact-image acceptance; the reporter marks the
runtime unverified. PRD-13 remains open at **35% estimated work remaining**
(planning estimate), including detail latency and remaining full-ticket evidence.


## Current desktop and phone functional execution

The complete 21-case local invocation on 2026-10-07 ran all 20 Checklist functional
cases and the overlapping watch-notification case. It finished with **19 passes
and two failures** in 15 minutes: desktop Checklist collaboration failed while
reopening an item after conflict discard, and watch setup failed before the Board
subscription's subscribed state appeared. The phone collaboration case and the
other 18 Checklist cases passed. These failures are retained; the invocation is
not claimed green.

The desktop trace showed that the expected canonical item editor never appeared
and the review control remained. The collaboration fixture now requires the
actual admitted Card revision 4 and a settled protected Board after discard, then
uses the shared keyboard admission helper for each independent review control.
It retries enabled focus only and sends one activation key. The watch fixture
uses the same helper and additionally verifies each real subscription's HTTP 200,
watching state and revision 1 before proceeding. Production admission, command
policy, timeouts, retries and existing scenario assertions are unchanged.

A complete targeted three-case rerun passes in **2.3 minutes**: desktop and 390px
Checklist collaboration plus overlapping watch delivery. Together, the two
invocations provide scoped local passes for every one of the **20 distinct
Checklist functional cases**: reads, root/item ordering, item creation/edit/
completion/deletion, root deletion, concurrent dirty choices, actual HTTP 409,
missed updates during a proxied socket outage, restored live updates, lost committed
acknowledgments, revoked access and original-receipt refusal, and Card/List archive/
restore/delete at both widths. Existing keyboard, focus, accessibility, canonical
version, exact request and no-disclosure assertions remain. This is not a single
green full invocation or performance-suite execution.

Runtime: readonly compiled Production API from `5e7e1f5a`, the production web
bundle containing the current canonical Card lookup, actual PostgreSQL 17/pgvector
schema 110 and restricted roles, Nginx/CSP, and the separate existing compiled
Worker scoped to each fixture Organization. Optional email verification matches
the CI functional browser phase. Current immutable release verification is still
required. The temporary API/web/Worker containers were removed after terminal
checks, preserving the original three containers and their data.

Browser TypeScript and diff checks pass after the fixture repairs. The previous
unchanged Checklist benchmark's **201.74 ms** cached-detail result still fails its
original <200 ms budget; it is not replaced by functional passes. Large client
capacity, current release-wide acceptance and shared lifecycle/telemetry gaps
still govern closure. Estimated PRD-13 work remaining stays **35%** (planning
estimate); the ticket remains open.

## Cached-detail backdrop layout repair

The unchanged complete desktop Checklist benchmark reproduced the cached-detail
failure against the current compiled API/schema 111: **206.08 ms**, with all 63
items, 50/13 pages and twenty mutation commands completed. Board readiness was
1,034.90 ms, feedback 40.70 ms and mutation p95 100.60 ms. This failed report is
retained alongside the earlier 201.74 ms measurement.

Source inspection confirmed that the Card Dialog's content used the existing
immediate transition, but its zero-duration MUI Backdrop still used Fade. Fade
unconditionally reads `scrollTop` to force layout even with a zero timeout. The
new regression failed with one backdrop scroll-position read before the repair.
The Card Dialog now uses the same immediate transition for its backdrop. MUI
Modal, backdrop styling, focus containment, close handling and the existing
manual return-focus lifecycle remain in place; no benchmark timing point, limit,
fixture size, request policy or production authorization was changed.

After the repair, both immediate-transition cases and two selected Board
dirty-draft/focus/Card-switching cases passed. Types, focused lint and production
web build passed. The entire unchanged Checklist benchmark then passed: cached
detail **149.41 ms**, Board readiness **957.96 ms**, feedback **44.70 ms**, mutation
p95 **98.13 ms**, item pages **127.44/120.35 ms**. All 20 mutation samples, full
progress, actual held creation feedback and version assertions completed. The
privacy-safe report's sample/p95 calculation, original budgets, fixed fixture and
unverified-runtime provenance were independently checked. These are independent
local runs, not a controlled estimate of the transition's speedup.

Runtime: readonly API published from `a9d107f7`, repaired production web bundle,
separate existing compiled Worker scoped to the fixture Organization, current
Nginx/CSP and restricted schema-111 PostgreSQL. A disposable database copy hosted
the fixture. It and all owned test containers were removed after terminal
execution; the three original containers/database were preserved. Local normal
desktop acceptance now passes, but current immutable-image release acceptance,
large client capacity and the other full-ticket gaps still prevent closure.
Estimated PRD-13 work remaining is **33%** (planning estimate).


## Current complete collaboration baseline, 2026-10-10

The [unchanged two-client desktop/phone file](../../tests/browser/checklist-collaboration.spec.ts)
passes **2/2**, one attempt per case, zero skips/flaky/unexpected/report errors,
in 105,148.847 ms on compiled schema-133 Production API/separate scoped Worker,
fresh restricted PostgreSQL and current long-label frontend. Both real clients,
submitted server-409 conflict, retained draft/completion, revision-4 reopen,
actual socket outage/reconnect and recovered completion, committed/lost-response
item mutation, revoked membership, denied original-key receipt/new writes,
exact unchanged owner-visible canonical state and tagged Axe remain unchanged.
Owned containers, database and credential environments are independently absent.
This optional-verification local baseline does not reproduce the old immutable
phone failure or prove its cause. Full command/lifecycle/capacity/performance,
current immutable-image and all original PRD acceptance remain required.
Estimated PRD-13 work remaining stays **33%** (planning estimate); issue stays open.
See [retained CI investigation](browser-recovery-ci.md#retained-immutable-image-run-37999375942-2026-10-10-inspection).

## Current complete nested review recovery

The [review admission verification](checklist-review-admission-verification.md)
records a current complete baseline with one missing editor and the repaired
complete desktop/mobile invocation. The five-second read-only opening loop now
checks the final canonical edit field and can reload only an explicitly rejected,
unsubmitted item read. All original dirty-draft, completion, server conflict,
socket/revision, committed receipt, revoked-access, backing-state and accessibility
requirements remain, as do application version/access guards and the scenario
deadline. All 75 management component cases and 213 CI coverage guards pass;
source/runtime/web hashes and owned cleanup are independently checked. Current
immutable/full CI and the remaining full AC/DoD still govern closure. Estimated
PRD-13 work remaining stays **33%**.

The [explicit discard follow-up](checklist-review-admission-verification.md#observe-explicit-discard-before-independent-reopening)
addresses the later retained desktop reconnect failure while keeping the strict
canonical editor-value assertion. Both explicit discard points must remove the
old editor before an independent opening and new typing. Complete desktop/phone
collaboration scenarios pass **2/2 in 133,758.085 ms**, with original conflict,
preserved text/completion, socket recovery, versions, committed receipt,
revocation, unchanged state, accessibility and deadline checks. Captured inputs
match during execution and owned fixtures are removed. Current immutable/full
acceptance remains pending; estimated work remaining stays **33%**.

The [cached-detail assertion snapshot repair](../kanban-performance.md#cached-detail-assertion-snapshot-isolation-2026-10-16)
awaits the existing browser-painted observation before enabled-title assertion
polling can build a document-wide accessibility failure snapshot during the timed
opening. The exact title/focus/enabled/viewport/paint criteria, 200 ms budget,
all 63 items, 50/13 pages, held-command feedback and 20 mutation samples remain.
The unchanged local baseline passes at 162.5 ms; a separate CPU-instrumented
318.9 ms failure identifies observer work and remains retained as a diagnostic
failure. Final normal execution passes the complete four-scenario shared group,
with Checklist detail 107.0 ms, feedback 44.6 ms and mutation p95 79.449 ms.
Nine observer checks, browser types and 289 CI guards pass. Native inputs match
and owned cleanup restores Docker inventory. This is compiled-source evidence,
not current immutable/full CI or proof of the hosted failure's exclusive cause.
PRD-13 remains open at **33% estimated work remaining**; large-load tests stay
post-MVP.
