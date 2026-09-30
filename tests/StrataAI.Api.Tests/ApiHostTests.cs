using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

// ARCH-01-AC-001/003, ARCH-09-FR-002, PRD-02-TC-01/03/04/07,
// PRD-24-TC-04: real endpoint binding, middleware and session authorization.
public sealed partial class ApiHostTests
{
    // PRD-07/08-TC-07, PRD-24-TC-05: repeated intent and revoked replay.
    [Fact]
    public async Task Work_retry_keys_deduplicate_concurrent_creation_and_versioned_updates_without_disclosing_revoked_results()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory();
        using var owner = app.CreateClient();
        using var member = app.CreateClient();
        await RegisterAndLogin(owner);
        await RegisterAndLogin(member);
        var userId = (await member.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        using var organizationResponse = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Retry organization" });
        var organizationId = (await organizationResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("organization").GetProperty("id").GetGuid();
        var organizations = app.Services.GetRequiredService<IOrganizationStore>();
        await organizations.AddOrRestoreMemberAsync(organizationId, userId, OrganizationRole.Member, DateTimeOffset.UtcNow, ct);
        var boardKey = Guid.NewGuid().ToString();
        var boardBody = new { organizationId, name = "Protected retry board" };
        using var createdBoard = await Mutate(member, HttpMethod.Post, "/boards", boardBody, boardKey);
        Assert.Equal(HttpStatusCode.Created, createdBoard.StatusCode);
        var board = await createdBoard.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var boardId = board.GetProperty("id").GetGuid();
        var listKey = Guid.NewGuid().ToString();
        var listRoute = $"/boards/{boardId}/lists";
        var duplicates = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Mutate(member, HttpMethod.Post, listRoute, new { name = "One list" }, listKey)));
        Guid? listId = null;
        foreach (var response in duplicates)
        {
            using (response)
            {
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                var id = (await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("id").GetGuid();
                listId ??= id;
                Assert.Equal(listId, id);
            }
        }
        using var collision = await Mutate(member, HttpMethod.Post, listRoute, new { name = "Different list" }, listKey);
        Assert.Equal(HttpStatusCode.Conflict, collision.StatusCode);
        Assert.Equal("idempotency_key_reused", (await collision.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("code").GetString());
        // Another actor has a separate key namespace.
        using var ownerList = await Mutate(owner, HttpMethod.Post, listRoute, new { name = "Owner list" }, listKey);
        Assert.Equal(HttpStatusCode.Created, ownerList.StatusCode);
        using var cardResponse = await Mutate(member, HttpMethod.Post, $"/lists/{listId}/cards", new { title = "Private original" }, Guid.NewGuid().ToString());
        var cardId = (await cardResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("id").GetGuid();
        var editKey = Guid.NewGuid().ToString();
        var edit = new { title = "Saved exactly once", version = 1 };
        using var saved = await Mutate(member, HttpMethod.Patch, $"/cards/{cardId}", edit, editKey);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        using var replayed = await Mutate(member, HttpMethod.Patch, $"/cards/{cardId}", edit, editKey);
        Assert.Equal(HttpStatusCode.OK, replayed.StatusCode);
        Assert.Equal(2, (await replayed.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("version").GetInt64());
        using var stale = await Mutate(member, HttpMethod.Patch, $"/cards/{cardId}", edit, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var snapshot = await member.GetFromJsonAsync<JsonElement>($"/boards/{boardId}", ct);
        Assert.Equal(2, snapshot.GetProperty("lists").GetArrayLength());
        var work = app.Services.GetRequiredService<IWorkManagementStore>();
        await work.RemoveBoardMemberAsync(boardId, userId, DateTimeOffset.UtcNow, ct);
        // Still an organization member: creation replay must check the created board.
        using var revokedCreation = await Mutate(member, HttpMethod.Post, "/boards", boardBody, boardKey);
        Assert.Equal(HttpStatusCode.NotFound, revokedCreation.StatusCode);
        Assert.DoesNotContain("Protected retry board", await revokedCreation.Content.ReadAsStringAsync(ct));
        await organizations.RemoveMemberAsync(organizationId, userId, DateTimeOffset.UtcNow, ct);
        using var revokedEdit = await Mutate(member, HttpMethod.Patch, $"/cards/{cardId}", edit, editKey);
        Assert.Equal(HttpStatusCode.NotFound, revokedEdit.StatusCode);
        Assert.DoesNotContain("Saved exactly once", await revokedEdit.Content.ReadAsStringAsync(ct));
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("11111111-1111-1111-1111-111111111111,22222222-2222-2222-2222-222222222222")]
    public async Task Work_mutations_reject_invalid_retry_keys_before_domain_changes(string key)
    {
        await using var app = new ApiFactory();
        using var client = app.CreateClient();
        await RegisterAndLogin(client);
        using var response = await Mutate(client, HttpMethod.Post, "/boards", new { organizationId = Guid.NewGuid(), name = "Rejected" }, key);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_idempotency_key", (await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Correlation_ids_preserve_safe_values_and_replace_unbounded_or_unsafe_headers()
    {
        await using var app = new ApiFactory();
        using var client = app.CreateClient();
        foreach (var supplied in new[] { new[] { "safe-correlation_01" }, new[] { "contains spaces" }, new[] { new string('a', 128) }, new[] { "secret/audit=detail" }, new[] { "one", "two" } })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/runtime");
            request.Headers.TryAddWithoutValidation("X-Correlation-ID", supplied);
            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var correlationId = Assert.Single(response.Headers.GetValues("X-Correlation-ID"));
            if (supplied[0] == "safe-correlation_01") Assert.Equal(supplied[0], correlationId);
            else Assert.True(Guid.TryParseExact(correlationId, "N", out _));
        }
    }

    [Theory]
    [InlineData("PRIVATE", false)]
    [InlineData("ORGANIZATION", true)]
    [InlineData("PUBLIC", true)]
    public async Task Organization_board_discovery_obeys_board_visibility_and_membership(string visibility, bool initiallyVisible)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory();
        using var owner = app.CreateClient();
        using var member = app.CreateClient();
        using var outsider = app.CreateClient();
        await RegisterAndLogin(owner);
        await RegisterAndLogin(member);
        await RegisterAndLogin(outsider);
        var memberProfile = await member.GetFromJsonAsync<JsonElement>("/me", cancellationToken);
        var userId = memberProfile.GetProperty("id").GetGuid();
        using var organizationResponse = await Mutate(owner, HttpMethod.Post, "/organizations/", new { name = "Discovery organization" });
        var organization = await organizationResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        var organizationId = organization.GetProperty("organization").GetProperty("id").GetGuid();
        await app.Services.GetRequiredService<IOrganizationStore>().AddOrRestoreMemberAsync(organizationId, userId, OrganizationRole.Member, DateTimeOffset.UtcNow, cancellationToken);
        using var boardResponse = await Mutate(owner, HttpMethod.Post, "/boards", new { organizationId, name = "Confidential discovery name", visibility });
        var board = await boardResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        var boardId = board.GetProperty("id").GetGuid();
        var route = $"/organizations/{organizationId}/boards";
        var ownerBoards = await owner.GetFromJsonAsync<JsonElement>(route, cancellationToken);
        Assert.Single(ownerBoards.EnumerateArray());
        var memberBoards = await member.GetFromJsonAsync<JsonElement>(route, cancellationToken);
        Assert.Equal(initiallyVisible ? 1 : 0, memberBoards.GetArrayLength());
        using var denied = await outsider.GetAsync(route, cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.DoesNotContain("Confidential discovery name", await denied.Content.ReadAsStringAsync(cancellationToken));
        var workStore = app.Services.GetRequiredService<IWorkManagementStore>();
        await workStore.UpsertBoardMemberAsync(boardId, userId, BoardRole.Member, DateTimeOffset.UtcNow, cancellationToken);
        Assert.Single((await member.GetFromJsonAsync<JsonElement>(route, cancellationToken)).EnumerateArray());
        await workStore.RemoveBoardMemberAsync(boardId, userId, DateTimeOffset.UtcNow, cancellationToken);
        Assert.Equal(initiallyVisible ? 1 : 0, (await member.GetFromJsonAsync<JsonElement>(route, cancellationToken)).GetArrayLength());
        await workStore.UpsertBoardMemberAsync(boardId, userId, BoardRole.Admin, DateTimeOffset.UtcNow, cancellationToken);
        await app.Services.GetRequiredService<IOrganizationStore>().RemoveMemberAsync(organizationId, userId, DateTimeOffset.UtcNow, cancellationToken);
        using var revokedList = await member.GetAsync(route, cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, revokedList.StatusCode);
        using var revokedRead = await member.GetAsync($"/boards/{boardId}", cancellationToken);
        Assert.Equal(visibility == "PUBLIC" ? HttpStatusCode.OK : HttpStatusCode.NotFound, revokedRead.StatusCode);
        if (visibility == "PUBLIC")
        {
            var snapshot = await revokedRead.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            Assert.False(snapshot.GetProperty("access").GetProperty("canEdit").GetBoolean());
            Assert.False(snapshot.GetProperty("access").GetProperty("canAdminister").GetBoolean());
        }
        using var revokedWrite = await Mutate(member, HttpMethod.Post, $"/boards/{boardId}/lists", new { name = "Revoked contributor write" });
        Assert.Equal(HttpStatusCode.NotFound, revokedWrite.StatusCode);
        var unchanged = await owner.GetFromJsonAsync<JsonElement>($"/boards/{boardId}", cancellationToken);
        Assert.Empty(unchanged.GetProperty("lists").EnumerateArray());
        await workStore.SetBoardLifecycleAsync(boardId, BoardLifecycleState.Active, BoardLifecycleState.Deleted, 1, DateTimeOffset.UtcNow, cancellationToken);
        Assert.Empty((await owner.GetFromJsonAsync<JsonElement>(route, cancellationToken)).EnumerateArray());
    }

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

    private static async Task<HttpResponseMessage> Mutate(HttpClient client, HttpMethod method, string route, object body, string? retryKey = null)
    {
        using var request = new HttpRequestMessage(method, route) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-StrataAI-Request", "1");
        if (retryKey is not null) request.Headers.TryAddWithoutValidation("Idempotency-Key", retryKey);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}

internal sealed class ApiFactory(string mode = "demo", Action<IServiceCollection>? configureServices = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        if (configureServices is not null) builder.ConfigureServices(configureServices);
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
