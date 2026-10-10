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
| FR-007: retryable external work outside requests | Separate Worker, PostgreSQL jobs/outbox, identity/invitation delivery, Work event handlers and actual scoped browser delivery exist. | Complete remaining external-work modules and Demo/Production contract admission; generic Demo job-store resolution is currently missing, described below. |
| FR-008: shared tenant/security/audit | Organization-scoped stores, forced RLS/composite constraints, actual tenant/runtime-role gates and core API negative tests. | All required modules and mutable clock provenance remain; a passing core isolation suite cannot certify unimplemented endpoints. |
| FR-009: safe health/runtime/build diagnostics | API host exposes health/runtime metadata and readiness composition; source/private native records exercise current compiled API/Worker. | Verify current exact-image diagnostics/readiness, restart/outage and partial-deployment behavior without secret/content disclosure. |
| FR-010: unsupported Production configuration refuses startup | Real API startup tests reject missing/empty/unknown runtime mode; Production configuration requires database credentials, with no Demo fallback. | Remaining provider/configuration permutations, all added modules and current exact-image failure paths. |
| FR-011: no Demo reporting in Production | Current Production composition excludes Demo sample store, Demo hosted services and sample endpoints. | Reporting itself is not demonstrated as a complete Production module; absent reporting is not proof of its required behavior. |

## Concrete Demo composition gap

[Runtime configuration](../../src/StrataAI.Infrastructure/Runtime/RuntimeConfiguration.cs)
returns from the Demo branch after registering the sample store and dependency
status. Its generic `IBackgroundJobStore` registration is only in Production.
The complete Infrastructure registration inventory has no Demo implementation
of that contract. Existing Demo deletion/reminder/attachment simulations and
private in-memory publishers do not establish generic claim/complete/fail
capability. This is a source finding; execution of the new real-host Demo
composition regression is pending.

Do not hide the gap by removing the background contract from a mode-composition
manifest, registering a no-op provider, adding folders or declaring all future
modules out of scope. A repair must preserve real Demo-safe behavior, process-local
state, Organization isolation, original publisher/transaction ownership and
lease/retry semantics where supported. Production PostgreSQL and separate Worker
boundaries must remain. If the intended Demo architecture excludes this contract,
that requires an explicit resolved architecture decision rather than silent waiver.

## Acceptance and verification boundaries

- AC-001 requires every supported module's actual Demo-safe composition without
  production dependencies. Current subset tests and source mapping are insufficient
  for all named modules; the generic background-store gap is open.
- AC-002 requires actual Production binding for complete supported module coverage.
  Current startup tests prove implemented core bindings; their unreachable fixture
  database deliberately does not prove real persistence/RLS.
- AC-003 has explicit real-host negative runtime-mode tests; all configuration
  acceptance and current immutable release proof remain separate.
- AC-004 requires every module endpoint to fail closed for an out-of-scope
  Organization. Current real core PostgreSQL/API gates do not cover future modules.

The previous full schema-133 API result is 697/697 on its compiled backend with
actual source/content-root mappings; its scope is retained in
[source test results](source-test-results.md). A new Demo-composition regression
is under development and is not part of that executed count. No new .NET
build/test pass is claimed here. The frontend full-source and current 32-case
Board invocations are live independently and do not establish this architecture's
completion. ARCH-04/05/06/07 dependencies and every DoD requirement remain.
