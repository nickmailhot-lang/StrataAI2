# Runtime modes

StrataAI2 has two explicit runtime modes controlled by `STRATAAI_RUNTIME_MODE`.

## Demo

`demo` is deterministic, local, and isolated from production providers.

- PostgreSQL is not required.
- Production AI, mailbox, OAuth, and vendor integrations are not resolved.
- Versioned sample data is packaged in code.
- `GET /api/demo/state` returns current sample state.
- `POST /api/demo/reset` restores the deterministic sample state.
- `DELETE /api/demo/state` clears it.

These endpoints are not mapped in Production.

Authenticated invitation commands also maintain a private, process-local
[recipient source journal and replay reader](invitation-recipient-events.md).
Its source history and counters commit or roll back with the existing invitation
transaction. API restart resets this Demo history; sample-catalog reset endpoints
do not reset it. Both modes expose protected recipient SignalR replay, consumed
by the invitations page for future actual invitation transitions. See the linked
journal guide for protected recovery, unfinished authority invalidation and
remaining native acceptance evidence.

Demo also journals canonical Organization creation/editing, membership changes
and Internal Organization invitation transitions in the original transaction.
Both modes expose [protected metadata HTTP and SignalR replay](organization-metadata-replay.md#demo-metadata-replay),
including browser draft preservation and reconnect recovery. Demo publication is
synchronous in-memory simulation; Production retains its separate durable Worker.
API restart resets the journal, while sample-catalog reset leaves it intact.

Demo automatically processes committed deletion requests through a
[bounded graph simulation](organization-deletion-lifecycle.md#demo-bounded-graph-simulation)
hosted inside the Demo API. It traverses actual attachments, Cards, Lists and
Boards, retains history, then commits the terminal Organization audit and
canonical completion source together. Protected Owner observations and member
HTTP/SignalR lifecycle recovery consume that source. A 202 acknowledges the
request; an empty graph or unavailable ordinary read never establishes completion.

This simulation is process-local and does not use external providers. Failed
processing restores the graph, history, parent and source before retrying. The
accepted request supplies the original actor authority independently of a
browser session; current account/membership/session checks still protect reads.
API restart loses this Demo state, while sample-catalog reset leaves it intact.
Production retains its separate Worker, restricted PostgreSQL durable jobs,
leases and transactional outbox. Demo completion does not prove physical object
erasure or backup expiration.

### Demo sign-in

Set `STRATAAI_RUNTIME_MODE=demo`, start the API and web application, and open
the web application's `/login` page. Each fresh Demo API process seeds this
verified, active account; no registration or email verification is required:

| Field | Value |
| --- | --- |
| Email | `demo@strataai.test` |
| Password | `StrataAI-Demo-2026!` |
| Display name | Demo User |

The account signs in through the normal password and session flow. It starts
without Organization memberships; create an Organization after signing in to
explore the collaboration workflows. The `/api/demo/state` sample catalog is
separate from authenticated Organization membership and work data.

The Demo API also starts with ASP.NET's Development service validation enabled
when binary attachment storage is disabled. Its attachment services compose
without resolving an object-storage provider: protected reads return no bytes,
and binary uploads remain unavailable. URL attachments retain their existing
workflow.

These are public test credentials, seeded only in Demo's in-memory identity
store. Production uses its PostgreSQL identity store and does not seed this
account. Account changes and sessions last for the current API process. Restart
the Demo API to restore the original credentials; `/api/demo/reset` and
`DELETE /api/demo/state` affect only the sample catalog, not accounts or sessions.

Return to the [project README](../../README.md#demo-sign-in) or the
[documentation index](../README.md).

`DemoAccountTests.cs` covers sign-in with these documented credentials without
registration, rejection of a wrong password without a session, and Development
startup with binary attachment storage disabled. All three focused API-host
checks passed again on 2026-10-07. The seeded account has no implicit Organization
access; these checks do not establish complete authentication PRD acceptance.

## Production

`production` fails startup unless `ConnectionStrings__Postgres` is configured.
Production never falls back to sample persistence.

Both API and Worker expose safe runtime/build diagnostics and readiness. Environment-specific
configuration remains outside container images.
