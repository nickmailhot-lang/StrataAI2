# StrataAI2

StrataAI2 is a strata/condominium governance and operations platform built from the PRD backlog in this repository.

## Adopted architecture

StrataAI2 is a modular monolith delivered as a small containerized system:

- React + TypeScript + Vite + MUI web application
- ASP.NET Core API
- Separate .NET Worker process from the same codebase
- PostgreSQL as the primary relational datastore
- PostgreSQL RLS for defence-in-depth Organization isolation
- pgvector for semantic retrieval when AI retrieval is introduced
- Object storage for document/file binaries
- SignalR for realtime collaboration
- PostgreSQL-backed transactional outbox/durable jobs initially
- Immutable web/API/Worker Docker images produced by CI

The canonical product hierarchy is:

```text
User -> Organization -> Board -> List -> Card
```

`Organization` is the domain-facing tenant boundary. Infrastructure may use `tenant_id` to represent the same boundary.

## Repository layout

```text
apps/
  web/                       React/MUI SPA (introduced by ARCH-02)
src/
  StrataAI.Domain/           Domain entities and rules
  StrataAI.Application/      Use cases and application contracts
  StrataAI.Infrastructure/   Persistence and provider adapters
  StrataAI.Api/              HTTP/auth/realtime host
  StrataAI.Worker/           Background processing host
tests/                       Automated test projects
docs/                        Architecture/ADR/product documentation
scripts/                     Operational/developer scripts
.github/workflows/           CI and repository automation
```

## Current implementation sequence

Work is processed in dependency order from the GitHub PRD and architecture issues. Initial commits establish ARCH-01/ARCH-03 foundations and the PRD-01 canonical hierarchy before authentication, persistence, Kanban behavior, realtime, AI, and portal features are layered on.

## Toolchains

- .NET 10 SDK
- Node.js 24 for web tooling
- Docker/Compose for the deployable runtime

From a clean checkout, run the local source checks at the repository root:

```sh
npm ci
npm run typecheck
npm run lint
npm test
npm run build
dotnet restore StrataAI2.slnx --locked-mode
dotnet build StrataAI2.slnx --configuration Release --no-restore
dotnet test tests/StrataAI.Domain.Tests/StrataAI.Domain.Tests.csproj --configuration Release --no-build
dotnet test tests/StrataAI.Api.Tests/StrataAI.Api.Tests.csproj --configuration Release --no-build
```

The API host tests select an isolated Demo runtime automatically; they do not
require database or provider credentials. See
[API host testing](docs/architecture/api-host-testing.md) and
[dependency locking](docs/architecture/dependency-locking.md). CI additionally
requires real PostgreSQL isolation tests, exact-release-image integration,
browser workflows and security scans before producing the tested Docker bundle.

See GitHub issues `ARCH-01` through `ARCH-12` and `PRD-01` through `PRD-80` for the authoritative implementation requirements.
