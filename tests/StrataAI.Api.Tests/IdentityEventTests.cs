using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    // AUTH-FR-004/006, PRD-02/60-TC-05/07: reset invalidates all sessions and publishes once without secrets.
    [Fact]
    public async Task Password_reset_revokes_every_session_and_publishes_one_content_free_event()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var first = app.CreateClient(); using var other = app.CreateClient();
        await RegisterAndLogin(first);
        var initial = await first.GetFromJsonAsync<JsonElement>("/me/sync", ct);
        var profile = initial.GetProperty("profile");
        var email = profile.GetProperty("email").GetString();
        var userId = profile.GetProperty("id").GetGuid();
        using var secondLogin = await Mutate(other, HttpMethod.Post, "/auth/login", new { email, password = "api-host-correct-horse" });
        Assert.Equal(HttpStatusCode.OK, secondLogin.StatusCode);
        using var requested = await Mutate(first, HttpMethod.Post, "/auth/password/forgot", new { email });
        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
        var token = (await requested.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("resetToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));
        using var invalid = await Mutate(first, HttpMethod.Post, "/auth/password/reset", new { token = "invalid-proof", newPassword = "replacement-correct-horse" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var unchanged = await first.GetFromJsonAsync<JsonElement>("/me/sync?after=1", ct);
        Assert.Equal(1, unchanged.GetProperty("profile").GetProperty("version").GetInt64());
        Assert.Empty(unchanged.GetProperty("events").EnumerateArray());
        using var reset = await Mutate(first, HttpMethod.Post, "/auth/password/reset", new { token, newPassword = "replacement-correct-horse" });
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        using var firstDenied = await first.GetAsync("/me/sync?after=1", ct);
        using var otherDenied = await other.GetAsync("/me/sync?after=1", ct);
        Assert.Equal(HttpStatusCode.Unauthorized, firstDenied.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, otherDenied.StatusCode);
        using var oldPassword = await Mutate(other, HttpMethod.Post, "/auth/login", new { email, password = "api-host-correct-horse" });
        Assert.Equal(HttpStatusCode.Unauthorized, oldPassword.StatusCode);
        using var freshLogin = await Mutate(other, HttpMethod.Post, "/auth/login", new { email, password = "replacement-correct-horse" });
        Assert.Equal(HttpStatusCode.OK, freshLogin.StatusCode);
        var replay = await other.GetFromJsonAsync<JsonElement>("/me/sync?after=1", ct);
        var item = Assert.Single(replay.GetProperty("events").EnumerateArray());
        Assert.Equal("SESSION_REVOKED", item.GetProperty("eventType").GetString());
        Assert.Equal(userId, item.GetProperty("actorId").GetGuid());
        Assert.Equal(userId, item.GetProperty("entityId").GetGuid());
        Assert.Equal(2, item.GetProperty("version").GetInt64());
        Assert.Equal(2, replay.GetProperty("cursor").GetInt64());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("organizationId").ValueKind);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("boardId").ValueKind);
        Assert.Empty(item.GetProperty("metadata").EnumerateObject());
        Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("correlationId").GetString()));
        Assert.DoesNotContain(token!, replay.GetRawText());
        Assert.DoesNotContain("replacement-correct-horse", replay.GetRawText());
        using var duplicate = await Mutate(other, HttpMethod.Post, "/auth/password/reset", new { token, newPassword = "another-replacement-horse" });
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        var afterDuplicate = await other.GetFromJsonAsync<JsonElement>("/me/sync?after=2", ct);
        Assert.Equal(2, afterDuplicate.GetProperty("profile").GetProperty("version").GetInt64());
        Assert.Empty(afterDuplicate.GetProperty("events").EnumerateArray());
    }

    // PRD-02/60-TC-08/09: atomic snapshot cursor and ordered, content-free subject replay.
    [Fact]
    public async Task Identity_replay_pairs_profile_and_cursor_and_recovers_profile_changes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var client = app.CreateClient();
        await RegisterAndLogin(client);
        var initial = await client.GetFromJsonAsync<JsonElement>("/me/sync", ct);
        var user = initial.GetProperty("profile").GetProperty("id").GetGuid();
        Assert.Empty(initial.GetProperty("events").EnumerateArray());
        Assert.Equal(1, initial.GetProperty("cursor").GetInt64());
        using var edit = await Mutate(client, HttpMethod.Patch, "/me", new { displayName = "Event recovery", version = 1 });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        var replay = await client.GetFromJsonAsync<JsonElement>("/me/sync?after=1", ct);
        Assert.Equal("Event recovery", replay.GetProperty("profile").GetProperty("displayName").GetString());
        Assert.Equal(2, replay.GetProperty("cursor").GetInt64());
        Assert.False(replay.GetProperty("hasMore").GetBoolean());
        var item = Assert.Single(replay.GetProperty("events").EnumerateArray());
        Assert.Equal("USER_PROFILE_UPDATED", item.GetProperty("eventType").GetString());
        Assert.Equal(user, item.GetProperty("entityId").GetGuid());
        Assert.Equal(2, item.GetProperty("version").GetInt64());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("organizationId").ValueKind);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("boardId").ValueKind);
        Assert.Empty(item.GetProperty("metadata").EnumerateObject());
        var duplicate = await client.GetFromJsonAsync<JsonElement>("/me/sync?after=1", ct);
        Assert.Equal(item.GetProperty("eventId").GetGuid(), duplicate.GetProperty("events")[0].GetProperty("eventId").GetGuid());
    }

    // PRD-02/60-TC-04/05: another account's event history and revoked sessions are denied.
    [Fact]
    public async Task Identity_replay_is_subject_scoped_and_requires_a_live_session()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var first = app.CreateClient(); using var other = app.CreateClient();
        await RegisterAndLogin(first); await RegisterAndLogin(other);
        var own = await other.GetFromJsonAsync<JsonElement>("/me/sync?after=0", ct);
        var otherId = own.GetProperty("profile").GetProperty("id").GetGuid();
        Assert.All(own.GetProperty("events").EnumerateArray(), item => Assert.Equal(otherId, item.GetProperty("entityId").GetGuid()));
        using var logout = await Mutate(other, HttpMethod.Post, "/auth/logout", new { });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var denied = await other.GetAsync("/me/sync?after=0", ct);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using var permitted = await first.GetAsync("/me/sync?after=0", ct);
        Assert.Equal(HttpStatusCode.OK, permitted.StatusCode);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(999)]
    public async Task Identity_replay_rejects_invalid_or_future_cursors(long cursor)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var client = app.CreateClient();
        await RegisterAndLogin(client);
        using var denied = await client.GetAsync($"/me/sync?after={cursor}", ct);
        Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        Assert.Equal("invalid_identity_cursor", (await denied.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
    }
}
