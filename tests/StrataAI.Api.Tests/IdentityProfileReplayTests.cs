using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    // PRD-02/60-TC-07/08: retry acknowledgment, collision, actor isolation and post-revocation denial.
    [Theory]
    [InlineData("/me")]
    [InlineData("/me/")]
    public async Task Profile_retry_serializes_duplicate_edits_and_reproves_the_current_session(string route)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var first = app.CreateClient(); using var other = app.CreateClient();
        await RegisterAndLogin(first); await RegisterAndLogin(other);
        var key = Guid.NewGuid().ToString();
        var body = new { displayName = "Saved once", version = 1 };
        var responses = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Mutate(first, HttpMethod.Patch, route, body, key)));
        string? acknowledgment = null;
        foreach (var response in responses)
        {
            using (response)
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var json = await response.Content.ReadAsStringAsync(ct);
                acknowledgment ??= json; Assert.Equal(acknowledgment, json);
            }
        }
        var replay = await first.GetFromJsonAsync<JsonElement>("/me/sync?after=1", ct);
        Assert.Equal(2, replay.GetProperty("profile").GetProperty("version").GetInt64());
        Assert.Single(replay.GetProperty("events").EnumerateArray());
        using var collision = await Mutate(first, HttpMethod.Patch, route, new { displayName = "Different", version = 1 }, key);
        Assert.Equal(HttpStatusCode.Conflict, collision.StatusCode);
        Assert.Equal("idempotency_key_reused", (await collision.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
        using var stale = await Mutate(first, HttpMethod.Patch, route, body, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var isolated = await Mutate(other, HttpMethod.Patch, route, body, key);
        Assert.Equal(HttpStatusCode.OK, isolated.StatusCode);
        var firstId = replay.GetProperty("profile").GetProperty("id").GetGuid();
        Assert.NotEqual(firstId, (await isolated.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid());
        using var logout = await Mutate(first, HttpMethod.Post, "/auth/logout", new { });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var denied = await Mutate(first, HttpMethod.Patch, route, body, key);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.DoesNotContain("Saved once", await denied.Content.ReadAsStringAsync(ct));
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("11111111-1111-1111-1111-111111111111,22222222-2222-2222-2222-222222222222")]
    public async Task Profile_retry_rejects_invalid_keys_without_advancing_state(string key)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var client = app.CreateClient();
        await RegisterAndLogin(client);
        using var invalid = await Mutate(client, HttpMethod.Patch, "/me", new { displayName = "Denied", version = 1 }, key);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("invalid_idempotency_key", (await invalid.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
        var unchanged = await client.GetFromJsonAsync<JsonElement>("/me/sync?after=1", ct);
        Assert.Equal(1, unchanged.GetProperty("profile").GetProperty("version").GetInt64());
        Assert.Empty(unchanged.GetProperty("events").EnumerateArray());
    }
}
