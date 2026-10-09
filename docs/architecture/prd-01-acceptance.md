# PRD-01 foundation acceptance and closure audit

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
| FOUND-FR-008: desktop/tablet/mobile primary screens | Large-Board cases at 1280/768/390 pixels; [navigation cases](../../tests/browser/navigation-observations.spec.ts) | Large-Board coverage does not certify every primary screen or physical device. Complete the screen/viewport matrix. |
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

## Executed evidence and closure boundary

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

Closure additionally requires all functional/data/API/permission/business rules,
canonical navigation-event publication/consumption, full mutable-clock coverage,
realtime/lifecycle/edge-case behavior, telemetry, documented performance budgets,
accessibility and error states, reviewed migrations, all required test layers and
the complete current build-once release pipeline. Verify no known P0/P1 defects
and sufficiently complete product behavior documentation. The declared PRD-02,
03, 04, 06, 08 and 09 dependencies remain; cycle membership does not waive them.
Estimated work remaining stays **34%** (planning estimate).
