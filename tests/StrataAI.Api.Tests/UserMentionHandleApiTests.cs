using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using StrataAI.Application.Identity;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Theory]
    [InlineData("/me/mention-handle")]
    [InlineData("/me/mention-handle/")]
    public async Task Account_handle_http_retries_preserve_original_versions_and_reprove_cookie(string route)
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var first = app.CreateClient(); using var other = app.CreateClient();
        var cookie = await RegisterAndLogin(first); await RegisterAndLogin(other);
        var initial = await first.GetFromJsonAsync<UserMentionHandleSetting>(route, ct); Assert.NotNull(initial);
        Assert.Equal($"u_{initial.UserId:N}", initial.Handle); Assert.Equal(1, initial.UserVersion); Assert.Equal(1, initial.HandleVersion);
        var key = Guid.NewGuid().ToString(); var input = new ClaimMentionHandleInput(" ALICE ", 1, 1);
        var responses = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Mutate(first, HttpMethod.Patch, route, input, key)));
        foreach (var response in responses)
        {
            using (response)
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(response.Headers.CacheControl!.NoStore);
                Assert.Equal(new HandleClaimAcknowledgment(initial.UserId, "alice", 2, 2, true),
                    await response.Content.ReadFromJsonAsync<HandleClaimAcknowledgment>(ct));
            }
        }
        using var changedIntent = await Mutate(first, HttpMethod.Patch, route, input with { Handle = "different" }, key);
        Assert.Equal(HttpStatusCode.Conflict, changedIntent.StatusCode);
        Assert.Equal("idempotency_key_reused", (await changedIntent.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
        using var collision = await Mutate(other, HttpMethod.Patch, route, input, key);
        Assert.Equal(HttpStatusCode.Conflict, collision.StatusCode);
        Assert.Equal("mention_handle_unavailable", (await collision.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
        using var independent = await Mutate(other, HttpMethod.Patch, route, input with { Handle = "another" }, key);
        Assert.Equal(HttpStatusCode.OK, independent.StatusCode);
        Assert.NotEqual(initial.UserId, (await independent.Content.ReadFromJsonAsync<HandleClaimAcknowledgment>(ct))!.UserId);
        using var peer = await Mutate(first, HttpMethod.Patch, "/me", new { displayName = "Peer profile", version = 2 }, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.OK, peer.StatusCode);
        using var recovered = await Mutate(first, HttpMethod.Patch, route, input with { Handle = "alice" }, key);
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        Assert.Equal(2, (await recovered.Content.ReadFromJsonAsync<HandleClaimAcknowledgment>(ct))!.UserVersion);
        var current = await first.GetFromJsonAsync<UserMentionHandleSetting>(route, ct); Assert.Equal(3, current!.UserVersion);
        using var renamed = await Mutate(first, HttpMethod.Patch, route, new ClaimMentionHandleInput("renamed", 3, 2), Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        using var former = await Mutate(first, HttpMethod.Patch, route, input, key);
        Assert.Equal(HttpStatusCode.Conflict, former.StatusCode); Assert.DoesNotContain("alice", await former.Content.ReadAsStringAsync(ct));
        using var logout = await Mutate(first, HttpMethod.Post, "/auth/logout", new { }); Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var revoked = app.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        revoked.DefaultRequestHeaders.Add("Cookie", cookie);
        using var deniedRead = await revoked.GetAsync(route, ct); Assert.Equal(HttpStatusCode.Unauthorized, deniedRead.StatusCode);
        using var deniedRetry = await Mutate(revoked, HttpMethod.Patch, route, input, key);
        Assert.Equal(HttpStatusCode.Unauthorized, deniedRetry.StatusCode);
        Assert.DoesNotContain("alice", await deniedRetry.Content.ReadAsStringAsync(ct));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("11111111-1111-1111-1111-111111111111,22222222-2222-2222-2222-222222222222")]
    public async Task Account_handle_http_requires_one_original_key_and_retains_state_on_refusal(string? key)
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory(); using var client = app.CreateClient();
        await RegisterAndLogin(client); var initial = await client.GetFromJsonAsync<UserMentionHandleSetting>("/me/mention-handle", ct);
        using var invalid = await Mutate(client, HttpMethod.Patch, "/me/mention-handle", new ClaimMentionHandleInput("valid", 1, 1), key);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode); Assert.True(invalid.Headers.CacheControl!.NoStore);
        Assert.Equal("invalid_idempotency_key", (await invalid.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
        Assert.Equal(initial, await client.GetFromJsonAsync<UserMentionHandleSetting>("/me/mention-handle", ct));
        foreach (var reserved in new[] { "card", "board", "u_foreign", "nïck" })
        {
            using var refusal = await Mutate(client, HttpMethod.Patch, "/me/mention-handle", new ClaimMentionHandleInput(reserved, 1, 1), Guid.NewGuid().ToString());
            Assert.Equal(HttpStatusCode.BadRequest, refusal.StatusCode);
            Assert.Equal("mention_handle_invalid", (await refusal.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
        }
        var events = await client.GetFromJsonAsync<JsonElement>("/me/sync?after=1", ct);
        Assert.Empty(events.GetProperty("events").EnumerateArray());
    }

    [Fact]
    public async Task Account_handle_http_has_no_foreign_lookup_and_requires_csrf_proof()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory(); using var client = app.CreateClient();
        using var anonymous = await client.GetAsync("/me/mention-handle", ct); Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        await RegisterAndLogin(client);
        using var foreign = await client.GetAsync($"/me/mention-handle/{Guid.NewGuid()}", ct); Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        using var request = new HttpRequestMessage(HttpMethod.Patch, "/me/mention-handle") { Content = JsonContent.Create(new ClaimMentionHandleInput("valid", 1, 1)) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        using var noProof = await client.SendAsync(request, ct); Assert.Equal(HttpStatusCode.Forbidden, noProof.StatusCode);
        Assert.Equal(1, (await client.GetFromJsonAsync<UserMentionHandleSetting>("/me/mention-handle", ct))!.UserVersion);
    }
}
