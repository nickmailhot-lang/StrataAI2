# Navigation observations — implementation in progress

PRD-01 requires `APPLICATION_CONTEXT_CHANGED`, `BOARD_OPENED`, and `CARD_OPENED`. These are personal navigation observations. They do not change shared Board/Card content or replace audit history. See the [documentation index](README.md), [routing isolation](architecture/routing-isolation.md), and [current actor sessions](architecture/command-actor-sessions.md).

## Implemented behavior

The admitted internal Organization shell confirms Organization context. Active Board and Card screens confirm opens after their current reads finish. Anonymous visitors submit no personal observation. Archived Boards, archived Lists, and unavailable targets cannot authorize an active-target observation.

`POST /navigation/observations` accepts an empty body and exactly one value per supported query field:

| Kind | Query fields |
| --- | --- |
| `context` | `kind=context`; optional `organizationId` |
| `board` | `kind=board`, `organizationId`, `boardId`, positive `version` |
| `card` | `kind=card`, `organizationId`, `boardId`, `cardId`, positive `version` |

The request requires authentication, the normal application request marker, an `Idempotency-Key`, and `X-StrataAI-Expected-Actor`. The server validates the current session, target access, parent lifecycle, and observed revision inside the owning transaction. Replays require current authorization; a receipt is not an access grant. Responses are private and must not be cached.

The canonical acknowledgment has exactly ten fields: `eventId`, `eventType`, `actorId`, `organizationId`, `boardId`, `entityType`, `entityId`, `version`, `metadata`, and `createdAt`. Metadata is empty. Global context uses `ApplicationContext` with its event ID as entity ID; Organization context uses the Organization ID. Board and Card observations use their actual entity IDs and revisions. Timestamps preserve PostgreSQL microsecond precision.

## Retry and consumption

The browser checks the account before and after dispatch, validates the acknowledgment against the independently admitted target, and deduplicates original event IDs in a bounded account-specific window. Account replacement or screen cancellation prevents consumption.

Before dispatch, the original request is retained in session storage under its account and target. Returning to the same target recovers the original key and revision, even if the newly loaded revision differs. Successful confirmation removes only the matching retained key. A lost response offers **Retry navigation confirmation**; editing the entity does not replace the unresolved open request.

The retry window is 24 hours. Browser recovery storage allows 1,000 retained requests and reclaims at most 100 canonical expired originals owned by the current account when full. Live or foreign-account originals are not evicted. Storage failure prevents dispatch of an unretained new original. Session storage is local to the browser tab; it is not a cross-device event queue.

Production sources and receipts use PostgreSQL forced RLS with actor-private access and immutable originals. Receipt admission serializes through the active actor, retains the original event ID/time, and performs current target authorization on replay. The separate Worker has no navigation-source grant. These records are separate from optional analytics and shared work events.

## Verification and remaining work

Focused model, producer, HTTP, browser consumer, transport, component, and recovery fixtures exist. Local browser tests cover canonical validation, account replacement, cancellation, lost responses, return visits, and bounded recovery storage. Local .NET compilation does not prove runtime acceptance; native SQL and HTTP execution must pass CI for the relevant revision.

Global application-context screen wiring, broader native replay expiry/capacity/concurrency evidence, and full PRD-01 acceptance remain incomplete. The full Board regression suite has reported timeout failures despite narrower scenarios passing; investigate these rather than treating focused success as a full-suite result. Exact-image browser, performance, accessibility, lifecycle, and realtime acceptance must also be verified before closing the ticket. Navigation observations do not by themselves prove the PRD's telemetry or performance requirements.
