# Board synchronization contract (PRD-22)

`GET /boards/{id}/sync?since=cursor&limit=100` returns a bounded page of durable,
content-free Work event headers. The server resolves the Board's Organization and
current view permissions before opening its tenant/RLS event session, then checks
permissions again after the awaited event read. A denied, deleted or inaccessible
Board returns the same `board_not_found` 404 without events or recovery metadata.
Responses use `Cache-Control: no-store`. No client-supplied Organization grants
access or changes the resolved scope.

`since` is an unsigned decimal cursor within PostgreSQL's signed bigint range;
omission means zero. Duplicate, negative, empty, nonnumeric and overflowing values
return `invalid_sync_cursor` 400. `limit` defaults to 100 and must be 1–100, otherwise
`invalid_sync_limit` 400. Cursor and event sequence fields are decimal strings so
browser number rounding cannot skip events. Entity `version` retains the mutation
API's numeric version contract.

Each page includes `cursor`, `hasMore`, `pending`, `resetRequired` and `events`.
Events include eventId, organizationId, boardId, nullable actorId, eventType,
entityType, entityId, version, sequence, createdAt and empty metadata. Domain
content, correlation metadata, titles, descriptions and queue metadata are absent.
Event IDs are stable on repeated reads. Consumers must deduplicate IDs and fetch
authorized authoritative state before reconciling UI data.

The reader includes pending rows in its bounded window and stops at the first
unready sequence. Later ready events never advance the cursor past that boundary.
`pending=true` means wait or use a fresh authorized snapshot; do not busy-loop.
`hasMore=true` means another contiguous ready page is available. A cursor ahead of
the stream or missing committed history returns `resetRequired=true`, cursor zero
and no events: load an authorized snapshot and restart recovery. Boards predating
the event migration have a valid empty stream. No retention cleanup is implemented.

Entity visibility is evaluated against current active List/Card/parent List state.
An archived, deleted, moved-away or otherwise unavailable historical entity emits
only `BOARD_INVALIDATED` with the current Board ID/version; its historical entity
ID/type/version and actor are omitted from disclosure. This does not grant archive
browsing permissions. Actor IDs are exposed only to a current Board administrator
for visible events, consistent with the protected membership surface; other readers,
including public visitors, receive null. Public Board archival revokes visitor
replay. Authorization continues to use the existing Board/Organization policy.

Production replays only events marked ready by the separately scoped Worker.
Operators must configure its explicit Organization scope as documented in
`background-jobs.md` and the release README. Demo uses the same sequence/page and
visibility rules in memory with immediate readiness; it is not durable production
delivery. Read failures return sanitized `work_sync_unavailable` 503 and log only a
stable failure message and request correlation ID.

Tests cover delayed predecessors, bounded pagination and repeat IDs, cursor precision,
missing/future cursor recovery, invalid query binding, private/public authorization,
historical hidden-entity filtering and revocation after an awaited read. CI exercises
the exact release API with real PostgreSQL/RLS, including a blocked event SELECT
followed by membership suspension and a denied response with no partial batch.

`/boards/live` now exposes the SignalR `Watch(boardId,cursor)` server stream using
these same bounded replay pages. Configure `STRATAAI_REALTIME_PUBLIC_ORIGIN` to the
exact browser scheme/host/port; otherwise it uses `STRATAAI_PUBLIC_ORIGIN`. Missing
origin configuration disables live transport with sanitized 503; invalid configured
origins reject startup. Every negotiate/transport request requires the exact trusted
Origin, including direct WebSocket upgrades. No wildcard origins are accepted.

One connection may hold one Board stream. Cancelling/disconnecting releases its
scope. Each pass revalidates the active cookie session and Board access; session
validation is repeated after awaited reads. A permission revision change discards
the tentative page and rereads/redacts it. Logout, session expiration/reset and lost
Board access abort the connection. Anonymous subscriptions can read only currently
public Boards. Invalid/revoked supplied cookies cannot silently retain an authenticated
subscription. Receive messages are bounded to 4 KiB, event pages to 100, streaming
buffer capacity to one and output buffers to 128 KiB. Detailed server errors are off;
read failures log only a stable warning and request correlation ID.

The server checks persisted readiness once per second and sends changes, pending
status transitions and a heartbeat approximately every 20 seconds. PostgreSQL/RLS
is still the source; no in-memory cross-process event broker or extra service was
added. Nginx forwards WebSocket upgrades for this route with buffering disabled.
Consumers must still deduplicate, recover cursors and refresh authorized state.
The transport follows [Microsoft's streaming contract](https://learn.microsoft.com/en-us/aspnet/core/signalr/streaming?view=aspnetcore-10.0)
and [Origin/security guidance](https://learn.microsoft.com/en-us/aspnet/core/signalr/security?view=aspnetcore-10.0).

API host tests exercise actual SignalR JSON/WebSocket framing, wrong origins,
protected scope denial, logout/membership revocation, subscription caps and cancellation.
The release browser fixture connects two sockets through the real Nginx edge,
consumes PostgreSQL events made ready by the actual Worker, reconnects from a cursor,
and verifies copied-session logout revocation. Its protocol probe is test-only.

The Board screen now uses `@microsoft/signalr` 10.0.11, restricted to same-origin
WebSockets with cookie credentials and the CSRF intent header for negotiation.
SDK logging is disabled; server/provider error bodies are never shown. The SDK
owns framing/heartbeats and reconnects at 0, 2, 10 and 30 seconds. Failed initial
starts or exhausted reconnects retry at 1, 2, 5, 10 and then 30 seconds; the attempt
counter resets only after a valid replay page. Every reconnect subscribes from the
last accepted string cursor. Scope changes/unmount cancel timers, streams and the
connection, and stale callbacks cannot affect another scope.
Stream-error handling defers to the next microtask: transport cancellation can
enter the SDK reconnect lifecycle first, rather than being interrupted by an
application stop/start. An application stream error or malformed page still
restarts safely. Resubscription cancels any leftover custom start timer.

Client cursor advancement validates scope, contiguous sequence, decimal bigint
range, page size and flags before committing any part of a batch. A bounded cache
retains 1,024 event IDs/sequences to deduplicate repeats without rewinding. Invalid
pages retain the old cursor and trigger sanitized fallback/reconnect. History reset
clears the cursor/cache and requests a fresh authorized snapshot. Repeated resets
do not cause a refresh storm. Snapshot invalidations coalesce over 100 ms; reads
already in flight complete before one queued refresh rather than being repeatedly
aborted by incoming events.

The screen automatically checks snapshots every 10 seconds while disconnected,
delivery is pending or history reset remains unresolved. Transport failures and
reconnects also request an immediate coalesced refresh. A transport initialization
failure retains snapshot polling. Normal live delivery refreshes authoritative
state only for new events; an unsuccessful snapshot read also retries automatically
after 10 seconds, so advancing the event cursor cannot leave failed refreshes stale
indefinitely. Explicit refresh is always available.
Board reads have a 15-second deadline, cancel the underlying transport and settle
even if it ignores cancellation. This includes explicit card recovery. Timeouts
preserve scoped data/drafts for retry; supersession/unmount cancels silently and
removes the deadline. Late timed-out responses cannot apply after recovery.
Dirty card fields, base version and focus are preserved on incoming newer snapshots; edits cannot
silently overwrite them. A denied snapshot clears protected state and tears down
the subscription. The UI announces connection/recovery/fallback status politely.

Unit tests cover deduplication, atomic invalid/gapped/cross-scope rejection, bigint
precision, reconnect cursors/backoff, polling, repeated resets and disposal. The
desktop/phone browser fixture exercises the production SDK and actual Board UI,
two-client pushed updates, dirty fields/focus, outage fallback, reconnection and
copied-session logout revocation. CI uses the exact images and real Worker-ready
PostgreSQL events; local Demo validates the same client against immediate readiness.
The browser fixture also holds a real phone GET beyond the deadline, verifies its
abort and subsequent authoritative recovery, and preserves the draft throughout.

This is not full PRD-22 completion. Optimistic movement reconciliation, large-board
performance, complete accessibility/telemetry and all acceptance coverage remain.
