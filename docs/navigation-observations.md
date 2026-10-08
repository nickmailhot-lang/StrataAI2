# Navigation observations — implementation in progress

FOUND-FR-009's mutable-record audit now includes the
[executed session/security-token lifecycle clock repair](architecture/identity-lifecycle-clocks.md).
That scoped upgrade and restricted-store proof does not certify every mutable
entity or full PRD-01 acceptance. Estimated work remaining stays **34%**.
The [canonical invitation metadata audit](architecture/invitation-audit-metadata.md)
also projects its existing stored clock/revision through creation and lifecycle
reads, with complete restricted returned-row and recovery checks.

PRD-01 requires `APPLICATION_CONTEXT_CHANGED`, `BOARD_OPENED`, and `CARD_OPENED`. These are personal navigation observations. They do not change shared Board/Card content or replace audit history. See the [documentation index](README.md), [routing isolation](architecture/routing-isolation.md), and [current actor sessions](architecture/command-actor-sessions.md).

## Implemented behavior

The admitted internal Organization shell confirms Organization context. The authorized Organizations directory confirms global context after its account-bound read completes. Active Board and Card screens confirm opens after their current reads finish. Anonymous visitors submit no personal observation. Archived Boards, archived Lists, and unavailable targets cannot authorize an active-target observation.

`POST /navigation/observations` accepts an empty body and exactly one value per supported query field:

| Kind | Query fields |
| --- | --- |
| `context` | `kind=context`; optional `organizationId` |
| `board` | `kind=board`, `organizationId`, `boardId`, positive `version` |
| `card` | `kind=card`, `organizationId`, `boardId`, `cardId`, positive `version` |

The request requires authentication, the normal application request marker, an `Idempotency-Key`, and `X-StrataAI-Expected-Actor`. The server validates the current session, target access, parent lifecycle, and observed revision inside the owning transaction. Replays require current authorization; a receipt is not an access grant. An exact persisted original remains recoverable after later entity revisions; fresh observations still require the current revision. Current target scope and active parent lifecycle are rechecked in both cases. Responses are private and must not be cached.

The canonical acknowledgment has exactly ten fields: `eventId`, `eventType`, `actorId`, `organizationId`, `boardId`, `entityType`, `entityId`, `version`, `metadata`, and `createdAt`. Metadata is empty. Global context uses `ApplicationContext` with its event ID as entity ID; Organization context uses the Organization ID. Board and Card observations use their actual entity IDs and revisions. Timestamps preserve PostgreSQL microsecond precision.

## Retry and consumption

The browser checks the account before and after dispatch, validates the acknowledgment against the independently admitted target, and deduplicates original event IDs in a bounded account-specific window. Account replacement or screen cancellation prevents consumption.

Before dispatch, the original request is retained in session storage under its account and target. Returning to the same target recovers the original key and revision, even if the newly loaded revision differs. Successful confirmation removes only the matching retained key. A lost response offers **Retry navigation confirmation**; editing the entity does not replace the unresolved open request.

The retry window is 24 hours. Browser recovery storage allows 1,000 retained requests and reclaims at most 100 canonical expired originals owned by the current account when full. Live or foreign-account originals are not evicted. Storage failure prevents dispatch of an unretained new original. Session storage is local to the browser tab; it is not a cross-device event queue.

Production sources and receipts use PostgreSQL forced RLS with actor-private access and immutable originals. Receipt admission serializes through the active actor, retains the original event ID/time, and performs current target authorization on replay. The separate Worker has no navigation-source grant. These records are separate from optional analytics and shared work events.

## Verification and remaining work

Navigation confirmation reports optional client observations through the existing authenticated `/me/activity-client-events` endpoint. Fixed actions are `navigation_context`, `navigation_board`, and `navigation_card`; fixed kinds record visit opens, attempts, explicit user retries, exceptions, and success/failure timing. A visit open is counted once, while each admitted attempt is counted separately. Cancellation does not report a failure or exception. Timing covers account checks, admission, transport and recovery completion, and is not the Board rendering or server mutation performance measurement.

The bounded best-effort queue contains only action, kind, count and optional duration. It retains no actor, entity, Organization, route, request key, original event, content or exception detail. Reports are never retried; report failure cannot change the authoritative acknowledgment. These untrusted measurements are separate from immutable navigation sources and shared audit history. Server parsing rejects extra fields. Stable permission-denial and realtime measurements still require their separate acceptance evidence.

Focused model, producer, HTTP, browser consumer, transport, component, and recovery fixtures exist. Local browser tests cover canonical validation, account replacement, cancellation, lost responses, return visits, and bounded recovery storage. Local .NET compilation does not prove runtime acceptance; native SQL and HTTP execution must pass CI for the relevant revision.

Broader native replay expiry/capacity/concurrency evidence and full PRD-01 acceptance remain incomplete. Full-suite verification must follow fixture repairs; focused recovery success alone does not establish a full-suite result. Exact-image browser, performance, accessibility, lifecycle, and realtime acceptance must also be verified before closing the ticket. Navigation observations do not by themselves prove the PRD's telemetry or performance requirements.

### Acceptance evidence map

The following fixtures cover navigation-specific requirements from [PRD-01](https://github.com/nickmailhot-lang/StrataAI2/issues/1). A fixture's existence is not a passing result. Check its execution for the same application revision before using it as acceptance evidence.

| Requirement or scenario | Executable evidence | Verification boundary |
| --- | --- | --- |
| Canonical fields, actor and target admission, immutable originals | [Producer tests](../tests/StrataAI.Api.Tests/NavigationInteractionProducerTests.cs), [store tests](../tests/StrataAI.Api.Tests/NavigationInteractionStoreTests.cs), [browser acknowledgment tests](../apps/web/src/app/navigationInteraction.test.ts) | Browser checks execute locally; .NET runtime execution requires CI on this workstation. |
| Invalid input, anonymous or wrong actor, lost response, duplicate submission, access revocation | [HTTP tests](../tests/StrataAI.Api.Tests/NavigationInteractionHttpTests.cs), [exact-image HTTP checks](../scripts/ci/test-navigation-observations.sh) | Demo host tests and production container checks are distinct requirements. |
| Forced RLS, private receipts, expired receipt reclamation, capacity, rollback, recovery after revision changes | [Restricted PostgreSQL fixture](../scripts/ci/test-navigation-interaction-sources.sql) | Requires the native PostgreSQL CI job; compilation cannot verify these guarantees. |
| Concurrent requests with the same original key | [Exact-image HTTP checks](../scripts/ci/test-navigation-observations.sh) | All concurrent responses must contain the same persisted original, through the release proxy. |
| Retained original, return visit, account isolation, expiry and bounded browser storage | [Recovery tests](../apps/web/src/app/navigationRecovery.test.ts) | Local storage tests do not prove server admission or native browser behavior. |
| Desktop, tablet, phone, keyboard Back, lost response followed by an entity edit, accessibility | [Native browser checks](../tests/browser/navigation-observations.spec.ts) | Runs at 1280, 768 and 390 pixels against release images; execution remains pending for the current implementation. |

Full foundation acceptance also requires hierarchy integrity, all mutable-record audit fields, lifecycle-aware deep links, preserved Board viewport context, authorized two-client updates and reconnect recovery, and the stated capacity and performance targets. Those requirements span other feature suites and their acceptance records; this navigation evidence map does not establish their completion.

### Revision-specific verification ledger

- CI run [37407613926](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37407613926)
  at `e3bcb1d` passed web quality: 122 files and 1,468 tests, followed by the web
  build. Its API-host job failed the two navigation HTTP scenarios with
  `BadRequest`; the navigation route was absent from the idempotency middleware
  at that revision. `5caf8be` subsequently added matched-route handling and
  invalid-key/trailing-slash fixtures; `74ce75d` added private/no-store headers
  before middleware key rejection. Current runtime verification is pending.
- The same run's PostgreSQL job rejected a complete migration ledger before
  restricted search traversal. `e158969` removed the stale numeric readiness
  count, and `e0c78cd` added restricted API/Worker complete, missing and restored
  ledger checks. See [schema readiness](architecture/schema-upgrades.md#runtime-migration-readiness).
- Local full web execution at `ca81d3f` finished with 121 files passing and one
  failing: 1,468 tests passed and one URL-attachment recovery fixture failed.
  `c6541ed` supplied its canonical navigation acknowledgment and waited for
  enabled command admission and actual original dispatch. The focused repaired
  scenario and typecheck passed. A fresh full run is pending; no current-main
  full-suite or release success is asserted by these results.

These results identify separate source, HTTP and persistence failures and their
later repairs. They do not prove exact-image browser, performance, accessibility
or whole-ticket acceptance.

- Subsequent CI run [37408240323](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37408240323)
  at `74ce75d` passed .NET quality: 643 Domain tests and 426 API-host tests,
  with zero failures. This provides native execution evidence for the navigation
  HTTP middleware/key/cache repairs present at that revision. Web quality also
  passed. PostgreSQL failed with the same runtime schema readiness exception
  before restricted search traversal; that revision predates `e158969`.
  Whole-release and current-main acceptance remain unproven.

## Observation transaction admission

Navigation originals and retries use the dedicated identity observation boundary.
Production locks the scoped Organization parent before shared actor admission,
then serializes private interaction history with a transaction advisory lock.
Migration 085 updates restricted append/replay functions to the same order.
This avoids the observed account/Board lock inversion while retaining current
session checks before/after storage, current target access, private subject RLS
and atomic source/receipt publication. Demo retains its account-before-Work gates.
The [CI investigation](architecture/browser-recovery-ci.md) records the failed
revision and the mandatory native lock-order regression; execution is pending.

A verified navigation visit is now retained as complete across temporary live
read admission changes. Re-admission cannot resubmit its completed original;
unresolved attempts keep their existing recovery. A new mounted visit creates a
fresh original key. The component regression covers both paths. Native execution
remains required to resolve the duplicate-key failure reported by the old
`5a2433f` browser suite.

## Native completion before leaving an acknowledged scope

Desktop, tablet and phone navigation cases pass in the current seven-case compiled
Production API/Worker/MUI/PostgreSQL invocation. A received server receipt still
requires current-account confirmation before its local original is removed.
The fixture waits for that completion before leaving an acknowledged screen;
the deliberately lost Card original survives leaving and returns with the same
key, query and event. Later visits require new keys, and final retained-original
counts remain zero. The fixture never removes storage entries. Keyboard Back,
Board context and WCAG assertions remain. See [execution evidence](architecture/browser-recovery-ci.md#archive-source-admission-and-navigation-completion-boundaries).

Estimated PRD-01 work remaining stays **34%** (planning estimate). This focused
execution does not establish complete foundation or current immutable CI acceptance.
