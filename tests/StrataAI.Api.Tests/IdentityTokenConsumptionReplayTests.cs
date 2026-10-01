using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Infrastructure.Identity;
using Xunit;

namespace StrataAI.Api.Tests;

// PRD-02-TC-05/06/07 and PRD-60-TC-06/07/15: original proof, retry and current lifecycle.

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task Token_consumption_retry_uses_retained_original_key_and_fails_closed_after_retirement()
    {
        var ct = TestContext.Current.CancellationToken;
        using var old = new IdentityLoginRetrySecrets("old", new Dictionary<string, string> { ["old"] = Convert.ToBase64String(new byte[32]) });
        using var rotated = new IdentityLoginRetrySecrets("new", new Dictionary<string, string> { ["old"] = Convert.ToBase64String(new byte[32]), ["new"] = Convert.ToBase64String(Enumerable.Repeat((byte)1, 32).ToArray()) });
        using var retired = new IdentityLoginRetrySecrets("new", new Dictionary<string, string> { ["new"] = Convert.ToBase64String(Enumerable.Repeat((byte)1, 32).ToArray()) });
        var secrets = new RotatingConsumptionSecrets(old);
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton<IIdentityTokenConsumptionRetrySecrets>(secrets));
        using var client = app.CreateClient();
        var email = $"consume-rotation-{Guid.NewGuid():N}@example.test";
        using var registered = await Mutate(client, HttpMethod.Post, "/auth/register", new { email, password = "initial-correct-horse", displayName = "Consumption rotation" });
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        using var request = await Mutate(client, HttpMethod.Post, "/auth/password/forgot", new { email });
        var token = (await request.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("resetToken").GetString()!;
        var body = new { token, newPassword = "replacement-correct-horse" };
        var key = Guid.NewGuid().ToString();
        using var first = await Mutate(client, HttpMethod.Post, "/auth/password/reset", body, key);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var original = await first.Content.ReadAsStringAsync(ct);
        secrets.Current = rotated;
        using var retained = await Mutate(client, HttpMethod.Post, "/auth/password/reset", body, key);
        Assert.Equal(HttpStatusCode.OK, retained.StatusCode);
        Assert.Equal(original, await retained.Content.ReadAsStringAsync(ct));
        secrets.Current = retired;
        using var denied = await Mutate(client, HttpMethod.Post, "/auth/password/reset", body, key);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, denied.StatusCode);
        Assert.Equal("identity_retry_key_unavailable", (await denied.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
        Assert.False(denied.Headers.Contains("Set-Cookie"));
        secrets.Current = old;
        using var restored = await Mutate(client, HttpMethod.Post, "/auth/password/reset", body, key);
        Assert.Equal(original, await restored.Content.ReadAsStringAsync(ct));
    }
    private sealed class RotatingConsumptionSecrets(IIdentityTokenConsumptionRetrySecrets initial) : IIdentityTokenConsumptionRetrySecrets
    {
        public IIdentityTokenConsumptionRetrySecrets Current { get; set; } = initial;
        public string CurrentKeyVersion => Current.CurrentKeyVersion;
        public bool TryConsumptionFingerprint(Guid userId, Guid key, IdentityTokenPurpose purpose, string rawToken, string? newPassword, string version, out string fingerprint)
            => Current.TryConsumptionFingerprint(userId, key, purpose, rawToken, newPassword, version, out fingerprint);
    }
    [Fact]
    public async Task Token_consumption_reset_retry_requires_current_password_and_active_account_before_profile_disclosure()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory();
        using var client = app.CreateClient();
        var email = $"consume-credential-{Guid.NewGuid():N}@example.test";
        using var registered = await Mutate(client, HttpMethod.Post, "/auth/register", new { email, password = "initial-correct-horse", displayName = "Private lifecycle" });
        var id = (await registered.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("user").GetProperty("id").GetGuid();
        using var request = await Mutate(client, HttpMethod.Post, "/auth/password/forgot", new { email });
        var token = (await request.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("resetToken").GetString()!;
        var body = new { token, newPassword = "replacement-correct-horse" };
        var key = Guid.NewGuid().ToString();
        using var first = await Mutate(client, HttpMethod.Post, "/auth/password/reset", body, key);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var store = app.Services.GetRequiredService<IIdentityStore>();
        var original = (await store.FindUserByIdAsync(id, ct))!;
        await store.UpdatePasswordHashAsync(id, app.Services.GetRequiredService<IPasswordHashService>().Hash(id, "different-current-password"), DateTimeOffset.UtcNow, ct);
        using var changed = await Mutate(client, HttpMethod.Post, "/auth/password/reset", body, key);
        Assert.Equal(HttpStatusCode.BadRequest, changed.StatusCode);
        Assert.DoesNotContain(id.ToString(), await changed.Content.ReadAsStringAsync(ct));
        await store.UpdatePasswordHashAsync(id, original.PasswordHash, DateTimeOffset.UtcNow, ct);
        Assert.True(await store.DeactivateUserAsync(id, DateTimeOffset.UtcNow, ct));
        using var inactive = await Mutate(client, HttpMethod.Post, "/auth/password/reset", body, key);
        Assert.Equal(HttpStatusCode.BadRequest, inactive.StatusCode);
        var denial = await inactive.Content.ReadAsStringAsync(ct);
        Assert.DoesNotContain(id.ToString(), denial); Assert.DoesNotContain(email, denial);
        Assert.Equal(2, (await store.ReadEventsAsync(id, 0, ct)).Value!.Events.Count);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Token_consumption_retries_acknowledge_once_and_do_not_repeat_events_or_revoke_newer_sessions(bool verification)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton(
            new IdentityPolicy(true, verification, 12, TimeSpan.FromHours(12), TimeSpan.FromMinutes(30))));
        using var client = app.CreateClient();
        var email = $"consume-retry-{Guid.NewGuid():N}@example.test";
        using var registration = await Mutate(client, HttpMethod.Post, "/auth/register", new { email, password = "initial-correct-horse", displayName = "Consumption retry" });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        var account = await registration.Content.ReadFromJsonAsync<JsonElement>(ct);
        var id = account.GetProperty("user").GetProperty("id").GetGuid();
        string token;
        if (verification) token = account.GetProperty("verificationToken").GetString()!;
        else {
            using var reset = await Mutate(client, HttpMethod.Post, "/auth/password/forgot", new { email });
            token = (await reset.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("resetToken").GetString()!;
        }
        var route = verification ? "/auth/verify-email" : "/auth/password/reset";
        object body = verification ? new { token } : new { token, newPassword = "replacement-correct-horse" };
        var key = Guid.NewGuid();
        var replies = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Mutate(client, HttpMethod.Post, route, body, key.ToString())));
        string? original = null;
        foreach (var reply in replies) using (reply) {
            Assert.Equal(HttpStatusCode.OK, reply.StatusCode);
            var json = await reply.Content.ReadAsStringAsync(ct);
            original ??= json; Assert.Equal(original, json);
            Assert.False(reply.Headers.Contains("Set-Cookie"));
        }
        var events = (await app.Services.GetRequiredService<IIdentityStore>().ReadEventsAsync(id, 0, ct)).Value!.Events;
        Assert.Equal(2, events.Count);
        var purpose = verification ? IdentityTokenPurpose.VerifyEmail : IdentityTokenPurpose.ResetPassword;
        var receipt = await app.Services.GetRequiredService<IIdentityTokenConsumptionReplayStore>().ReadAsync(id, key, purpose, ct);
        Assert.NotNull(receipt); Assert.DoesNotContain(token, JsonSerializer.Serialize(receipt));
        Assert.DoesNotContain("replacement-correct-horse", JsonSerializer.Serialize(receipt));
        using var different = await Mutate(client, HttpMethod.Post, route, body, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.BadRequest, different.StatusCode);
        Assert.DoesNotContain(id.ToString(), await different.Content.ReadAsStringAsync(ct));
        using var signIn = await Mutate(client, HttpMethod.Post, "/auth/login", new { email, password = verification ? "initial-correct-horse" : "replacement-correct-horse" });
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
        using var replay = await Mutate(client, HttpMethod.Post, route, body, key.ToString());
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        using var profile = await client.GetAsync("/me", ct);
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        Assert.Equal(events.Count, (await app.Services.GetRequiredService<IIdentityStore>().ReadEventsAsync(id, 0, ct)).Value!.Events.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Token_consumption_retry_denies_expired_original_proof_without_private_profile_disclosure(bool verification)
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new ReceiptTestClock();
        await using var app = new ApiFactory(configureServices: services => {
            services.AddSingleton<IClock>(clock);
            services.AddSingleton(new IdentityPolicy(true, verification, 12, TimeSpan.FromHours(12), TimeSpan.FromMinutes(30)));
        });
        using var client = app.CreateClient();
        var email = $"consume-expiry-{Guid.NewGuid():N}@example.test";
        using var registration = await Mutate(client, HttpMethod.Post, "/auth/register", new { email, password = "initial-correct-horse", displayName = "Private expiry" });
        var account = await registration.Content.ReadFromJsonAsync<JsonElement>(ct);
        var id = account.GetProperty("user").GetProperty("id").GetGuid();
        string token;
        if (verification) token = account.GetProperty("verificationToken").GetString()!;
        else {
            using var request = await Mutate(client, HttpMethod.Post, "/auth/password/forgot", new { email });
            token = (await request.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("resetToken").GetString()!;
        }
        var key = Guid.NewGuid().ToString();
        var route = verification ? "/auth/verify-email" : "/auth/password/reset";
        object body = verification ? new { token } : new { token, newPassword = "replacement-correct-horse" };
        using var first = await Mutate(client, HttpMethod.Post, route, body, key);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        clock.UtcNow = clock.UtcNow.AddMinutes(31);
        using var expired = await Mutate(client, HttpMethod.Post, route, body, key);
        Assert.Equal(HttpStatusCode.BadRequest, expired.StatusCode);
        var denial = await expired.Content.ReadAsStringAsync(ct);
        Assert.DoesNotContain(id.ToString(), denial); Assert.DoesNotContain(token, denial); Assert.DoesNotContain(email, denial);
        Assert.Equal(2, (await app.Services.GetRequiredService<IIdentityStore>().ReadEventsAsync(id, 0, ct)).Value!.Events.Count);
    }
}
