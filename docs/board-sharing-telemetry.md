# Board-sharing request telemetry

The API emits native .NET instruments from meter `StrataAI.BoardSharing`, owned
by its dependency-injection meter factory. This adds no datastore, collector,
deployable service, package or public metrics endpoint. Authorized operators can
consume the instruments through their configured .NET diagnostics/metrics tools.
Listeners must remain non-blocking. A throwing listener is isolated from the
business response and cannot replace a committed acknowledgment with an error.

| Instrument | Unit | Meaning |
| --- | --- | --- |
| `strataai.board_sharing.requests` | request | One observed matched request, including rejection/repeated attempts |
| `strataai.board_sharing.duration` | seconds | Server pipeline duration through response execution |

Covered operations are Board snapshot read, member directory read, role change,
removal, visibility change, and bound Board invitation creation/history/revocation.
Timing starts before normal database security/routing/authentication/authorization
checks. Unmatched routes, pre-routing failures without a selected endpoint and
other application surfaces are excluded. Invitation provider delivery runs in the
separate Worker and is not included in this synchronous API duration.

The four labels are fixed operation, outcome, allowlisted stable error code and a
boolean indicating a syntactically valid retry-key header. No retry key itself is
recorded. Keyed attempts count request attempts, not actual successful replay or
distinct committed commands. Outcomes distinguish success, denied/not-found,
conflict, rate limiting, unavailability, invalid input and cancellation. A 404
never reveals whether a protected Board exists. Authentication/model-binding/
middleware rejections use bounded fallback categories when no endpoint problem
code is available. Unknown problem codes become `other_error`.

The implementation never reads request/response bodies for metrics. Endpoint
filters inspect only the existing problem envelope's code and apply an allowlist.
Labels exclude Organization, Board, invitation, member, actor and correlation IDs;
route values/query strings; names/titles/emails; proofs, cookies and headers other
than the retry-header boolean. Metrics neither persist audit events nor change
authorization, receipts, domain events, queue publication or response contents.
Cancellation/unknown response delivery does not imply a committed action failed.

Three API-host cases use isolated native meter listeners to verify count/duration
observations for success, invalid input/key, stale-version conflict, anonymous and
protected denial, plus all three Board invitation administration operations. They
check bounded label keys/values and actual bearer-bearing Demo invitation responses
without labeling recipient/proof data. A throwing listener case verifies the
authoritative visibility response and persisted refresh remain intact. Current
warnings-as-errors build and diff checks pass; Linux host execution and exact-image
regression evidence remain pending.

This is server request instrumentation, not complete PRD-05 observability. Operator
collector/export/retention configuration, production p50/p95 dashboards, client
feature-open/use counts, user-visible retry and exception reporting, explicit
realtime reconnect/recovery metrics and performance acceptance remain outstanding.
Histograms provide observations; they do not establish latency targets or complete
the ticket. Audit history remains the authoritative record of business actions.
