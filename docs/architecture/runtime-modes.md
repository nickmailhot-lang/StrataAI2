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
do not reset it. Recipient SignalR/browser delivery remains unfinished.

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

These are public test credentials, seeded only in Demo's in-memory identity
store. Production uses its PostgreSQL identity store and does not seed this
account. Account changes and sessions last for the current API process. Restart
the Demo API to restore the original credentials; `/api/demo/reset` and
`DELETE /api/demo/state` affect only the sample catalog, not accounts or sessions.

Return to the [project README](../../README.md#demo-sign-in) or the
[documentation index](../README.md).

## Production

`production` fails startup unless `ConnectionStrings__Postgres` is configured.
Production never falls back to sample persistence.

Both API and Worker expose safe runtime/build diagnostics and readiness. Environment-specific
configuration remains outside container images.
