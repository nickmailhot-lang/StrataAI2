using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Runtime;
using StrataAI.Infrastructure.Identity;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("{\"key\":null}")]
    [InlineData("{\"key\":123}")]
    [InlineData("{\"key\":\"private-invalid-secret\"}")]
    [InlineData("{\"key\":\"private-invalid-secret\",\"key\":\"duplicate-private-secret\"}")]
    public void Login_retry_production_configuration_rejects_invalid_rings_without_disclosing_values(string ring)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["STRATAAI_AUTH_RETRY_CURRENT_KEY"] = "key", ["STRATAAI_AUTH_RETRY_KEYS"] = ring,
        }).Build();
        var error = Assert.Throws<InvalidOperationException>(() => services.AddStrataAiIdentity(configuration, new RuntimeDescriptor(RuntimeMode.Production, "test", "test")));
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("private-invalid-secret", error.ToString());
        Assert.DoesNotContain("duplicate-private-secret", error.ToString());
        Assert.Equal("Production sign-in retry keys require a valid current version and a unique JSON key ring of base64 32-byte secrets.", error.Message);
    }

    [Fact]
    public async Task Login_retry_key_is_scoped_to_the_credential_proven_account()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var first = app.CreateClient(); using var second = app.CreateClient();
        await RegisterAndLogin(first); await RegisterAndLogin(second);
        var key = Guid.NewGuid().ToString();
        var firstProfile = await first.GetFromJsonAsync<JsonElement>("/me", ct);
        var secondProfile = await second.GetFromJsonAsync<JsonElement>("/me", ct);
        using var one = await Mutate(first, HttpMethod.Post, "/auth/login", new { email = firstProfile.GetProperty("email").GetString(), password = "api-host-correct-horse" }, key);
        using var two = await Mutate(second, HttpMethod.Post, "/auth/login", new { email = secondProfile.GetProperty("email").GetString(), password = "api-host-correct-horse" }, key);
        Assert.Equal(HttpStatusCode.OK, one.StatusCode); Assert.Equal(HttpStatusCode.OK, two.StatusCode);
        Assert.NotEqual(one.Headers.GetValues("Set-Cookie").Single().Split(';')[0], two.Headers.GetValues("Set-Cookie").Single().Split(';')[0]);
        Assert.Equal(secondProfile.GetProperty("id").GetGuid(), (await two.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("user").GetProperty("id").GetGuid());
        using var invalid = await Mutate(first, HttpMethod.Post, "/auth/login", new { email = firstProfile.GetProperty("email").GetString(), password = "api-host-correct-horse" }, Guid.Empty.ToString());
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode); Assert.False(invalid.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Login_retry_uses_retained_original_key_and_fails_closed_when_it_is_removed()
    {
        var ct = TestContext.Current.CancellationToken;
        using var old = new IdentityLoginRetrySecrets("old", new Dictionary<string, string> { ["old"] = Convert.ToBase64String(new byte[32]) });
        using var rotated = new IdentityLoginRetrySecrets("new", new Dictionary<string, string> {
            ["old"] = Convert.ToBase64String(new byte[32]), ["new"] = Convert.ToBase64String(Enumerable.Repeat((byte)1, 32).ToArray()) });
        using var removed = new IdentityLoginRetrySecrets("new", new Dictionary<string, string> { ["new"] = Convert.ToBase64String(Enumerable.Repeat((byte)1, 32).ToArray()) });
        var secrets = new RotatingLoginSecrets(old);
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton<IIdentityLoginRetrySecrets>(secrets));
        using var client = app.CreateClient(); await RegisterAndLogin(client);
        var user = await client.GetFromJsonAsync<JsonElement>("/me", ct);
        var body = new { email = user.GetProperty("email").GetString(), password = "api-host-correct-horse" };
        var key = Guid.NewGuid().ToString();
        using var first = await Mutate(client, HttpMethod.Post, "/auth/login", body, key);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var cookie = first.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
        secrets.Current = rotated;
        using var retained = await Mutate(client, HttpMethod.Post, "/auth/login", body, key);
        Assert.Equal(HttpStatusCode.OK, retained.StatusCode);
        Assert.Equal(cookie, retained.Headers.GetValues("Set-Cookie").Single().Split(';')[0]);
        secrets.Current = removed;
        using var denied = await Mutate(client, HttpMethod.Post, "/auth/login", body, key);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, denied.StatusCode);
        Assert.False(denied.Headers.Contains("Set-Cookie"));
        Assert.Equal("identity_retry_key_unavailable", (await denied.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
    }

    private sealed class RotatingLoginSecrets(IIdentityLoginRetrySecrets initial) : IIdentityLoginRetrySecrets
    {
        public IIdentityLoginRetrySecrets Current { get; set; } = initial;
        public string CurrentKeyVersion => Current.CurrentKeyVersion;
        public bool TryDeriveSession(Guid userId, Guid sessionId, string keyVersion, out string token) => Current.TryDeriveSession(userId, sessionId, keyVersion, out token);
        public bool TryFingerprint(Guid userId, Guid intentKey, string emailNormalized, string password, string keyVersion, out string fingerprint) => Current.TryFingerprint(userId, intentKey, emailNormalized, password, keyVersion, out fingerprint);
    }

    [Fact]
    public async Task Login_retry_denies_an_expired_original_session_without_issuing_a_cookie()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new ReceiptTestClock();
        clock.UtcNow = new DateTimeOffset(clock.UtcNow.UtcTicks / 10 * 10 + 7, TimeSpan.Zero);
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton<IClock>(clock));
        using var client = app.CreateClient();
        await RegisterAndLogin(client);
        var user = await client.GetFromJsonAsync<JsonElement>("/me", ct);
        var body = new { email = user.GetProperty("email").GetString(), password = "api-host-correct-horse" };
        var key = Guid.NewGuid().ToString();
        using var first = await Mutate(client, HttpMethod.Post, "/auth/login", body, key);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var acknowledgment = await first.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(0, acknowledgment.GetProperty("sessionExpiresAt").GetDateTimeOffset().UtcTicks % 10);
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
