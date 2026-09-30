# StrataAI2 architecture

The GitHub architecture PRDs (ARCH-01 through ARCH-12) are authoritative.

## Dependency direction

```text
StrataAI.Api ---------+
                      |
                      v
              StrataAI.Application
                      |
                      v
                StrataAI.Domain

StrataAI.Worker ------+

StrataAI.Infrastructure implements interfaces consumed by Application and may depend on
Application/Domain. Domain must not depend on ASP.NET Core, PostgreSQL, provider SDKs,
Docker, AI providers, email providers, or object storage implementations.
```

## Runtime processes

- **API** — HTTP, authentication/authorization, commands/queries, realtime endpoints.
- **Worker** — durable/background work such as email intake, AI processing, indexing,
  notifications and recurring tasks.

Both are part of one modular-monolith product and share the same domain/application code.
