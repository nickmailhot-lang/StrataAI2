# Activity history observations (PRD-15, partial)

The MUI Board/Card activity reader records opening, read attempts, successful
and failed read durations, explicit user retries and client exceptions. Reports
contain only the fixed actions `board_read`, `card_read`, `board_disclosure` and
`card_disclosure`, a fixed observation kind, count and optional milliseconds.
The same endpoint now also accepts fixed comment and mention actions:
`comment_disclosure`, `comment_read`, `comment_create`, `comment_edit`,
`comment_delete`, `mention_read`, `mention_selection`,
`card_group_confirmation` and `board_group_confirmation`. Comment editing and
original-request retries record use/retry, confirmed result timing, definite
conflict and client exceptions. A clean opened comment view records reconnect
recovery. Teammate lookup records use/result; explicit selection and group
consent record only fixed use counts. Selection/consent are not proof of a
notification delivery or recipient count. Reports never carry account/tenant/Board/Card identities, event IDs, historical
captions, bodies, profiles, cursors, paths, raw error codes or exception messages.
Aborted obsolete reads do not report a successful result.

Production reporting batches up to 20 observations after five seconds, retains
at most 120 queued observations, coalesces fixed counters up to 100 and aborts
report transport after three seconds. Failed reports are dropped without retry.
The same-origin transport supplies existing session and CSRF protection. Local
development disables reporting. Counts are untrusted use observations, never
authoritative domain history or audit evidence.

`POST /me/activity-client-events` requires an authenticated session and shares
the existing per-user client-events abuse budget with Checklist reporting.
Its bounded reader accepts at most 8 KiB and depth five, checks every entry in a
batch before recording anything, and refuses extra/duplicate fields, unknown
categories, empty/oversized batches, nonnumeric/unbounded counts and invalid
timings. Duration results must have count one and 0–60,000 milliseconds. Shared
parsing/transport preserves the existing Checklist contract. Operator listener
failure cannot change a successful observation acknowledgment.

Native instruments are `strataai.activity.client.events` and
`strataai.activity.client.duration` (seconds), with fixed `action`/`kind` labels.
The existing optional OTLP exporter now selects `StrataAI.ActivityClient` along
with the Board-sharing and Checklist meters. Existing server request/duration
instruments distinguish `board_activity_read` and `card_activity_read`, with
stable server outcomes/error codes. No new collector, datastore or app image is
introduced. Reports remain separate from immutable Work sources.

The mandatory exact-image operator fixture now submits authenticated Board/Card
activity reads and opening/retry/success observations, rejects anonymous and
private-cursor reports, and requires activity counters/duration and both server
read operations in the actual pinned Collector scrape.
The fixture additionally creates and reads an actual comment using its captured
authoritative Card version and requires comment opening/create/teammate-use
counters, comment-create duration and both actual server comment operations.
Raw scrapes are temporary; the retained artifact contains only fixed verification
flags and build metadata.
Its validator refuses unknown metric families/private labels and cannot pass
when any required observation is missing.

Local managed compilation, focused UI/transport tests and operator-validator
tests cover this increment. Actual Linux managed/exporter and release Collector
execution is required before claiming successful ingestion. This does not
complete PRD-15: complete native browser/lifecycle/reconnect, large-data timing
and cross-feature acceptance remain open. Comment/mention telemetry source
tests cover preserved original retries/group confirmations, safe teammate
selection, conflict classification and reconnect refresh; actual managed and
release ingestion remains required.

A focused regression also exposed activity denial completion ordering that
could leave keyboard focus on a disabled older-page control. The reader now
clears its completed request before publishing idle denial state. Recovery
focus therefore need not wait for a final promise microtask that may not produce
another render. Denial tests exercise synchronous parent access publication,
and the existing delayed-read test continues to preserve another control's
focus ownership.

PRD-17 inbox observations use the same bounded transport and instruments with three additional fixed actions: notification_disclosure, notification_read and notification_mark_read. They record opening, use/retry, successful/failed latency, fixed conflict/client-exception categories and browser online recovery. Focus/visibility/timer refreshes remain ordinary use reads, not reconnect observations. No notification/recipient/entity IDs, selected IDs, links, cursor, command keys or diagnostics enter reports. Cancelled obsolete reads/results remain fenced, and counts are untrusted client observations rather than notification delivery or audit evidence. A component test drives an uncertain read command and exact retry, offline failure and online recovery and verifies the bounded payload contains only fixed fields. Six server parser cases reject private extra fields atomically. All 25 focused component/transport cases, SPA and full browser typechecks and lint pass; strict .NET compilation has zero warnings/errors. Server runtime and deployed collector/native release evidence remain pending.

Notification SignalR recovery also emits the existing fixed notification_read/reconnect observation on SDK reconnection or a successful retained-cursor retry, and notification_read/exception on invalid replay/stream failure or failed connection startup. Observers receive only the fixed kind, never an exception or stream value. Disposed and obsolete stream callbacks remain fenced. Connection tests verify fixed-only observations and no emissions after disposal; the inbox privacy regression includes live recovery observations in the actual bounded telemetry request. Counts remain client observations, not proof of unique network reconnects, durable delivery or authorization outcomes. Native/collector execution remains required.
