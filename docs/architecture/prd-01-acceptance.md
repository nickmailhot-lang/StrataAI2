# PRD-01 foundation acceptance and closure audit

The [mutable profile receipt clock review](schema149-clock-inventory.md#mutable-profile-receipt-clocks--2026-10-16)
separates account creation/mutation time from an acknowledgment generation's
publication/update/24-hour expiry. Required persistence assertions now verify
actual publication windows, immutable replay clocks/expiry, expired-key version-3
replacement, canonical source/audit counts, unchanged session and preserved user
creation. The native positive contract passes; a deliberately stale replacement
clock is rejected by the exact new guard in a separate negative probe. Both runs
preserve captured source/binaries and original Docker inventory. Production stores
already implement the reviewed semantics and remain unchanged; compilation and
all 285 CI guards pass. Historical/other-writer and current immutable/full release
acceptance remain required. PRD-01 stays open at **34% estimated work remaining**.

The [retained identity receipt classification](schema149-clock-inventory.md#retained-identity-receipt-facts--2026-10-16)
traces five receipt writers, retained publication/consumption clocks and bounded
expiry cleanup. The maintained role guard now checks every column's UPDATE
capability for both runtime roles. Complete native RLS/role execution, ten
UPDATE refusals and ten column-grant refusals pass with unchanged row fingerprints
and captured inputs; canonical reprovisioning removes all ten grants in the
updated native test. Profile replays keep their separate mutable/upsert audit.
These scoped storage/privilege facts retain HTTP, historical, other-writer and
current immutable/full release requirements. Estimated PRD-01 work remaining
stays **34%** (planning estimate); the issue stays open.

The [complete schema-149 backend baseline](source-test-results.md#complete-schema-149-backend-baseline--2026-10-16)
passes all 1,086 active Domain and 1,359 active API cases at `c8ecfd47`, with exactly
five reviewed capacity cases deferred. Console/XML/audit counters agree; all
2,192 captured repository files and 523 binaries match, and owned cleanup restores
Docker inventory. This strengthens source regression evidence across the existing
foundation/dependency set. It precedes the separately verified Worker monitoring
repair and does not complete writer/history classification, primary-screen and
release acceptance or post-MVP capacity requirements. Estimated PRD-01 work
remaining stays **34%** (planning estimate); the issue remains open.

The [Organization immutable-fact classification](schema149-clock-inventory.md#organization-immutable-facts-and-mutable-owners--2026-10-15)
traces Board-event routing, deletion requests and completed deletion-step receipts
to their owning writers/source clocks and distinguishes their mutable counters,
progress and lifecycle. The real one-Card workflow completes; nine independent
mutation refusals and the exact new mandatory CI function preserve a fingerprint
covering all 114 public tables. All 717 source/driver inputs and 71 binaries match, and owned
cleanup restores Docker inventory. The full deletion-surface matrix, remaining
writer/history audit and current immutable/full CI remain required. Estimated
PRD-01 work remaining stays **34%**; these three classifications do not exempt
their mutable counterparts or other candidates from FOUND-FR-009.

The [current schema-149 clock inventory](schema149-clock-inventory.md) refreshes
the original schema-114 catalog baseline: 114 public base tables, partitioned
into 50 with both physical clock columns non-null, 57 missing at least one
conventional column and seven with nullable clocks. The machine-readable
snapshot covers all 114 tables without row data. Organization configuration
already persists guarded creation/update facts in JSON; absent conventional
columns do not alone establish a defect. The 5.825-second read-only capture
preserves 152 inputs and restores Docker inventory after owned cleanup.
Owning-writer/legacy-history audits and current immutable/full CI remain
required. Estimated PRD-01 work remaining stays **34%**; the issue stays open.

The [complete post-database-restart Work scenario](postgres-pool-recovery.md#complete-work-event-scenario-after-restart--2026-10-15)
passes with current compiled API/Worker, fresh restricted PostgreSQL and all 149
migrations. It strengthens the hierarchy/atomic mutation/convergence boundary:
full-state rollback on event/queue refusal, contiguous concurrent events,
cross-tenant RLS refusal and leased Worker recovery all pass. All captured inputs
match and owned cleanup restores Docker inventory. It does not replace current
immutable-image/full CI or the complete navigation/role/lifecycle matrices.
Estimated PRD-01 work remaining stays **34%**; the issue remains open.

## Current Card description dependency verification

The [PRD-09 Markdown implementation and evidence](../card-description-markdown.md)
adds the missing safe description subset, original-source preservation, live
peer delivery and active/archive/session-withdrawal cases. The full frontend
suite passes 2,536 cases; both rich cached-detail cases and all five original
normal budgets pass locally. The complete 200-List/5,000-active/100,000-archived
rank/capacity chain passes all three viewports after observing the exact admitted
drop target before release. Earlier source, timing and pointer failures remain
recorded separately. This is current local compiled-runtime proof; the entire
foundation scope and current immutable-image CI still govern closure.
Estimated work remaining stays **34%** (planning estimate).

[PRD-01](https://github.com/nickmailhot-lang/StrataAI2/issues/1) remains open at
**34% estimated work remaining** (planning estimate). The complete issue body,
including its functional requirements, acceptance criteria, thirteen scenarios
and definition of done, governs closure. This map identifies evidence and gaps;
it does not certify release readiness or waive dependencies.

## Functional requirements

| Requirement | Implementation or verification source | Evidence still required for complete acceptance |
| --- | --- | --- |
| FOUND-FR-001: User → Organization → Board → List → Card | [Canonical Domain models](../../src/StrataAI.Domain/WorkManagement/), [Organization](../../src/StrataAI.Domain/Organizations/Organization.cs), [foundation schema](../../db/migrations/001_foundation.sql), [Organization acceptance](prd-03-acceptance.md) | Match authorized membership/navigation and persisted hierarchy to the current release, including lifecycle and role matrices. |
| FOUND-FR-002: one Organization per Board | Non-null `boards.tenant_id` and Organization FK in the foundation schema; [tenant schema guard](../../scripts/ci/check-tenant-schema.sql) | Current required PostgreSQL guard/refusal and HTTP authorization evidence. A source FK is not an executed admission test. |
| FOUND-FR-003: one Board per List | Composite List `(board_id, tenant_id)` FK; [guard and negative catalog checks](../../scripts/ci/test-tenant-schema.sh) | Verify invalid/cross-tenant admission and complete unchanged state in the current pipeline. |
| FOUND-FR-004: one List and Board per Card | Composite Card `(list_id, board_id, tenant_id)` FK and independent Board FK | Current move/copy/lifecycle integrity evidence, including source/destination permissions and owning rollback. |
| FOUND-FR-005: navigation, Board header/canvas and Card overlay | [Board screen](../../apps/web/src/features/kanban/BoardScreen.tsx), [routing boundary](web-spa-boundary.md), [Board acceptance](prd-04-acceptance.md) | Complete primary-screen loading/empty/error/accessibility and role coverage against the current release. |
| FOUND-FR-006: stable Board/Card deep links | UUID routes and [persisted workflow](../../tests/browser/board.spec.ts) | Current release results for active, moved, archived/deleted and inaccessible targets, including account/tenant replacement. |
| FOUND-FR-007: preserve Board context and browser Back | [Rendered history regressions](../../apps/web/src/features/kanban/BoardScreen.test.tsx), persisted workflow, [large-Board cases](../../tests/browser/board-capacity.case.ts) | Executed local focus/viewport proof exists; immutable-image/full CI and broader lifecycle/navigation combinations remain. |
| FOUND-FR-008: desktop/tablet/mobile primary screens | Large-Board cases at 1280/768/390 pixels; [navigation cases](../../tests/browser/navigation-observations.spec.ts) | [Portal admission/reflow](portal-screen-verification.md) passes at 1280/768/390/320 CSS pixels with the original permission-loss/logout matrix. [Long-name primary-screen reflow](primary-screen-reflow.md) additionally passes the original four-width matrix for authentication/recovery, Organization listing/home/settings, profile/safe cancellation, notifications, search and invitations. Large-Board, Portal and this selected long-content coverage do not certify every primary-screen state or physical device; the complete screen/viewport matrix remains required. |
| FOUND-FR-009: creation/update clocks on all mutable entities | [Domain base](../../src/StrataAI.Domain/Common/DomainEntity.cs), [identity clocks](identity-lifecycle-clocks.md), [notification clocks](notification-audit-clocks.md), [invitation metadata](invitation-audit-metadata.md) | Complete the writer/source/projection audit below and verify accepted updates, refused repeats, rollback and historical upgrade for every mutable entity. |
| FOUND-FR-010: consistent terminology | Canonical models, navigation events, [shared work synchronization tests](../../tests/StrataAI.Api.Tests/WorkSynchronizationApiTests.cs) | Audit terminology across UI/API/events/persistence/analytics/tests and execute the current two-client/reconnect matrix. |

## Acceptance criteria

| Criterion | Required proof | Current boundary |
| --- | --- | --- |
| AC-FOUND-01-01 | Authorized hierarchy operation validates input/authorization, commits the authoritative state and reconciles the client. | The desktop/phone persisted workflow passes locally, including lost response and concurrent editing; full current release acceptance is pending. |
| AC-FOUND-01-02 | A rule-violating List operation is refused with the stable public error, unchanged protected state and no disclosure. | Composite integrity, forced RLS and server checks have source/fixture paths. Current full guard, HTTP and complete-state results must be retained. |
| AC-FOUND-01-03 | A successful canonical mutation is confirmed and reaches or is recovered by another authorized client without manual reload. | Work/Organization/label/member browser cases exercise real separate-Worker delivery and socket recovery. One passing subset cannot certify the whole criterion or release. |

## Linked test scenarios

Each scenario remains subject to its full PRD requirements; the paths below
locate coverage rather than mark the issue checkboxes complete.

| Scenario | Verification paths and remaining scope |
| --- | --- |
| PRD-01-TC-01: happy path | Persisted Board workflow and restricted hierarchy/command checks; current full release gate pending. |
| PRD-01-TC-02: empty states | Board/Organization component and browser flows; complete primary-screen matrix still required. |
| PRD-01-TC-03: invalid input | Domain/API and tenant-schema negative fixtures; verify stable errors and unchanged full state. |
| PRD-01-TC-04: unauthorized user | [Navigation HTTP tests](../../tests/StrataAI.Api.Tests/NavigationInteractionHttpTests.cs), RLS and browser isolation; complete role/visibility matrix required. |
| PRD-01-TC-05: mid-session permission loss | Native Organization/member/visibility/live cases; retain current aggregate and account-replacement results. |
| PRD-01-TC-06: timeout/retry | Navigation recovery and persisted command flows preserve original keys/state; complete current consumer/producer matrix required. |
| PRD-01-TC-07: duplicate/idempotent request | Restricted receipts, rank chain and browser recovery; require exact originals, atomic effects and conflict/refusal evidence. |
| PRD-01-TC-08: two-client update | Persisted workflow and native live matrices; full release evidence remains pending. |
| PRD-01-TC-09: disconnect/reconnect | Native navigation/Work/Organization/socket cases; complete current reconnect/access-change matrix required. |
| PRD-01-TC-10: archived/deleted parents | [Lifecycle acceptance](lifecycle-acceptance.md), archived routes and directory cases; whole lifecycle/deep-link matrix remains required. |
| PRD-01-TC-11: keyboard | Rendered history regressions, intact keyboard workflows and three-width capacity cases pass locally; current release and other primary-screen checks remain. |
| PRD-01-TC-12: mobile | Desktop/phone persisted workflows and 1280/768/390 capacity cases pass locally; physical-device and remaining screen coverage are separate. |
| PRD-01-TC-13: large-data performance | Full 200-List/5,000-active/100,000-archived rank/capacity chain and all five [normal budget cases](../kanban-performance.md#current-history-focus-normal-performance-matrix) pass locally. Repeatability, physical-device and immutable release proof remain required. |

## Mutable-record audit scope

A read-only catalog probe of the live schema-114 disposable database on
2026-10-09 identified **66 public-schema relations** lacking one or both physical
`created_at`/`updated_at` columns. It inspected column metadata, not account or
content values. This is an audit candidate list, not 66 established defects:

- Canonical mutable entities require authoritative creation/update clocks.
  Sessions and security tokens now have persisted clocks with forward-upgrade
  proof; notification read-state clocks derive from their durable first-read
  fact and retain actual timestamp precision.
- Immutable facts such as audit/navigation events or published image metadata
  require source verification before they can be classified as immutable.
  Lack of an update column alone does not establish missing mutation history.
- Routing/read projections require a trace to their canonical entity clocks;
  copying a second independent timestamp into an index does not prove the rule.
- Operational leases, sweep cursors, stream counters, replay receipts and
  lifecycle progress require an explicit writer/state classification. Their
  classification is still incomplete and cannot silently exclude them from
  the issue's all-mutable-record requirement.

For each candidate, identify every owning writer and allowed state transition,
its durable creation/update facts, canonical projection, historical migration
semantics and rollback/refusal evidence. Do not backfill an invented historical
mutation time or infer completion merely from column presence. The catalog probe
does not prove complete clock coverage.

The [issuer-authority writer audit](invitation-issuer-clock-audit.md) classifies
four candidates from their owning SQL functions and history protections. Proofs,
sources and effects are immutable facts; jobs have mutable claim/reclaim and
terminal state. Migration 115 repairs the jobs' general update clock, with
executed local full migration and restricted contract checks. Its historical
upgrade refuses unproven RUNNING leases until actual terminal drain; current
immutable-image/deployed upgrade verification remains. This does not classify
the remaining candidates.

The [Board background ownership audit](board-background-clock-audit.md) traces
both production creation paths, immutable SQL history protection and canonical
Board selection clocks. Five read-only schema-116 catalog/permission checks
confirm the installed guard, forced RLS and restricted runtime permissions.
The ownership fact has no allowed update transition; this classification does
not exclude the mutable Board or other unclassified operational records.

[Canonical entity route clocks](entity-route-clocks.md) adds source-derived
creation/update metadata to Board/List/Card/Label projections in migration 117.
The local locked build, restricted persistence/readiness contracts and complete
populated migration gate pass. A fresh complete gate also passes explicit
orphan-source refusal, untouched route history and absent columns/ledger after
rollback, followed by successful repair and repeat application. Current
immutable-image/deployed upgrade verification remains. Invitation routing and other operational writers
still require their own history audit.

The same [route-clock record](entity-route-clocks.md#preserve-discovery-authorization-refusals)
retains a failure in the original restricted routing security gate and migration
118's forward repair. Discovery-only writes remain denied with the original
42501 authorization category; source-history faults retain their 23514 guard.
The original gate, full migration gate and restricted ledger-readiness contracts
pass. Extended denial checks pass for all four clocked route types; current
immutable-image/deployed proof remains required.

[Invitation routing clocks](entity-route-clocks.md#invitation-routing-clocks)
adds canonical invitation creation/update facts in migration 119, retaining
the historical publication Organization label. The local full upgrade gate
passes orphan-source refusal/rollback, exact historical clocks/body, repeated
application, no-op and tamper checks. Expanded actual restricted
creation/acceptance/revocation/rollback and discovery-denial verification passes,
including unchanged invitation clocks/publication label after a fixture parent
rename. Other mutable operational history and current release proof remain.

[Organization access route clocks](entity-route-clocks.md#organization-membership-access-routing)
adds persisted membership creation metadata in migration 120. Reciprocal
membership state integrity and tenant RLS remain; the restricted discovery
write-denial gate passes for all six routing projections. Complete historical
upgrade, role/status propagation and actual Owner creation precision/readiness
verification passes. The local locked build and 179 CI source checks also pass.
Other operational records and current release scope
remain part of FOUND-FR-009.

The [recipient page/counter audit](invitation-recipient-authority.md#page-lifecycle-clocks-and-remaining-counter-audit)
adds explicit managed page creation/update clocks in migration 116 from the
recorded owning job and first completion. Its complete local migration gate
passes. Recipient revision counters are mutable and lack historical delivery
timestamps; their writer/provenance repair remains unresolved, not silently
excluded from FOUND-FR-009.

## Executed evidence and closure boundary

The [invitation recipient publication counter clock repair](invitation-recipient-stream-clocks.md)
derives clocks from retained immutable events and preserves recipient lookup
privacy. The separate authority revision-counter provenance gap remains;
this repair does not establish complete mutable-clock or release acceptance.

The [Organization metadata counter clock repair](organization-metadata-stream-clocks.md)
derives clocks from retained journal facts and requires matching source history
before commit. Its local migration/runtime verification is scoped separately
from current immutable-image release and the full mutable-record audit.

The [Work event delivery clock audit](work-event-clock-audit.md) identifies
a remaining update-clock/provenance gap and verifies the original activity
source/publish/reset contract on schema 121 with full required-ledger readiness.
Its full-row immutable-history guard and admitted reset semantics must be
preserved in a future repair. The complete current schema-121 Board browser
phase is active; its result remains pending.

The [Organization event clock repair](organization-event-clocks.md) derives
metadata/lifecycle journal update clocks from actual first publication or
creation, retaining immutable payload and delivery authority. Its migration
and restricted-role verification are bounded evidence; other mutable records
and complete current release acceptance remain part of FOUND-FR-009.

The [navigation execution record](../navigation-observations.md) retains the
history-focus repair, two regressions failing before repair, final 38-case Board
component pass, complete **1,986-test/142-file web-suite pass**, type/lint/build checks,
and the local **30-pass/one-failure** complete Board invocation. Both persisted
history workflows passed; desktop member-removal consent preparation failed.
Its full report/trace remain private. The following intact rerun passed both
repaired member-consent cases but finished with **29 passes and two failures**
in phone metadata and label activation, no skipped/flaky cases or report-level
errors (1,424.54 seconds). Those traces are also retained; the pending fixture
repairs are being verified in another fresh complete 31-case invocation.

That next complete invocation finished with **29 passes and two failures** in
1,469.35 seconds, no skipped/flaky cases or report errors. Member, metadata and
Card-label cases all passed; phone historical-actor paging and the later
label-filter picker opening failed. The [updated execution record](../navigation-observations.md#complete-strict-board-rerun-after-activation-repairs)
retains the scope, diagnoses, sixteen passing activity component regressions and
independently verified cleanup. A fresh full rerun with pending fixture repairs
is active; this is not aggregate acceptance or release proof.

The subsequent complete invocation also finished **29/31**, with no skipped or
flaky cases, in 1,345.90 seconds. Activity-history passed; desktop Card-label
Clear and label-filter due-completion Apply failed. Their pending actual-dispatch
fixture checks retain original criteria, results and command counts. The next
full invocation uses current compiled schema-115 API/Worker source; its
[execution scope and preserved failures](../navigation-observations.md#subsequent-complete-board-result-and-current-schema-115-rerun)
remain separate from current immutable-image CI and aggregate acceptance.

The next complete schema-115 compiled-source phase now passes **31/31** across
all fourteen original files in 1,352.49 seconds, each case passing on its only
attempt with no skips/flaky cases/report errors. The [full passing scope](../navigation-observations.md#complete-schema-115-board-phase-passed)
retains the original assertions, exact dispatch/key/state checks, failed-run
history, 179 passing CI source checks and independently verified cleanup. This
closes the pending local aggregate verification of those five fixture repairs;
newer schema-116/immutable-image release and complete foundation matrices remain.

The complete three-width capacity chain passes all **3/3 cases** and final
100,000 archived-record count/fingerprint checks. [Its execution record](../navigation-observations.md#executed-large-board-history-and-viewport-preservation)
documents runtime, optional rank-fixture verification policy, exact nested scroll
checks, fixture restoration and independently confirmed cleanup. It does not
replace normal performance or current immutable-image CI.

All five unchanged normal performance cases also pass together against the
history-focus frontend in 161.84 seconds. [Recorded measurements and scope](../kanban-performance.md#current-history-focus-normal-performance-matrix)
preserve all twenty mutation samples per applicable case, independently checked
nearest-rank p95, null release revision and independently verified cleanup.
One local normal/capacity pass is not performance repeatability or full release
acceptance.

The [Card route clock identity lookup](card-route-clock-lookup.md) addresses
observed growing-List scans inside the canonical clock read. The full original
100,002-card deletion candidate fixture and routing isolation checks remain;
current full verification is pending and no clock, permission or release
acceptance is inferred from the plan diagnosis alone.

That subsequent full schema-125 deletion verification now passes all seven
original contracts and complete runtime readiness, including the original large
candidate seed, full scale graph and restricted discovery. The extended whole
migration gate and unchanged routing isolation gate pass. The linked record
retains complete measurements and earlier failures; current immutable-image
release acceptance remains separate.

The [remaining candidate audit](remaining-clock-candidates.md) records a fresh
schema-125 catalog and selected notification-counter, sweep-cursor and recipient
authority writers. Its 52 physical-column candidates are not defect or closure
counts. Missing historical provenance and unclassified candidates remain;
no clock semantics are invented or current runtime checks claimed by that audit.

The subsequent [complete responsive Board phase](../navigation-observations.md#complete-schema-119-responsive-board-phase-passed)
passes **32/32 cases across fourteen files**, including desktop, tablet and
mobile document-width checks, with one attempt per case and independently
verified cleanup. Organization action rows wrap; the original persistence,
history, retry, concurrency and isolation assertions remain. This is local
compiled schema-119 runtime evidence; schema-120 and current immutable-image
release proof remain outstanding.

Closure additionally requires all functional/data/API/permission/business rules,
canonical navigation-event publication/consumption, full mutable-clock coverage,
realtime/lifecycle/edge-case behavior, telemetry, documented performance budgets,
accessibility and error states, reviewed migrations, all required test layers and
the complete current build-once release pipeline. Verify no known P0/P1 defects
and sufficiently complete product behavior documentation. The declared PRD-02,
03, 04, 06, 08 and 09 dependencies remain; cycle membership does not waive them.
Estimated work remaining stays **34%** (planning estimate).

The [Organization deletion checkpoint clock repair](organization-deletion-progress-clocks.md)
addresses a confirmed missing creation column using its immutable accepted
request. The complete migration gate, populated-history refusal and all seven
original restricted deletion contracts now pass, including the future-checkpoint
terminal regression. This proves the repair's local scope; complete mutable-clock
coverage and current release acceptance remain outstanding.

The subsequent [notification counter clock repair](notification-stream-clocks.md)
passes the complete migration/refusal/source-admission checks and original
tenant/RLS/runtime-role/Reminder SQL gates. Its complete native producer/consumer
phase now passes all nine cases with one attempt each and no skips. Readiness
verification also passes all 126 independently applied migration removals,
expanded from the original 48 selected cases. Additional complete watch/ordering
and current capacity execution remain separate. Neither this local repair nor an incomplete live run establishes
full clock coverage or current immutable-image release acceptance.

The [complete schema-126 candidate catalog](remaining-clock-candidates.md#complete-schema-126-catalog-navigation)
now lists all 51 current physical-column candidates, with verified declaration
links and retained clock fields. This is complete catalog navigation, not
complete mutable-record classification. Additional mail-snapshot and recipient
revision traces identify legacy mutation provenance and global User-effect
deduplication requirements. The full schema-126 pre-route-repair Linux API suite
also passes all 691 cases; [its source record](source-test-results.md) separates
that proof from the six-case route repair and fresh full current invocation.
No foundation or release requirement is waived; the estimate remains **34%**.


The [final Board/Card long-label reflow phase](primary-screen-reflow.md#board-and-card-long-label-reflow)
adds four passing viewport cases for complete previews, List actions/drop targets,
keyboard metadata dialogs and Card deep-link/focus restoration. Final complete
frontend source proof passes 2,037/2,037; baseline clipping and intermediate
fixture/source failures remain documented separately. This is local runtime/source
proof, not full FOUND-FR-008, accessibility or current immutable CI acceptance.
Estimated PRD-01 work remaining stays **34%**; the ticket remains open.


The [Demo background-job audit repair](background-jobs.md#demo-queue-audit-state)
adds actual first-publication/update timestamps and revisions to the new mutable
process-local queue. Claims, retries, completion and final expiry record admitted
mutations; duplicate/refused/canceled operations and rollback preserve the original
audit state. Baseline 7/10 and repaired complete three-class 17/17 results are
separate; full repaired API/current immutable CI remain pending. This does not
resolve legacy persisted provenance or full FOUND-FR-009. Estimated PRD-01 work
remaining stays **34%**, and the ticket remains open.


## Current hold entity clock convention

The subsequently introduced RetentionHold current state now projects the common entity `createdAt`/`updatedAt` names from its accepted persisted `appliedAt`/`releasedAt` lifecycle clocks. All current/directory/history/receipt/live representations carry the same exact clocks. Release preserves creation and advances update to its accepted release; original receipt recovery and refused repeats preserve their captured time. Client response validation refuses aliases inconsistent with their source. The Domain regression, all 70 focused Records API cases, complete restricted native PostgreSQL contract and two maintained production desktop/mobile release/history cases passed. This closes a specific newer entity convention gap; the wider mutable writer catalog, performance/current immutable CI and complete acceptance remain required. Estimated PRD-01 work remaining stays **34%**.

## Current normal performance and source acceptance evidence

The [current schema-146 repeated normal matrix](../kanban-performance.md#current-schema-146-cached-detail-focus-and-repeated-normal-matrix)
retains the dated-Card cached-detail baseline failure at 211.32 ms, the diagnostic
failure and the subsequent focus placement repair. Two fresh-database invocations
each pass all five unchanged desktop/phone cases within the original readiness,
cached-detail, feedback and mutation-p95 budgets, with no retries/skips/flaky
results and independently verified samples, source/web hashes and cleanup.
These compiled local Nginx observations establish this normal-condition scope;
they do not replace current release capacity, physical-device or full acceptance.

Hosted source checks on `1c068329` additionally pass all 993 Domain and 912 API
tests with zero failures/skips; [the recorded scope](source-test-results.md#schema-146-hosted-source-result-follow-up)
keeps that source SHA separate from the subsequent frontend focus repair and
pending complete immutable-image integration. All original requirements remain.
Estimated PRD-01 work remaining stays **34%** (planning estimate).

## Current full supported Board capacity verification

The [schema-146 complete rank/capacity chain](../kanban-performance.md#current-schema-146-full-rank-and-three-viewport-capacity-chain)
now passes unchanged against `ac0d0484`, including original concurrent commands,
durable replay/refusal checks and all three desktop/tablet/phone cases. All
Back/Forward/Close focus, four nested scroll offsets, keyboard movement,
windowing, responsive-width and tagged accessibility assertions pass. The final
count and complete-record fingerprint prove all 100,000 archived Cards unchanged
after every viewport. Captured source/compiled/web hashes and owned cleanup are
independently verified. The earlier host-bootstrap failure is retained separately.
This updates local supported-capacity evidence to the current schema and cached-
detail repair; current immutable-image/full CI and all original broader
requirements still govern closure. Estimated work remaining stays **34%**.

## Creation form admission during protected Board reads

The [creation admission verification](board-creation-admission-verification.md)
records a clean controlled reproduction of missed List opening on tablet/mobile,
the complete repaired three-viewport diagnostic, and all three ordinary repository
scenarios without injection. A five-second read-only opening loop confirms the
List/Card creation field while preserving desktop pointer input, tablet/mobile
keyboard input, all Create/Save submissions, original retry keys/counts,
conflict drafts, navigation and tenant/public-reader isolation. The original
test deadline and release pacing remain. Captured source/runtime/web hashes and
owned cleanup are independently verified. Local compiled-runtime evidence does
not replace current immutable/full CI; estimated remaining work stays **34%**.

## Original navigation replay during protected reads

The [navigation replay repair](../navigation-observations.md#original-navigation-replay-across-protected-reads-2026-10-14)
records three retained viewport failures, the canceled replay's original key
and revision, and a failing component regression. One active replay now survives
temporary read admission changes, retaining scope/account fences. The complete
unchanged three-viewport scenario passes 3/3 with exact original requests,
receipt/session retirement, later fresh visit, keyboard Back, accessibility and
deadlines. All 26 navigation checks and the full 2,581-test web suite pass.
Inputs and owned cleanup are independently verified. Current immutable/full
acceptance remains required; work remaining stays **34%**, with large-load
testing deferred until after the MVP.
