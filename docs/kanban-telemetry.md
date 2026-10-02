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
