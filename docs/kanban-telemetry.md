# Kanban command telemetry

The existing operator meter `StrataAI.BoardSharing` now covers PATCH list
commands as `list_update` (rename/rank/move) and POST card movement as
`card_move`. It records native `strataai.board_sharing.requests` counters and
`strataai.board_sharing.duration` histograms in seconds, including security and
command processing. Existing Board reads already use `board_read`.

The same meter now covers List/Card creation, Card updates, List/Card archive,
restore and deletion, plus archived List/Card page reads. Operations use fixed
names (`list_create`, `card_create`, `card_update`, `list_archive`, `card_archive`,
`list_restore`, `card_restore`, `list_delete`, `card_delete`,
`archived_list_read`, `archived_card_read`, and List copying as `list_copy`). Lifecycle, explicit-consent,
contained-card impact and invalid archive-cursor errors join the bounded
allowlist. Matched route templates select operations; raw URLs and cursor input
never become labels.

The lifecycle host regression exercises successful creation/edit/archive/restore/
deletion, repeated archive acknowledgment, rejected deletion consent, changed
contained-card impact, malformed cursor and private denial. It asserts exact
request/duration counts and the shared safe-label contract. This new case builds
with warnings as errors. Linux CI run
[37008689795](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37008689795)
at `4db92b3` subsequently passed the unfiltered Domain/API host and web source
gates, including the lifecycle regression. Its full exact-image release gate is
still running. The earlier historical run below proves the movement case only.

Labels remain operation, outcome, allowlisted error_code and boolean
keyed_attempt. Titles, recipients, actor/tenant/object IDs, route values, raw
paths, correlation IDs, request bodies and retry keys are excluded. Stable move
errors include missing card/list, invalid position/rank and rank-space
exhaustion. Listener failures remain isolated from authoritative responses.

Each HTTP attempt is measured, including historical receipt recovery. A keyed
attempt does not prove a user-visible retry or a new mutation; these instruments
are not an audit source. The regression case checks two successful keyed card
attempts, a stale conflict, a private denial, a list update and an invalid list
position, with the existing bounded-label/value assertions.

The solution and test compile with zero warnings/errors. Local .NET execution
is blocked by Windows Application Control; executed test evidence is pending
Linux CI. Run 36946480776 at 97b2cce subsequently executed all 123 Domain and
179 API-host tests successfully, as confirmed in decoded job 110649660708 logs.
That includes the newly compiled Kanban telemetry regression in the unfiltered
API-host suite; it is source/host evidence, not full release-image acceptance.
Operator collector/export, dashboards, alerting, client exceptions,
reconnect/conflict rates, user-visible retry events and browser timing/performance
acceptance remain incomplete. This instrumentation does not complete PRD-06.

### Checklist client observations (PRD-13, partial)

Production Checklist disclosure/read and mutation controls now send best-effort
aggregate observations to `POST /me/checklist-client-events`. Explicit mutation
attempts and receipt retries use separate categories; success follows strict
canonical acknowledgment parsing. Read success follows scope/version validation.
Canceled/disposed responses do not report success or failure. Fixed exception
and conflict categories contain no exception message or object. Timings include
current-actor verification and validated acknowledgment, rather than HTTP alone.

The queue retains at most 120 observations, sends at most 20 every five seconds,
aggregates non-result counts up to 100, aborts transport after three seconds and
drops failed batches without retry. It uses the existing same-origin cookie/CSRF
path, with no new dependency. Server admission requires authentication, CSRF and
a separate 64-request/minute per-user limiter. Bodies are bounded to 8192 bytes,
even without Content-Length. The entire batch is validated before measurement;
unknown/duplicate properties and invalid types/categories/counts/timings fail
closed. The protocol excludes content, identities, scope, retry keys and errors.

Native Meter `StrataAI.ChecklistClient` provides counter
`strataai.checklist.client.events` and duration histogram
`strataai.checklist.client.duration` (seconds), tagged only by fixed `action` and
`kind`. These are untrusted client observations, not authoritative audit records
or a reliable denominator for product success rates. Listener failures cannot
change successful API responses. Existing server-side request/permission metrics
remain the authoritative HTTP observations.

Four queue tests and creation-control assertions cover safe batching, bounded
storage/transport, dropped failures, disabled mode, canonical success, malformed
acknowledgment failure and explicit original-intent retry. The focused 96 web
tests pass; typecheck/lint/production build pass. New host tests compile, including
whole-batch rejection, authentication/CSRF, body bounds, listener isolation and
per-user rate partitions. Their execution awaits Linux CI because Windows
Application Control prevents local .NET test execution. Collector/export,
operator dashboards, Checklist-specific realtime reconnect observations, render
exceptions and full performance/capacity acceptance remain incomplete.

Checklist reconnect observations now use an explicit completed transport-recovery
callback from the existing durable Board stream. After an established stream
loses transport (SDK reconnect or controller restart), the callback runs once
when scoped cursor validation accepts a non-pending, non-reset page. Normal
Worker pending/reset transitions do not increment it. Initial connection and
failed initial starts do not count. Disposed streams suppress callbacks, and
observer errors do not break recovery. BoardScreen passes only a local sequence
counter into the Checklist disclosure; open views report fixed realtime/reconnect
observations, closed views and newly selected Cards do not replay prior episodes.
Coalesced counts are bounded to 100. Eight Checklist disclosure tests and thirteen
stream tests pass. Collector/export/dashboard and render-exception coverage
remain incomplete; no claim of complete PRD-13 telemetry or release acceptance.

Subsequent executed server proof: Linux run 37107129291 at b99c988 completed
source-quality, web, .NET and PostgreSQL gates successfully. Decoded job
111157751220 confirms 246 Domain and 249 API-host tests passed unfiltered,
including the new client telemetry cases. Build-once images and mandatory
container/security/release gates are still pending; this is source/host evidence.

API operator export is now explicitly configurable through OTLP HTTP/protobuf;
see `docs/architecture/operator-metrics.md` for bounded scope, configuration and
remaining collector/dashboard/Worker evidence. Export tests compile; execution
and deployed ingestion remain pending. This supersedes the absence of an export
path above, without claiming full telemetry acceptance.
