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

## Production

`production` fails startup unless `ConnectionStrings__Postgres` is configured.
Production never falls back to sample persistence.

Both API and Worker expose safe runtime/build diagnostics and readiness. Environment-specific
configuration remains outside container images.
