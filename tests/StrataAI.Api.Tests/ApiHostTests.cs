using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace StrataAI.Api.Tests;

// ARCH-01-AC-001/003, ARCH-09-FR-002, PRD-02-TC-01/03/04/07,
// PRD-24-TC-04: real endpoint binding, middleware and session authorization.
public sealed class ApiHostTests
{
    [Fact]
    public async Task Host_isolated_demo_health_and_build_identity_are_explicit()
    {
        await using var app = new ApiFactory();
        using var client = app.CreateClient();
        using var health = await client.GetAsync("/readyz", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        var body = await health.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("demo", body.GetProperty("mode").GetString());
        using var runtime = await client.GetAsync("/api/runtime", TestContext.Current.CancellationToken);
        var descriptor = await runtime.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken);
        var embedded = StrataAI.Infrastructure.Runtime.BuildIdentityReader.Read(typeof(Program).Assembly);
        Assert.Equal(embedded.Revision, descriptor.GetProperty("revision").GetString());
        Assert.Equal(embedded.Version, descriptor.GetProperty("version").GetString());
        Assert.NotEqual("api-host-test", descriptor.GetProperty("revision").GetString());
        Assert.True(runtime.Headers.Contains("X-Correlation-ID"));
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, "cross-site")]
    [InlineData(true, "same-site")]
    public async Task Rejected_browser_mutation_preserves_live_session(bool intent, string? site)
    {
        await using var app = new ApiFactory();
        using var client = app.CreateClient();
        await RegisterAndLogin(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/logout");
        if (intent) request.Headers.Add("X-StrataAI-Request", "1");
        if (site is not null) request.Headers.Add("Sec-Fetch-Site", site);
        using var blocked = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Equal("csrf_rejected", (await blocked.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken)).GetProperty("code").GetString());
        using var current = await client.GetAsync("/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
    }

    [Fact]
    public async Task Logout_revokes_the_copied_session_cookie_not_only_the_browser_cookie()
    {
        await using var app = new ApiFactory();
        using var client = app.CreateClient();
        var cookie = await RegisterAndLogin(client);
        using var replay = app.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        replay.DefaultRequestHeaders.Add("Cookie", cookie);
        Assert.Equal(HttpStatusCode.OK, (await replay.GetAsync("/me", TestContext.Current.CancellationToken)).StatusCode);
        using var logout = await Mutate(client, HttpMethod.Post, "/auth/logout", new { });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var denied = await replay.GetAsync("/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
    }

    [Fact]
    public async Task Stale_profile_request_cannot_overwrite_authoritative_update()
    {
        await using var app = new ApiFactory();
        using var client = app.CreateClient();
        await RegisterAndLogin(client);
        var initial = await client.GetFromJsonAsync<JsonElement>("/me", TestContext.Current.CancellationToken);
        var version = initial.GetProperty("version").GetInt64();
        using var saved = await Mutate(client, HttpMethod.Patch, "/me", new { displayName = "Authoritative", version });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        using var stale = await Mutate(client, HttpMethod.Patch, "/me", new { displayName = "Stale", version });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("version_conflict", (await stale.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken)).GetProperty("code").GetString());
        var final = await client.GetFromJsonAsync<JsonElement>("/me", TestContext.Current.CancellationToken);
        Assert.Equal("Authoritative", final.GetProperty("displayName").GetString());
        Assert.Equal(version + 1, final.GetProperty("version").GetInt64());
    }

    [Fact]
    public async Task Authentication_required_before_organization_and_profile_disclosure()
    {
        await using var app = new ApiFactory();
        using var client = app.CreateClient();
        foreach (var route in new[] { "/me", "/organizations/" })
        {
            using var response = await client.GetAsync(route, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.False(response.Headers.Contains("Location"));
        }
    }

    [Theory]
    [InlineData("PRIVATE", HttpStatusCode.NotFound)]
    [InlineData("PUBLIC", HttpStatusCode.OK)]
    public async Task Board_visibility_and_organization_mutation_are_server_authorized(string visibility, HttpStatusCode readStatus)
    {
        await using var app = new ApiFactory();
        using var owner = app.CreateClient();
        using var outsider = app.CreateClient();
        using var visitor = app.CreateClient();
        await RegisterAndLogin(owner);
        await RegisterAndLogin(outsider);
        using var createdOrganization = await Mutate(owner, HttpMethod.Post, "/organizations/", new { name = "Host Organization" });
        Assert.Equal(HttpStatusCode.Created, createdOrganization.StatusCode);
        var organization = await createdOrganization.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken);
        var organizationId = organization.GetProperty("organization").GetProperty("id").GetGuid();
        using var createdBoard = await Mutate(owner, HttpMethod.Post, "/boards", new { organizationId, name = "Restricted host fixture", visibility });
        Assert.Equal(HttpStatusCode.Created, createdBoard.StatusCode);
        var board = await createdBoard.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken);
        var route = $"/boards/{board.GetProperty("id").GetGuid()}";
        foreach (var client in new[] { outsider, visitor })
        {
            using var response = await client.GetAsync(route, TestContext.Current.CancellationToken);
            Assert.Equal(readStatus, response.StatusCode);
            if (readStatus == HttpStatusCode.NotFound)
                Assert.DoesNotContain("Restricted host fixture", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }
        using var forbiddenCreate = await Mutate(outsider, HttpMethod.Post, "/boards", new { organizationId, name = "Unauthorized", visibility });
        Assert.Equal(HttpStatusCode.NotFound, forbiddenCreate.StatusCode);
        using var unchanged = await owner.GetAsync(route, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, unchanged.StatusCode);
    }

    [Fact]
    public void Production_without_database_configuration_fails_closed()
    {
        using var app = new ApiFactory("production");
        var failure = Assert.ThrowsAny<Exception>(() => app.CreateClient());
        Assert.Contains("Production mode requires a database connection string or complete runtime credentials.", failure.ToString());
    }

    private static async Task<string> RegisterAndLogin(HttpClient client)
    {
        var credentials = new { email = $"host-{Guid.NewGuid():N}@example.test", password = "api-host-correct-horse", displayName = "API host" };
        using var registered = await Mutate(client, HttpMethod.Post, "/auth/register", credentials);
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        using var loggedIn = await Mutate(client, HttpMethod.Post, "/auth/login", credentials);
        Assert.Equal(HttpStatusCode.OK, loggedIn.StatusCode);
        var cookie = Assert.Single(loggedIn.Headers.GetValues("Set-Cookie"));
        Assert.Contains("httponly", cookie.ToLowerInvariant());
        return cookie.Split(';')[0];
    }

    private static async Task<HttpResponseMessage> Mutate(HttpClient client, HttpMethod method, string route, object body)
    {
        using var request = new HttpRequestMessage(method, route) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-StrataAI-Request", "1");
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}

internal sealed class ApiFactory(string mode = "demo") : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Minimal-host startup reads runtime settings before Build(). Supply
        // host configuration early, without process-wide environment mutation.
        builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["STRATAAI_RUNTIME_MODE"] = mode,
            ["STRATAAI_BUILD_REVISION"] = "api-host-test",
            ["STRATAAI_BUILD_VERSION"] = "runtime-spoof",
            ["STRATAAI_IDENTITY_EMAIL_ENABLED"] = "false",
            ["ConnectionStrings:Postgres"] = "",
            ["Logging:LogLevel:Default"] = "Warning",
        }));
        return base.CreateHost(builder);
    }
}
