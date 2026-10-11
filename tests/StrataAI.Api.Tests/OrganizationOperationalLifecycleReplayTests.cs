using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Organizations;
using Xunit;

namespace StrataAI.Api.Tests;
public sealed partial class ApiHostTests
{
    [Fact]
    public async Task PRD_27_Demo_operational_lifecycle_replay_is_private_current_member_scoped_and_content_free()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var member = app.CreateClient(); using var outsider = app.CreateClient();
        await RegisterAndLogin(owner); await RegisterAndLogin(member); await RegisterAndLogin(outsider);
        var (actor, parent) = await ConfigurationParent(owner, "Private lifecycle parent", ct);
        var memberId = (await member.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        var organizations = app.Services.GetRequiredService<IOrganizationStore>();
        var added = await app.Services.GetRequiredService<IOrganizationUnitOfWork>().ExecuteAsync<bool>(parent.Id, actor, memberId, false,
            async () => { await organizations.AddOrRestoreMemberAsync(parent.Id, memberId, OrganizationRole.Member, DateTimeOffset.UtcNow, ct); return OrganizationOperation<bool>.Success(true); }, ct);
        Assert.True(added.Succeeded);
        var suspended = await app.Services.GetRequiredService<OrganizationOperationalLifecycleService>().ChangeAsync(parent.Id, actor,
            OrganizationStatus.Suspended, parent.Version, Guid.NewGuid(), "Private lifecycle correlation", ct); Assert.True(suspended.Succeeded);
        var route = $"/organizations/{parent.Id}/operational-lifecycle/events";
        using var response = await member.GetAsync(route, ct); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.Private); Assert.True(response.Headers.CacheControl.NoStore);
        var page = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(memberId, page.GetProperty("actorId").GetGuid()); Assert.Equal("SUSPENDED", page.GetProperty("status").GetString());
        var fact = Assert.Single(page.GetProperty("events").EnumerateArray()); Assert.Equal(suspended.Value!.EventId, fact.GetProperty("eventId").GetGuid());
        Assert.Equal(11, fact.EnumerateObject().Count()); Assert.False(fact.TryGetProperty("actorLabel", out _));
        Assert.False(fact.TryGetProperty("correlationId", out _)); Assert.False(fact.TryGetProperty("name", out _));
        using var denied = await outsider.GetAsync(route, ct); Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        using var adminReview = await member.GetAsync($"/organizations/{parent.Id}/operational-lifecycle", ct); Assert.Equal(HttpStatusCode.NotFound, adminReview.StatusCode);
        using var malformed = await member.GetAsync(route + "?afterVersion=2&afterVersion=2", ct); Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        using var future = await member.GetAsync(route + "?afterVersion=999", ct); Assert.Equal(HttpStatusCode.BadRequest, future.StatusCode);
        using var wrongActor = new HttpRequestMessage(HttpMethod.Get, route); wrongActor.Headers.Add("X-StrataAI-Expected-Actor", actor.ToString());
        using var switched = await member.SendAsync(wrongActor, ct); Assert.Equal(HttpStatusCode.Unauthorized, switched.StatusCode);
    }
    [Fact]
    public async Task PRD_27_Demo_operational_lifecycle_replay_resumes_fifty_published_sources_in_revision_order()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (actor, parent) = await ConfigurationParent(owner, "Lifecycle replay pages", ct);
        var lifecycle = app.Services.GetRequiredService<OrganizationOperationalLifecycleService>();
        for (var version = 1; version <= 51; version++) Assert.True((await lifecycle.ChangeAsync(parent.Id, actor,
            version % 2 == 1 ? OrganizationStatus.Suspended : OrganizationStatus.Active, version, Guid.NewGuid(), "replay-page", ct)).Succeeded);
        var route = $"/organizations/{parent.Id}/operational-lifecycle/events";
        var first = await owner.GetFromJsonAsync<JsonElement>(route, ct); Assert.Equal(50, first.GetProperty("events").GetArrayLength());
        Assert.Equal(51, first.GetProperty("nextVersion").GetInt64());
        var next = await owner.GetFromJsonAsync<JsonElement>(route + "?afterVersion=51", ct);
        Assert.Equal(52, Assert.Single(next.GetProperty("events").EnumerateArray()).GetProperty("version").GetInt64());
        Assert.Equal(JsonValueKind.Null, next.GetProperty("nextVersion").ValueKind);
        var empty = await owner.GetFromJsonAsync<JsonElement>(route + "?afterVersion=52", ct); Assert.Empty(empty.GetProperty("events").EnumerateArray());
    }
}
