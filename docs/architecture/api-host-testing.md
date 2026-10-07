# API host tests (ARCH-01 / ARCH-03 / ARCH-09)

`tests/StrataAI.Api.Tests` uses xUnit and ASP.NET Core `WebApplicationFactory`
to run the real API entry point, routes, JSON binding, middleware, dependency
injection and session authorization. Its test-only factory supplies runtime
configuration before minimal-host startup, without changing process-wide
environment variables or replacing authentication with a fake scheme. Each test
gets an isolated Demo host and in-memory data stores; no provider credentials or
production database are required.

Run after the locked solution restore and Release build:

```sh
dotnet test tests/StrataAI.Api.Tests/StrataAI.Api.Tests.csproj --configuration Release --no-build
```

CI runs this as a required step in `dotnet-quality`. Tests cover runtime/build
identity, production's missing-database startup denial, browser-intent/fetch-site
rejections with unchanged session state, revocation of a copied session cookie,
profile version conflicts, anonymous account/Organization denial, private/public
Board visibility and unrelated-user mutation denial. A source project-reference
check enforces the production Domain/Application/Infrastructure/API/Worker
dependency direction. Test source comments trace this evidence to ARCH/PRD IDs.

These fast host tests exercise Demo behavior; they do not prove PostgreSQL RLS,
production persistence, provider delivery or Nginx behavior. Existing real
PostgreSQL, exact-release-image, Worker, security and Playwright checks remain
required and complementary. Their checks also cover deployment-specific behavior
that `TestServer` does not reproduce.

This establishes the API test layer, not full ARCH-09 completion. New features
must extend host tests and real-provider/browser coverage for their own rules,
including future AI/search leakage, migrations/upgrades and realtime recovery.

## Local PostgreSQL contracts

With Docker Engine/Desktop running and PowerShell available, run from the
repository root:

```powershell
./scripts/run-local-persistence-contracts.ps1
# Optional: select the host SDK or retain the completed fixture database.
./scripts/run-local-persistence-contracts.ps1 -Dotnet /path/to/dotnet -KeepDatabase
```

The runner restores locked dependencies, builds Release with warnings as errors,
creates a uniquely named disposable PostgreSQL 17/pgvector container, applies all
ordered migrations to its fresh database, and provisions the same restricted
API/Worker roles as CI. It binds a randomly assigned port exclusively to loopback
and uses known disposable test passwords. It executes the full
`StrataAI.Persistence.Contracts` program, including the actual 5,000-active plus
100,000-archived-Card deletion contract. Progress reports are counts and elapsed
times; allow the original process to reach its terminal result.

On Windows the complete contract executable runs inside the Linux .NET SDK
image (`-LinuxSdkImage`, default `mcr.microsoft.com/dotnet/sdk:10.0`). The
production private download preparer intentionally requires Linux; the runner
does not bypass that check or skip attachment coverage. The container copies
source from a read-only mount into ephemeral storage, restores locked packages
and builds the persistence project there, excluding host bin/obj outputs. It
shares only its own PostgreSQL container's network namespace, connecting to
restricted roles at localhost without another exposed port. Host source and
build outputs are not modified by the Linux execution. On Linux the runner
uses the host SDK directly. `-Dotnet` selects the host restore/build SDK.

Existing containers/databases are not reused or reset. A successful run removes
only its own container and anonymous database volume after checking its immutable
ID and unique run label. `-KeepDatabase` retains it. Failed runs also retain their
database and print its name for inspection; inspect the actual failed state
before rerunning. Connection environment variables are restored on exit. The
runner does not restart a test or service based on silence or elapsed time.

These contracts exercise actual PostgreSQL RLS, restricted capabilities,
transactions, normal Worker claims and persistence. Their account/provider seams
are still synthetic. They complement API-host tests and cannot establish real
HTTP cookie behavior, deployed provider authority, browser accessibility or the
exact-image build-once release gate. The runner's PowerShell syntax has been
checked; its complete automated lifecycle remains pending execution. The initial
manual Windows run at `4f427d0` completed with exit code 1. Its complete
105,000-Card deletion mutation/delivery and source recovery contracts passed
(826 bounded mutation jobs, 105,201 ready work events, one terminal, 2,425,361 ms
elapsed, maximum leased mutation page 1,296 ms). Later attachment preview
publication failed at the explicit Linux-only private download boundary.
The original database is preserved as `codex-strataai-contract-20261006`; this
is not a whole-suite pass. The Windows Linux-container route repairs that
observed platform incompatibility; its full lifecycle remains to execute.
