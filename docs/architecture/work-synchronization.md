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

This is the replay boundary, not full PRD-22 completion. SignalR transport, live
session revalidation/revocation, client deduplication/reconnect/snapshot recovery,
automatic fallback, optimistic movement reconciliation, performance/accessibility
and complete telemetry/acceptance coverage remain to implement. The current Board
screen still uses explicit refresh and its existing dirty-draft protections.
