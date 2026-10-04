# Activity history observations (PRD-15, partial)

The MUI Board/Card activity reader records opening, read attempts, successful
and failed read durations, explicit user retries and client exceptions. Reports
contain only the fixed actions `board_read`, `card_read`, `board_disclosure` and
`card_disclosure`, a fixed observation kind, count and optional milliseconds.
They never carry account/tenant/Board/Card identities, event IDs, historical
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
read operations in the actual pinned Collector scrape. Raw scrapes are temporary;
the retained artifact contains only fixed verification flags and build metadata.
Its validator refuses unknown metric families/private labels and cannot pass
when any required observation is missing.

Local managed compilation, focused UI/transport tests and operator-validator
tests cover this increment. Actual Linux managed/exporter and release Collector
execution is required before claiming successful ingestion. This does not
complete PRD-15: complete native browser/lifecycle/reconnect, large-data timing,
remaining comment/mention telemetry and cross-feature acceptance remain open.
