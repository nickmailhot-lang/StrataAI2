# ARCH-03 backend architecture acceptance

This record checks [ARCH-03](https://github.com/nickmailhot-lang/StrataAI2/issues/84)
against current source and retained evidence. It does not close the architecture
ticket based on the already implemented core or treat future modules as waived.
Estimated work remaining is **60%** (rough planning estimate, primarily complete
module coverage and aggregate mode/isolation verification, not an assertion count).

## Requirement map

| Requirement | Current source / proof path | Remaining acceptance |
| --- | --- | --- |
| FR-001: one ASP.NET API/container | [API host](../../src/StrataAI.Api/Program.cs), API project and release composition; separate Worker is preserved. | Current immutable-image complete CI and diagnostics remain separate from local compiled-host proof. |
| FR-002: all named Domain modules | Domain currently groups Common, Organizations and WorkManagement; Application/Infrastructure additionally group Identity, Onboarding, BackgroundJobs, Runtime and Persistence. | This does not demonstrate complete Identity/Directory, Governance, Meetings, Operations/Maintenance, Projects/Vendors/Finance, Assets, Documents/Records, Compliance, Communications/Intake, OwnerPortal, AI, Reporting/Search, Audit and Security module coverage. Existing core types or disabled navigation entries do not fulfill future functional modules. |
| FR-003: contracts, endpoint registration, services per module | API explicitly registers Identity, Organizations, Onboarding and WorkManagement; those groups expose Application contracts and Infrastructure implementations. | Every required module must have its complete supported contracts, service orchestration and protected endpoints; no empty registration/folder counts as implementation. |
| FR-004: dependency injection | [Production composition tests](../../tests/StrataAI.Api.Tests/RuntimeCompositionTests.cs) resolve 24 implemented core persistence contracts through the actual startup graph. [Project direction guard](../../tests/StrataAI.Api.Tests/ArchitectureBoundaryTests.cs) retains Domain dependency purity. | Extend actual composition/production-construction verification for every added module. Source DI registration is not proof of all future contracts. |
| FR-005: explicit cross-module write orchestration | Existing Identity/Organization/Work unit-of-work contracts, command scopes, transactional publishers and current migration/RLS gates have scoped verification records. | Every cross-module write path must retain actor/tenant authority, transactional publication, retry/version behavior and rollback; unknown future writers are not accepted by these core proofs. |
| FR-006: safe stable Problems | Shared API Problem normalization, exception handling and correlation metadata; frontend safe-reference records retain fixed wording and malformed metadata refusal. | Full endpoint/error/code/disclosure review and current immutable-image checks across all modules. |
| FR-007: retryable external work outside requests | Separate Worker, PostgreSQL jobs/outbox, identity/invitation delivery, Work event handlers and actual scoped browser delivery exist. | Complete remaining external-work modules and Demo/Production contract admission; the Demo queue repair and remaining execution gaps are described below. |
| FR-008: shared tenant/security/audit | Organization-scoped stores, forced RLS/composite constraints, actual tenant/runtime-role gates and core API negative tests. | All required modules and mutable clock provenance remain; a passing core isolation suite cannot certify unimplemented endpoints. |
| FR-009: safe health/runtime/build diagnostics | API host exposes health/runtime metadata and readiness composition; source/private native records exercise current compiled API/Worker. | Verify current exact-image diagnostics/readiness, restart/outage and partial-deployment behavior without secret/content disclosure. |
| FR-010: unsupported Production configuration refuses startup | Real API startup tests reject missing/empty/unknown runtime mode; Production configuration requires database credentials, with no Demo fallback. | Remaining provider/configuration permutations, all added modules and current exact-image failure paths. |
| FR-011: no Demo reporting in Production | Current Production composition excludes Demo sample store, Demo hosted services and sample endpoints. | Reporting itself is not demonstrated as a complete Production module; absent reporting is not proof of its required behavior. |

## Demo composition and real publication

The initial actual-API composition baseline passes **6/7** and fails the new
Demo case specifically on missing `IBackgroundJobStore`. This confirms the
registration gap from source inspection; it is not a database failure. The
private baseline is `demo-composition-baseline-native-20261010`.

[The process-local job store](../../src/StrataAI.Infrastructure/BackgroundJobs/InMemoryBackgroundJobStore.cs)
now provides Organization-scoped claim/complete/fail behavior. It retains the
first publication per Organization/type/key, scheduled availability, two-minute
leases, exact Organization/job/Worker/lease fences, five attempts, bounded
exponential retry delay and terminal retirement after the final expired lease.
Canceled admission does not consume a lease; invalid error codes are refused.
It keeps no production connection or provider.

The [actual Demo reminder publisher](../../src/StrataAI.Infrastructure/WorkManagement/InMemoryCardReminderJobPublisher.cs)
uses this store inside its owning Work command. The store participates in the
same rollback snapshots as the domain mutation. Consumers acquire the same
Work gate, so an uncommitted publication cannot be claimed and rollback cannot
restore over concurrent consumer state. Duplicate or terminal keys are retained;
capacity refusal does not evict keys and admit duplicate effects.

[Queue invariants](../../tests/StrataAI.Api.Tests/DemoBackgroundJobStoreTests.cs)
and [real Work transaction/HTTP publication](../../tests/StrataAI.Api.Tests/DemoReminderQueueTests.cs)
cover this behavior. Expanded actual-API composition keeps all 24 core contracts,
including the background store, and excludes PostgreSQL and production transports
in Demo. Test-only internal visibility permits direct invariant tests without
making the transaction scope or publication API public.

This repair does **not** establish complete Demo processing of all job types.
Deletion and attachment simulators retain their existing paths; the separate
Worker still refuses generic Organization execution in Demo. Process-local
queues are not shared across API/Worker processes. Complete Demo handlers and
future module publication/processing remain acceptance work under ARCH-05/07.
No no-op binding, provider fallback, reduced manifest or future-module waiver is
introduced.

## Acceptance and verification boundaries

- AC-001 requires every supported module's actual Demo-safe composition without
  production dependencies. Current subset tests and source mapping are insufficient
  for all named modules; aggregate Demo execution remains incomplete.
- AC-002 requires actual Production binding for complete supported module coverage.
  Current startup tests prove implemented core bindings; their unreachable fixture
  database deliberately does not prove real persistence/RLS.
- AC-003 has explicit real-host negative runtime-mode tests; all configuration
  acceptance and current immutable release proof remain separate.
- AC-004 requires every module endpoint to fail closed for an out-of-scope
  Organization. Current real core PostgreSQL/API gates do not cover future modules.

The previous full schema-133 API result is 697/697 on its compiled backend with
actual source/content-root mappings; its scope remains in
[source test results](source-test-results.md). The new **14/14 focused Demo
architecture cases pass** in the pinned Linux runtime, with 14 actual unique
executions and matching report counters, zero failed/skipped/pending/error
results. This includes actual 24-contract Demo composition, real HTTP reminder
publication/replay, all five real Work transaction outcomes, rollback/consumer
coordination, 16 concurrent claim contenders, schedule/cancellation, exact lease
fences, retry/final-crash bounds and invalid error-code refusal. Private report:
`demo-job-store-final-lease-architecture-native-20261010/api.trx`.

The complete publication-backend API suite (before the subsequent audit repair
below) passes **711/711**, with 711 unique execution
IDs and matching declared counters, zero failed/error/timeout/aborted/pending
or unexecuted results. The terminal process exits zero. It uses the final locked
backend in the pinned .NET 10.0.12 Linux runtime and four actual source/content-root
mappings; this is compiled source proof, not a release-image claim. Private report:
`demo-job-store-final-full-api-native-20261010/api.trx`. The earlier expanded
binary independently passes 705/705 and remains retained separately; it does
not substitute for the final 711-case run. Both owned API test containers are
absent after terminal cleanup. Current complete Domain source proof passes 758/758
in the pinned Linux runtime. The latest locked solution builds with zero
warnings/errors. Expanded-test import and assertion-analyzer build failures are
retained separately from their corrected builds; no analyzer/assertion/deadline
is suppressed. The owned focused test container is removed after its terminal
result.

The complete frontend source suite passes 2,037/2,037 across 142 files with two
file workers, unchanged per-case deadlines/within-case concurrency and no case
retry options. This is local source proof, not immutable-image architecture
acceptance. The current complete original Board phase passes 32/32, with
independently verified fixture cleanup; its browser scope is separate from
architecture acceptance. ARCH-04/05/06/07 dependencies and every DoD requirement
remain.


## Demo queue audit follow-up

The [Demo audit repair](background-jobs.md#demo-queue-audit-state) closes the new
queue record's creation/update/revision gap without changing lease admission,
retry bounds or transaction ownership. Baseline queue class: 7/10, with all three
new audit checks failing on absent fields. Repaired complete queue, actual Work
transaction/reminder and runtime-composition classes: 17/17 unique executions,
matching counters and zero non-passing results. Locked solution build has zero
warnings/errors. Full repaired-backend API execution subsequently passes all
**714/714 actual unique tests/executions**, matching declared counters with zero
non-passed results, in the pinned Linux runtime with real source/content-root
mappings. Its owned container is independently absent; report:
`demo-job-audit-full-api-native-20261010/api.trx`. Current immutable-image
CI remains pending. Complete Demo execution/future modules and all original
architecture criteria remain. Estimated ARCH-03 work remaining stays **60%**.
