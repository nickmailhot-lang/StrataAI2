# PRD-21 API contract acceptance map

UX delivery addendum: apply [UXA-01–08](../ux/feature-acceptance.md), this PRD's [dependency/discovery mapping](../ux/backlog-map.md) and the [separate functional/UX/test/MVP report](../ux/delivery-report.md). Existing acceptance below remains authoritative; historical evidence is not re-certified by this addendum.

The [core Work diagnostic repair](api-error-contract-verification.md#core-work-synchronization-diagnostics--2026-10-15)
keeps synchronization failures at their private, correlated HTTP 503 and closes
failed Work streams when logging also fails. Five new regressions fail before
repair; all 55 expanded iterator/middleware/HTTP/SignalR cases pass afterward,
including cancellation, already-started replies and runtime role/schema refusal
preservation. Inputs match and owned cleanup restores Docker inventory. Current
immutable/full CI, deployed providers and the complete API matrix remain required.
Estimated PRD-21 work remaining stays **23%** (planning estimate).

[PRD-21](https://github.com/nickmailhot-lang/StrataAI2/issues/22) remains open.
The complete requirements, acceptance criteria, thirteen scenarios and Definition
of Done govern closure. This map locates implementation and verification;
source presence does not certify every endpoint or the current release.

| Requirement | Implementation and verification | Remaining proof |
| --- | --- | --- |
| API-FR-001: validate all inputs | ASP.NET JSON/route binding; Application input normalization; [host tests](api-host-testing.md) | Complete endpoint/input inventory, malformed/oversized/unknown input boundaries, stable refusals and unchanged state in current immutable CI. |
| API-FR-002: authorization before protected disclosure | Session middleware, Application current-scope admission, restricted PostgreSQL/RLS; [HTTP host tests](../../tests/StrataAI.Api.Tests/ApiHostTests.cs) | Whole entity/role/visibility/lifecycle and replay matrix, not only route attributes or a passing subset. |
| API-FR-003: consistent error envelope | [Shared error contract](../../src/StrataAI.Api/ApiErrorContract.cs), [HTTP regressions](../../tests/StrataAI.Api.Tests/ApiErrorContractTests.cs), [unexpected-error/readiness regressions](../../tests/StrataAI.Api.Tests/UnexpectedApiErrorTests.cs), [verification and limits](api-error-contract-verification.md) | Whole error/consumer inventory, optional field-error mapping and retained-image verification. Existing domain codes and specialized database 503s remain; empty HEAD responses retain HTTP semantics. |
| API-FR-004: bounded cursor collections | [Search cursors](../../tests/StrataAI.Api.Tests/GlobalSearchCursorTests.cs), [activity cursors](../../tests/StrataAI.Api.Tests/ActivityCursorTests.cs), entity directories | Full materially growing collection inventory, seek stability, pre-limit admission, invalid/revoked cursors and current release results. |
| API-FR-005: retry deduplication | [Work request context](../../src/StrataAI.Api/WorkManagement/WorkCommandContext.cs), [identity command context](../../src/StrataAI.Api/Auth/IdentityCommandContext.cs), [transactional Work unit](../../src/StrataAI.Infrastructure/WorkManagement/PostgresWorkManagementUnitOfWork.cs) | Complete retry-prone command inventory, atomic original receipts, competing keys, revocation and rollback against current restricted persistence and images. |
| API-FR-006: concurrency preconditions | Card/Board/List versions, Domain normalization and Application services; existing competing-writer fixtures | Full command/version inventory and authoritative rank/version reconciliation across real clients and lifecycle changes. |
| API-FR-007: canonical terminology | Organization → Board → List → Card Domain, routes, DTOs and events | Consistent terminology across every API/consumer/event/telemetry representation. |
| API-FR-008: reliable publication | [PostgreSQL event store](../../src/StrataAI.Infrastructure/WorkManagement/PostgresWorkEventStore.cs), transactional unit of work, separate durable Worker | Current publication rollback, readiness/job fencing, deduplication and reconnect proof across all producer families. |
| API-FR-009: separate binary transfer | [Upload HTTP tests](../../tests/StrataAI.Api.Tests/AttachmentUploadApiTests.cs), [attachment acceptance](attachment-covers-lifecycle.md) | Actual managed storage/scanner/upload/download admission and full retained-image chain; simulated provider cases remain explicitly limited. |
| API-FR-010: common public-view authorization | [Public filtering tests](../../tests/StrataAI.Api.Tests/PublicBoardFilterTests.cs), [native public filtering](../../tests/browser/public-board-filter.spec.ts) | Whole public/private/read-only and permission withdrawal matrix, current pipeline evidence and two-client recovery. |

AC-API-21-01 requires actual authorized input validation, durable state and client
reconciliation. AC-API-21-02 requires a stable refusal, complete unchanged state
and no protected disclosure. AC-API-21-03 requires a real authorized peer to
receive or recover a public-view-relevant mutation without a manual reload.
Demo HTTP host tests, restricted persistence, real Worker browser flows and
immutable release evidence prove different layers; none replaces the others.

The thirteen required scenarios still cover happy/empty/invalid input,
unauthorized and mid-session authority loss, timeout/retry and duplicate keys,
concurrent clients and reconnect, archived/deleted parents, keyboard/mobile and
large-data performance. Links above are coverage locations, not checked-off
scenario acceptance. Existing 1.5-second Board, 100-ms movement, 500-ms mutation
p95, 200-ms cached detail and 200-List/5,000-Card/100,000-archived targets remain.
The adopted MUI SPA, modular-monolith API, separate Worker, PostgreSQL/RLS,
pgvector, object storage and rigorous immutable build-once CI remain required.

Estimated PRD-21 work remaining: **23%** (planning estimate). The outstanding
contract inventories, whole error/health response audit, managed-provider and
complete current release verification prevent closure.
