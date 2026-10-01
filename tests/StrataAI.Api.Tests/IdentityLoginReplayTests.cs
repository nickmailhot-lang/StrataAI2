using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Common;
using StrataAI.Infrastructure.Identity;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task Login_retry_denies_an_expired_original_session_without_issuing_a_cookie()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new ReceiptTestClock();
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton<IClock>(clock));
        using var client = app.CreateClient();
        await RegisterAndLogin(client);
        var user = await client.GetFromJsonAsync<JsonElement>("/me", ct);
        var body = new { email = user.GetProperty("email").GetString(), password = "api-host-correct-horse" };
        var key = Guid.NewGuid().ToString();
        using var first = await Mutate(client, HttpMethod.Post, "/auth/login", body, key);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        clock.UtcNow = clock.UtcNow.AddHours(13);
        using var expired = await Mutate(client, HttpMethod.Post, "/auth/login", body, key);
        Assert.Equal(HttpStatusCode.Conflict, expired.StatusCode);
        Assert.Equal("idempotency_key_expired", (await expired.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
        Assert.False(expired.Headers.Contains("Set-Cookie"));
        using var fresh = await Mutate(client, HttpMethod.Post, "/auth/login", body, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.OK, fresh.StatusCode);
    }

    [Fact]
    public async Task Login_retries_return_the_original_cookie_and_current_profile_but_never_revive_logout()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var client = app.CreateClient();
        await RegisterAndLogin(client);
        var user = await client.GetFromJsonAsync<JsonElement>("/me", ct);
        var body = new { email = user.GetProperty("email").GetString(), password = "api-host-correct-horse" };
        var key = Guid.NewGuid().ToString();
        var replies = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Mutate(client, HttpMethod.Post, "/auth/login", body, key)));
        string? cookie = null; string? expires = null;
        foreach (var reply in replies)
        {
            using (reply)
            {
                Assert.Equal(HttpStatusCode.OK, reply.StatusCode);
                var currentCookie = reply.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
                cookie ??= currentCookie; Assert.Equal(cookie, currentCookie);
                var json = await reply.Content.ReadFromJsonAsync<JsonElement>(ct);
                var currentExpiry = json.GetProperty("sessionExpiresAt").GetString();
                expires ??= currentExpiry; Assert.Equal(expires, currentExpiry);
            }
        }
        using var wrong = await Mutate(client, HttpMethod.Post, "/auth/login", new { body.email, password = "incorrect-password" }, key);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.False(wrong.Headers.Contains("Set-Cookie"));
        using var edit = await Mutate(client, HttpMethod.Patch, "/me", new { displayName = "Current profile", version = 1 });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        using var replay = await Mutate(client, HttpMethod.Post, "/auth/login", body, key);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(cookie, replay.Headers.GetValues("Set-Cookie").Single().Split(';')[0]);
        Assert.Equal("Current profile", (await replay.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("user").GetProperty("displayName").GetString());
        using var logout = await Mutate(client, HttpMethod.Post, "/auth/logout", new { });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var revoked = await Mutate(client, HttpMethod.Post, "/auth/login", body, key);
        Assert.Equal(HttpStatusCode.Conflict, revoked.StatusCode);
        Assert.False(revoked.Headers.Contains("Set-Cookie"));
        using var fresh = await Mutate(client, HttpMethod.Post, "/auth/login", body, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.OK, fresh.StatusCode);
        Assert.NotEqual(cookie, fresh.Headers.GetValues("Set-Cookie").Single().Split(';')[0]);
    }

    [Fact]
    public void Login_retry_secrets_preserve_retained_versions_and_separate_sessions_from_intents()
    {
        var oldKey = Convert.ToBase64String(Enumerable.Repeat((byte)17, 32).ToArray());
        var newKey = Convert.ToBase64String(Enumerable.Repeat((byte)29, 32).ToArray());
        using var original = new IdentityLoginRetrySecrets("old", new Dictionary<string, string> { ["old"] = oldKey });
        using var rotated = new IdentityLoginRetrySecrets("new", new Dictionary<string, string> { ["old"] = oldKey, ["new"] = newKey });
        using var retired = new IdentityLoginRetrySecrets("new", new Dictionary<string, string> { ["new"] = newKey });
        var user = Guid.NewGuid(); var session = Guid.NewGuid(); var intent = Guid.NewGuid();
        Assert.True(original.TryDeriveSession(user, session, "old", out var token));
        Assert.True(rotated.TryDeriveSession(user, session, "old", out var retained)); Assert.Equal(token, retained);
        Assert.False(retired.TryDeriveSession(user, session, "old", out _));
        Assert.True(rotated.TryDeriveSession(Guid.NewGuid(), session, "old", out var otherUser)); Assert.NotEqual(token, otherUser);
        Assert.True(rotated.TryDeriveSession(user, Guid.NewGuid(), "old", out var otherSession)); Assert.NotEqual(token, otherSession);
        Assert.True(rotated.TryFingerprint(user, intent, "person@example.test", "private-password", "old", out var fingerprint));
        Assert.Equal(64, fingerprint.Length); Assert.DoesNotContain("private-password", fingerprint);
        Assert.True(rotated.TryFingerprint(user, Guid.NewGuid(), "person@example.test", "private-password", "old", out var otherIntent)); Assert.NotEqual(fingerprint, otherIntent);
        Assert.True(rotated.TryFingerprint(user, intent, "person@example.test", "changed-password", "old", out var changed)); Assert.NotEqual(fingerprint, changed);
        Assert.Throws<ArgumentException>(() => new IdentityLoginRetrySecrets("missing", new Dictionary<string, string> { ["old"] = oldKey }));
        Assert.Throws<ArgumentException>(() => new IdentityLoginRetrySecrets("old", new Dictionary<string, string> { ["old"] = Convert.ToBase64String(new byte[31]) }));
    }
}
