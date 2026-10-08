# Kanban command telemetry

## Runtime exception coverage

Production startup installs window observers for script/event-handler errors and
unhandled promises. They report `application_event_exception` and
`application_promise_exception`, respectively. Resource load events are excluded;
handled promise rejections do not fire this browser observation. The listeners
never read error messages, Error objects, promise reasons, filenames, stacks or
route values. They suppress the browser's default private diagnostic, without
stopping other listeners, retrying commands, reloading or changing feature state.
Development retains normal browser diagnostics. Listener disposal removes both
registrations; startup installs them once outside React's StrictMode tree.

React's production root callbacks separately count uncaught root failures as
`application_root_exception` and recovered failures as
`application_recovery_exception`, printing only the existing fixed message.
Caught renders remain owned by the router callback to avoid double counting.
These four categories join the three routed-render categories in the existing
bounded activity queue and authenticated collector. All seven accept exception
counts only, never timings, success/failure or other interaction kinds. Whole-batch
validation, abuse limits, CSRF, transport deadlines and failure dropping remain
unchanged. There is no new meter, service, schema or storage of diagnostics.

The two new root regressions first fail against the prior implementation. The
uncaught case exercises an actual React root outside React's test-only act queue,
which deliberately rethrows instead of invoking its root callback. The recovered
case directly exercises the registered callback; it is not a native recovery
scenario. The final combined 31 web cases pass, covering callback counts,
private-field getter traps, listener disposal, development behavior, telemetry
bounds/kind restrictions, router fallback and existing surface admission. All
seven API host cases pass with exact fixed tags, anonymous/CSRF denial and atomic
rejection of private/unknown fields and timing or success payloads. Locked API
build reports zero warnings/errors; web/browser TypeScript, targeted lint and
production web build pass.

Both native desktop/phone runtime cases pass against the frozen production web
bundle, compiled Production API, restricted PostgreSQL and Nginx. A real DOM
listener throws and a real promise remains unhandled; another rejection is
handled and does not increment the count. Actual reports return 204 with exactly
one count per category, excluding the private sentinel, draft and scope/account
identities. Production browser diagnostics exclude the sentinel. The dirty Card
title remains intact, no canonical work mutation is sent and the complete
persisted Lists/Cards snapshot is unchanged. These are compiled-source local
observations, not current immutable-image release acceptance.

The same final invocation also passes all four existing routed Board/Card
desktop/phone failure and explicit-reload scenarios: **six native cases pass in
3.2 minutes**. Both disposable API/web containers are removed after the terminal
run, preserving the original three services, images and volumes.

Observation does not prove recovery from a fatal root failure or restore state
already destroyed by a routed failure. It also does not attribute an exception
to a particular work command or establish that command's outcome. Cross-origin
rejections that the browser does not expose, startup failures before observer
installation and anonymous collection remain outside this proof. Correlation-safe
operator diagnostics and collector/dashboard ingestion, full timing/capacity/
accessibility and current immutable-release acceptance remain required. PRD-06
remains open; its current remaining-work estimate is **30%**, a planning estimate.

## Caught routed render failures

App now supplies a fixed MUI route error view instead of React Router's default
diagnostic page. It explains that a submitted change may have completed, warns
that reloading can discard an unsaved draft, and offers an explicit reload.
The fallback does not automatically retry, reload or issue a work command.
Production React caught-error diagnostics print only a fixed message; the router
callback never reports the Error object, message, component stack or route IDs.
Development React diagnostics retain their normal behavior.

The router's once-per-error callback counts render failures as `board_render`,
`card_render` or `application_render`. Only the pathname's structural shape is
used to select a fixed category; no path, query, scope or identity enters the
queue. Non-render loader/action errors are excluded from this observation.
The existing bounded activity queue and `POST /me/activity-client-events`
transport apply, including session/CSRF checks, shared per-user abuse limits,
five-second batching, failure dropping and the three-second transport deadline.
These three categories accept only `kind: exception` and bounded counts, without
duration fields. The server validates the complete batch before recording into
`StrataAI.ActivityClient`; private/unknown fields reject the whole batch.

The actual React/router regressions initially fail on all three missing report
categories, then pass. The final combined 22 web cases include fixed fallback
content, private-diagnostic suppression, exact aggregate reports, render-kind
restrictions and the existing surface-admission boundary. Three API host cases
pass, proving accepted counter values and fixed tags, anonymous/CSRF denial,
and atomic rejection of messages, stacks, paths, IDs, keys and durations.
The locked API build has zero warnings/errors. Web/browser TypeScript, targeted
lint and production build pass.

All four native desktop/phone Board/Card cases pass in one 2.2-minute invocation
against the frozen production bundle, compiled Production API, restricted
PostgreSQL and Nginx. A controlled malformed read forces a genuine React failure;
the plain fallback contains no private sentinel, production console diagnostics
do not expose it, and the actual telemetry POST receives 204 with a fixed
exception count and no identities/content. Explicit keyboard reload recovers
the real canonical screen; the complete persisted Lists/Cards remain unchanged.
The request assertion excludes navigation observations and SignalR negotiation
from canonical work mutations. Initial fixture failures came from matching the
SPA document in the read interceptor and counting those observation/transport
requests as work commands; both assumptions were corrected before the final
complete run. No product assertion, limiter or timing budget was weakened.
Disposable API/web containers are removed after the terminal run, preserving
the original three services and saved volumes. This is compiled-source local
evidence, not current immutable-image acceptance.

This closed a scoped routed-render observation gap. The later runtime exception
coverage above adds root, event-handler and unhandled-promise counts; this route
fallback still does not recover an original in-memory command intent or dirty
draft after its feature tree is destroyed.
Anonymous failures use the same fallback but are not collected by the
authenticated endpoint. Operator collector
ingestion/dashboard evidence, complete timing/capacity/accessibility acceptance
and current immutable-release execution also remain required. PRD-06 remains
open; its estimate at this routed-render increment was **31%**. The runtime
section above records the current planning estimate.

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
