using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Organizations;
using StrataAI.Domain.Organizations;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task PRD_27_configuration_HTTP_replay_pages_original_body_free_sources_and_resets_foreign_cursors()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (actor, parent) = await ConfigurationParent(owner, "Private replay parent", ct);
        var path = $"/organizations/{parent.Id}/configuration/events";
        using var bootstrap = await owner.GetAsync(path, ct); Assert.Equal(HttpStatusCode.OK, bootstrap.StatusCode);
        Assert.True(bootstrap.Headers.CacheControl!.Private); Assert.True(bootstrap.Headers.CacheControl.NoStore);
        var initial = await bootstrap.Content.ReadFromJsonAsync<OrganizationConfigurationSyncPage>(ct);
        Assert.True(initial!.ResetRequired); Assert.Empty(initial.Events);
        List<OrganizationConfigurationRecord> sources = [];
        for (var version = 0; version < 3; version++)
        {
            using var write = await Mutate(owner, HttpMethod.Patch, $"/organizations/{parent.Id}/configuration",
                new { version, configuration = new OrganizationConfigurationData("Never publish private legal name", "CA-BC", "UTC") }, Guid.NewGuid().ToString("D"));
            Assert.Equal(HttpStatusCode.OK, write.StatusCode);
            sources.Add((await write.Content.ReadFromJsonAsync<OrganizationConfigurationRecord>(ct))!);
        }
        var cursor = initial.Cursor;
        for (var index = 0; index < 3; index++)
        {
            using var response = await owner.GetAsync(path + "?limit=1&cursor=" + Uri.EscapeDataString(cursor), ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.DoesNotContain("Never publish private legal name", await response.Content.ReadAsStringAsync(ct));
            var page = await response.Content.ReadFromJsonAsync<OrganizationConfigurationSyncPage>(ct);
            Assert.False(page!.ResetRequired); Assert.False(page.Pending); Assert.Equal(index < 2, page.HasMore);
            var source = Assert.Single(page.Events);
            Assert.Equal(sources[index].EventId, source.EventId); Assert.Equal(sources[index].UpdatedAt, source.CreatedAt);
            Assert.Equal(actor, source.ActorId); Assert.Equal(index + 1, source.Version);
            Assert.Equal(sources[index].CorrelationId, source.CorrelationId); cursor = page.Cursor;
        }
        var metadata = await owner.GetFromJsonAsync<JsonElement>($"/organizations/{parent.Id}/metadata-events", ct);
        foreach (var foreign in new[] { "tampered", metadata.GetProperty("cursor").GetString()! })
        {
            var reset = await owner.GetFromJsonAsync<OrganizationConfigurationSyncPage>(path + "?cursor=" + Uri.EscapeDataString(foreign), ct);
            Assert.True(reset!.ResetRequired); Assert.Empty(reset.Events); Assert.False(reset.Pending);
        }
        var (_, other) = await ConfigurationParent(owner, "Second private replay", ct);
        var second = await owner.GetFromJsonAsync<OrganizationConfigurationSyncPage>($"/organizations/{other.Id}/configuration/events?cursor=" + Uri.EscapeDataString(cursor), ct);
        Assert.True(second!.ResetRequired); Assert.Empty(second.Events);
    }

    [Theory]
    [InlineData("?limit=0", "invalid_sync_limit")]
    [InlineData("?limit=101", "invalid_sync_limit")]
    [InlineData("?limit=invalid", "invalid_sync_limit")]
    [InlineData("?limit=1&limit=2", "invalid_sync_limit")]
    [InlineData("?cursor=a&cursor=b", "invalid_configuration_event_cursor")]
    public async Task PRD_27_configuration_HTTP_replay_checks_private_admission_before_query_validation(string query, string code)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var member = app.CreateClient();
        await RegisterAndLogin(owner); await RegisterAndLogin(member);
        var (_, parent) = await ConfigurationParent(owner, "Private query admission", ct);
        var memberId = (await member.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        await app.Services.GetRequiredService<IOrganizationStore>().AddOrRestoreMemberAsync(parent.Id, memberId, OrganizationRole.Member, DateTimeOffset.UtcNow, ct);
        var path = $"/organizations/{parent.Id}/configuration/events";
        using var denied = await member.GetAsync(path + query, ct);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.Equal("organization_not_found", (await denied.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
        using var missing = await owner.GetAsync($"/organizations/{Guid.NewGuid()}/configuration/events" + query, ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var invalid = await owner.GetAsync(path + query, ct); Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(code, (await invalid.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
        using var swapped = new HttpRequestMessage(HttpMethod.Get, path);
        swapped.Headers.Add("X-StrataAI-Expected-Actor", memberId.ToString("D"));
        using var actorDenied = await owner.SendAsync(swapped, ct); Assert.Equal(HttpStatusCode.Unauthorized, actorDenied.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PRD_27_configuration_live_delivers_original_sources_then_closes_after_admin_or_session_withdrawal(bool demotion)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var admin = app.CreateClient();
        await RegisterAndLogin(owner); var cookie = await RegisterAndLogin(admin);
        var (ownerId, parent) = await ConfigurationParent(owner, "Live configuration parent", ct);
        var actor = (await admin.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        var organizations = app.Services.GetRequiredService<IOrganizationStore>();
        await organizations.AddOrRestoreMemberAsync(parent.Id, actor, OrganizationRole.Admin, DateTimeOffset.UtcNow, ct);
        using var socket = await LiveSocket(app, cookie, "/organizations/live/configuration");
        await SendFrame(socket, new { type = 4, invocationId = "configuration", target = "Watch", arguments = new object?[] { parent.Id.ToString("D"), null } });
        var initial = await StreamItem(socket);
        Assert.Equal(parent.Id, initial.GetProperty("organizationId").GetGuid()); Assert.Equal(actor, initial.GetProperty("userId").GetGuid());
        Assert.True(initial.GetProperty("page").GetProperty("resetRequired").GetBoolean());
        Assert.Empty(initial.GetProperty("page").GetProperty("events").EnumerateArray());
        using var write = await Mutate(owner, HttpMethod.Patch, $"/organizations/{parent.Id}/configuration",
            new { version = 0, configuration = new OrganizationConfigurationData("Private live legal name", "CA-BC", "UTC") }, Guid.NewGuid().ToString("D"));
        Assert.Equal(HttpStatusCode.OK, write.StatusCode);
        var source = (await write.Content.ReadFromJsonAsync<OrganizationConfigurationRecord>(ct))!;
        var changed = await StreamItem(socket); Assert.DoesNotContain("Private live legal name", changed.GetRawText());
        var delivered = Assert.Single(changed.GetProperty("page").GetProperty("events").EnumerateArray());
        Assert.Equal(source.EventId, delivered.GetProperty("eventId").GetGuid());
        Assert.Equal(ownerId, delivered.GetProperty("actorId").GetGuid());
        Assert.Equal(source.UpdatedAt, delivered.GetProperty("createdAt").GetDateTimeOffset());
        if (demotion) await organizations.AddOrRestoreMemberAsync(parent.Id, actor, OrganizationRole.Member, DateTimeOffset.UtcNow, ct);
        else
        {
            using var logout = await Mutate(admin, HttpMethod.Post, "/auth/logout", new { });
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(8));
        var closed = false;
        try
        {
            while (!closed)
            {
                var frame = await Frame(socket, deadline.Token);
                closed = frame is null || frame.Value.TryGetProperty("type", out var type) && type.GetInt32() is 3 or 7;
                if (frame is not null && !closed) Assert.NotEqual(2, frame.Value.GetProperty("type").GetInt32());
            }
        }
        catch (WebSocketException) { closed = true; }
        Assert.True(closed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("http://evil.example.test")]
    public async Task PRD_27_configuration_live_refuses_missing_or_foreign_origin(string? origin)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var client = app.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/organizations/live/configuration/negotiate?negotiateVersion=1");
        request.Headers.Add("X-StrataAI-Request", "1"); if (origin is not null) request.Headers.Add("Origin", origin);
        using var response = await client.SendAsync(request, ct); Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("realtime_origin_denied", (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task PRD_27_configuration_live_denies_an_ordinary_member_without_any_stream_item()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var member = app.CreateClient();
        await RegisterAndLogin(owner); var cookie = await RegisterAndLogin(member);
        var (_, parent) = await ConfigurationParent(owner, "Member stream boundary", ct);
        var actor = (await member.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        await app.Services.GetRequiredService<IOrganizationStore>().AddOrRestoreMemberAsync(parent.Id, actor, OrganizationRole.Member, DateTimeOffset.UtcNow, ct);
        using var socket = await LiveSocket(app, cookie, "/organizations/live/configuration");
        await SendFrame(socket, new { type = 4, invocationId = "denied", target = "Watch", arguments = new object?[] { parent.Id.ToString("D"), null } });
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(8));
        var closed = false;
        try
        {
            while (!closed)
            {
                var frame = await Frame(socket, deadline.Token);
                closed = frame is null || frame.Value.TryGetProperty("type", out var type) && type.GetInt32() is 3 or 7;
                if (frame is not null) Assert.NotEqual(2, frame.Value.GetProperty("type").GetInt32());
            }
        }
        catch (WebSocketException) { closed = true; }
        Assert.True(closed);
    }

    [Fact]
    public async Task PRD_27_configuration_live_limits_subscriptions_and_releases_a_cancelled_stream()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); var cookie = await RegisterAndLogin(owner);
        var (_, parent) = await ConfigurationParent(owner, "Configuration subscription ownership", ct);
        using var socket = await LiveSocket(app, cookie, "/organizations/live/configuration");
        await SendFrame(socket, new { type = 4, invocationId = "first", target = "Watch", arguments = new object?[] { parent.Id.ToString("D"), null } });
        var initial = await StreamItem(socket); var cursor = initial.GetProperty("page").GetProperty("cursor").GetString();
        await SendFrame(socket, new { type = 4, invocationId = "duplicate", target = "Watch", arguments = new object?[] { parent.Id.ToString("D"), cursor } });
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(8));
        var rejected = await Frame(socket, deadline.Token); Assert.NotNull(rejected);
        Assert.Equal(3, rejected.Value.GetProperty("type").GetInt32()); Assert.Equal("duplicate", rejected.Value.GetProperty("invocationId").GetString());
        Assert.Contains("subscription_limit", rejected.Value.GetProperty("error").GetString());
        await SendFrame(socket, new { type = 5, invocationId = "first" });
        var completed = await Frame(socket, deadline.Token); Assert.NotNull(completed);
        Assert.Equal(3, completed.Value.GetProperty("type").GetInt32()); Assert.Equal("first", completed.Value.GetProperty("invocationId").GetString());
        await SendFrame(socket, new { type = 4, invocationId = "replacement", target = "Watch", arguments = new object?[] { parent.Id.ToString("D"), cursor } });
        var replacement = await StreamItem(socket);
        Assert.False(replacement.GetProperty("page").GetProperty("resetRequired").GetBoolean());
        Assert.Empty(replacement.GetProperty("page").GetProperty("events").EnumerateArray());
    }
}
