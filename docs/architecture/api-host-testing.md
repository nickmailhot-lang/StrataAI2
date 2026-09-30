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
