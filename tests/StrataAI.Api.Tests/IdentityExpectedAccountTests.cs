using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    // PRD-02-TC-05/07/08: a stale page must not mutate a replacement account.
    [Theory]
    [InlineData("PATCH", "/me")]
    [InlineData("POST", "/me/deactivate")]
    [InlineData("POST", "/auth/logout")]
    [InlineData("PATCH", "/me/mention-handle")]
    public async Task Expected_account_refuses_commands_after_the_cookie_subject_changes(string method, string path)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var original = app.CreateClient(); using var replacement = app.CreateClient();
        await RegisterAndLogin(original); await RegisterAndLogin(replacement);
        var prior = await original.GetFromJsonAsync<JsonElement>("/me", ct);
        var before = await replacement.GetFromJsonAsync<JsonElement>("/me/sync?after=0", ct);
        replacement.DefaultRequestHeaders.Add("X-StrataAI-Expected-User", prior.GetProperty("id").GetGuid().ToString());
        var key = Guid.NewGuid().ToString();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var response = await Mutate(replacement, new HttpMethod(method), path,
                new { displayName = "Old account private draft", version = 1, handle = "old_account_draft", userVersion = 1, handleVersion = 0 }, key);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            var failure = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            Assert.Equal("session_unavailable", failure.GetProperty("code").GetString());
            Assert.DoesNotContain("Old account private draft", failure.ToString());
            Assert.False(response.Headers.Contains("Set-Cookie"));
            Assert.Equal(before.ToString(), (await replacement.GetFromJsonAsync<JsonElement>("/me/sync?after=0", ct)).ToString());
            Assert.Equal(prior.ToString(), (await original.GetFromJsonAsync<JsonElement>("/me", ct)).ToString());
        }
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("invalid-private-expected-account")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("11111111-1111-1111-1111-111111111111,22222222-2222-2222-2222-222222222222")]
    public async Task Expected_account_malformed_proofs_fail_closed_without_reflecting_input(string header)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var client = app.CreateClient(); await RegisterAndLogin(client);
        var before = await client.GetFromJsonAsync<JsonElement>("/me/sync?after=0", ct);
        Assert.True(client.DefaultRequestHeaders.TryAddWithoutValidation("X-StrataAI-Expected-User", header));
        using var response = await Mutate(client, HttpMethod.Patch, "/me", new { displayName = "Forbidden", version = 1 });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var failure = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        Assert.Equal("session_unavailable", failure.GetProperty("code").GetString());
        Assert.DoesNotContain("invalid-private", failure.ToString());
        Assert.Equal(before.ToString(), (await client.GetFromJsonAsync<JsonElement>("/me/sync?after=0", ct)).ToString());
    }
}
