# Recipient-private notification events

The current implementation provides `NOTIFICATION_CREATED` and
`NOTIFICATION_READ` separately from Board replay: atomic PostgreSQL recipient
journals, authorized bounded HTTP sync, recipient-private SignalR delivery and
MUI invalidation/recovery. HTTP polling and foreground/online refresh complement
the private transport. The [PRD-17 acceptance map](prd-17-acceptance.md) records
executed native scenarios and remaining release, concurrency and capacity gates;
implementation does not by itself establish complete acceptance. The historical
integration notes below preserve earlier evidence limits.

The envelope identifies the stable notification, recipient, historical source
Organization/Board and actor. Creation uses the source actor, notification revision
one and original notification creation timestamp. Read uses the recipient actor,
revision two and the persisted first read timestamp. A retry clock cannot produce
a replacement read timestamp. Event identity and per-recipient sequence must be
assigned and retained by the journal transaction, never regenerated during stream
delivery or replay. Sequence values serialize as invariant decimal strings to
preserve bigint precision in the browser.

Metadata is an immutable empty dictionary. No Card title, description, comment,
profile, current Board projection or entity link is included. Consumers must
re-admit the inbox and obtain current authorized content/links from that service.
The envelope factory validates identities and timestamp ordering; it grants no
authorization and provides no durability by itself.

Storage and admission invariants: creation appends with the originating
notification transaction and first read appends with its read transaction.
Original event identity survives retries, and duplicate transitions are
suppressed. PostgreSQL enforces tenant isolation and recipient ownership without
granting the Worker general notification-table access. Reminder delivery uses its
narrow database capability to append the same atomic creation envelope. Delivery
checks the current session and Organization/source/current-entity visibility
before and after awaited reads, uses bounded recipient pages and recovers gaps
without exposing other recipients or trusting an old handshake principal. The
demo adapter implements the same contract while remaining explicitly non-durable.
SignalR and the existing API/Worker deployables remain the adopted architecture.

## Historical integration evidence

Three source tests cover identity/historical scope and absent projected Card data,
read actor/timestamp semantics and precise rejection of invalid scope/order.
Strict solution compilation succeeds with no warnings or errors. Local .NET test
execution is unavailable under Windows Application Control; Linux CI execution is
pending. No persisted event or completed PRD-17 acceptance is claimed.

Demo adapter integration now appends the creation envelope only when the first notification is inserted, and the read envelope only on the first read transition. Replayed commands and later read commands retain the same event identity/timestamp and do not append another transition. Per-Organization/recipient sequence windows are bounded to 51 internal entries and validate cursor/scope. Journal and notification snapshots are captured together by the existing demo transaction rollback participant. Source coverage checks dedupe, self-suppression, scope, 50/2 seek, rollback and subsequent sequence reuse; an API case checks actual read/replay/new-key behavior and denied recipient access without adding events. Strict solution compilation passes with zero warnings/errors; runtime CI remains pending. This is the explicitly non-durable demo adapter only. PostgreSQL journal, Reminder capability integration, authorized delivery and native recovery are still unfinished; the internal journal window is not an authorized endpoint.

PostgreSQL storage integration: migration 068 adds forced-tenant-RLS recipient streams and append-only private event rows, deterministic historical backfill, scope foreign keys, empty-metadata checks and creation/read revision checks. Notification row triggers append in the same owning transaction, including the existing narrow Reminder function. The first read time cannot be replaced; replay/same-time updates append nothing. Trigger capabilities have fixed search paths and no runtime/PUBLIC execute grants. API has SELECT only and Worker has no general journal access. A tenant advisory transaction gate precedes recipient counters to prevent reversed recipient-lock order across Board producers; notification effects across Boards of one tenant therefore serialize, and concurrent/fan-out performance remains unproven. The bounded PostgreSQL reader requires the owning tenant command scope, uses explicit recipient filtering, and validates stored envelope identity/timestamp/revision on reconstruction. It remains an internal window, not an authorized endpoint. Runtime readiness now requires 68 migrations. Upgrade fixtures check exact backfill scope/count, event/counter rollback and same-time update dedupe; role fixtures check read-only API/no Worker/direct-function capability. Strict solution compilation succeeds; three script syntax checks pass. Actual PostgreSQL migration/role/trigger/reader runtime execution remains pending Linux CI. Authorized SignalR delivery/native recovery are still unfinished.

Authorized bounded sync reads now use NotificationInboxService.ReadEventsAsync and GET /organizations/{organizationId}/notifications/sync. The current actor is the recipient; fresh active Organization/account admission and existing source/current Board/Card/List visibility gates apply before and after awaited reads. The service validates scope, monotonic sequence and event uniqueness, scans at most 50 journal entries with one lookahead, and discloses only presently admitted notifications. Inaccessible entries advance only the recipient-owned sequence cursor; an empty filtered page with hasMore must continue seeking, rather than imply exhaustion. This is bounded scanning, not a claim that 50 visible entries are filled across arbitrarily many inaccessible sources. Responses are private/no-store, and invalid cursors use the existing stable validation error after scope admission. API source coverage checks 50/2 pages, distinct identity, recipient isolation, anonymous/invalid requests, empty hidden-window continuation, and restoration of the original event identities after access is restored. Strict solution compilation passes; actual Linux execution remains pending. SignalR streaming, client invalidation/gap recovery and native acceptance remain unfinished.

Current integration supersedes the unfinished transport statements above: `/notifications/live` provides one SignalR `Watch(organizationId, cursor)` subscription per connection. An initial null cursor snapshots the recipient head without replaying history; HTTP sync without a cursor still explicitly starts at zero. Future cursors return an empty reset to the current head. The hub reauthenticates its original session before and after every authorized service read, advances hidden windows, drains bounded replay pages and emits periodic heartbeats. Production and development proxies support WebSockets and the existing origin fence covers negotiation and connection requests. API source tests cover actual creation/read transitions, backlog handoff, reset, cancellation, cursor/origin rejection and logout. Compilation passes; actual Linux execution remains pending.

The MUI inbox now subscribes with its current organization/recipient. It validates bounded replay, scope, IDs, revisions, empty metadata, exact UTC timestamps and decimal sequences using bigint before acknowledging a cursor. Events trigger fresh HTTP admission/content reads and are never directly rendered. Initial handoff, reconnect, reset, hidden-window advancement and transport failure invalidate content; reconnect retains the exact decimal cursor, and disposal fences stale callbacks. Invalidation during an active HTTP read or mutation is retained and processed afterwards. Existing bounded HTTP polling/focus/online recovery remains available. Focused parser, connection and inbox tests pass locally; actual browser transport, cross-peer recovery, revocation and capacity evidence still require the immutable-image CI run. This progress does not satisfy all PRD-17 acceptance criteria.

Replay admission also rejects unexpected properties at both page and event level. The accepted fields match the current server SyncPage and NotificationRealtimeEvent contract exactly; projected Card content, links, account details, diagnostics and read-state extras cannot be silently accepted while advancing a cursor. Seven paired page/event cases cover this boundary. All 32 focused replay/connection tests, SPA typechecking and lint pass; actual native/server delivery remains pending CI.

## Executed native integration

The [five-case watch/inbox/reminder invocation](prd-17-acceptance.md#executed-native-watch-inbox-and-reminder-recovery)
passes unchanged against local compiled Production API, separate Worker, current
MUI bundle, restricted PostgreSQL 17/pgvector and Nginx. It includes actual private
notification events and reconnect, shared reads, original-key response-loss
recovery, overlapping watch deduplication/self-suppression, unwatch, direct Card
watch movement/parent withdrawal and desktop/phone reminder recovery. See the
linked record for exact assertions and runtime/policy limits. Local compiled
evidence supersedes the earlier local-execution gap; strict email policy, due
reminder fire, full capacity/concurrency and current immutable-image acceptance
remain separate. PRD-17 stays open at **22% estimated work remaining**.
