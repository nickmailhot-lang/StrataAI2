# Kanban command telemetry

The existing operator meter `StrataAI.BoardSharing` now covers PATCH list
commands as `list_update` (rename/rank/move) and POST card movement as
`card_move`. It records native `strataai.board_sharing.requests` counters and
`strataai.board_sharing.duration` histograms in seconds, including security and
command processing. Existing Board reads already use `board_read`.

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
Linux CI. Operator collector/export, dashboards, alerting, client exceptions,
reconnect/conflict rates, user-visible retry events and browser timing/performance
acceptance remain incomplete. This instrumentation does not complete PRD-06.
