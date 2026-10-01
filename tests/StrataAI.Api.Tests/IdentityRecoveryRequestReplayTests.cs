using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Infrastructure.Identity;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task Recovery_request_retry_retains_original_key_and_masks_retirement_like_an_unknown_account()
    {
        var ct = TestContext.Current.CancellationToken;
        using var old = new IdentityLoginRetrySecrets("old", new Dictionary<string, string> { ["old"] = Convert.ToBase64String(new byte[32]) });
        using var rotated = new IdentityLoginRetrySecrets("new", new Dictionary<string, string> { ["old"] = Convert.ToBase64String(new byte[32]), ["new"] = Convert.ToBase64String(Enumerable.Repeat((byte)1, 32).ToArray()) });
        using var retired = new IdentityLoginRetrySecrets("new", new Dictionary<string, string> { ["new"] = Convert.ToBase64String(Enumerable.Repeat((byte)1, 32).ToArray()) });
        var secrets = new RotatingRecoverySecrets(old);
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton<IIdentityRecoveryRetrySecrets>(secrets));
        using var client = app.CreateClient();
        var email = $"recovery-rotation-{Guid.NewGuid():N}@example.test";
        using var account = await Mutate(client, HttpMethod.Post, "/auth/register", new { email, password = "initial-correct-horse", displayName = "Recovery rotation" });
        Assert.Equal(HttpStatusCode.Created, account.StatusCode);
        var key = Guid.NewGuid().ToString();
        using var first = await Mutate(client, HttpMethod.Post, "/auth/password/forgot", new { email }, key);
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        var original = await first.Content.ReadAsStringAsync(ct);
        secrets.Current = rotated;
        using var retained = await Mutate(client, HttpMethod.Post, "/auth/password/forgot", new { email }, key);
        Assert.Equal(original, await retained.Content.ReadAsStringAsync(ct));
        secrets.Current = retired;
        using var denied = await Mutate(client, HttpMethod.Post, "/auth/password/forgot", new { email }, key);
        using var unknown = await Mutate(client, HttpMethod.Post, "/auth/password/forgot", new { email = "unknown-rotation@example.test" }, key);
        Assert.Equal(HttpStatusCode.Accepted, denied.StatusCode);
        Assert.Equal(await unknown.Content.ReadAsStringAsync(ct), await denied.Content.ReadAsStringAsync(ct));
        Assert.False(denied.Headers.Contains("Set-Cookie"));
    }
    private sealed class RotatingRecoverySecrets(IIdentityRecoveryRetrySecrets initial) : IIdentityRecoveryRetrySecrets
    {
        public IIdentityRecoveryRetrySecrets Current { get; set; } = initial;
        public string CurrentKeyVersion => Current.CurrentKeyVersion;
        public bool TryDeriveRecovery(Guid userId, Guid tokenId, IdentityTokenPurpose purpose, string version, out string token) => Current.TryDeriveRecovery(userId, tokenId, purpose, version, out token);
        public bool TryRecoveryFingerprint(Guid userId, Guid key, IdentityTokenPurpose purpose, string email, string version, out string fingerprint) => Current.TryRecoveryFingerprint(userId, key, purpose, email, version, out fingerprint);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Recovery_request_retries_preserve_one_original_proof_and_do_not_replace_consumed_tokens(bool verification)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton(
            new IdentityPolicy(true, verification, 12, TimeSpan.FromHours(12), TimeSpan.FromMinutes(30))));
        using var client = app.CreateClient();
        var email = $"recovery-retry-{Guid.NewGuid():N}@example.test";
        using var registered = await Mutate(client, HttpMethod.Post, "/auth/register", new { email, password = "initial-correct-horse", displayName = "Recovery retry" });
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        var id = (await registered.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("user").GetProperty("id").GetGuid();
        var route = verification ? "/auth/verification/resend" : "/auth/password/forgot";
        var tokenField = verification ? "verificationToken" : "resetToken";
        var purpose = verification ? IdentityTokenPurpose.VerifyEmail : IdentityTokenPurpose.ResetPassword;
        var key = Guid.NewGuid();
        var replies = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Mutate(client, HttpMethod.Post, route, new { email }, key.ToString())));
        string? first = null;
        foreach (var reply in replies)
        {
            using (reply) {
                Assert.Equal(HttpStatusCode.Accepted, reply.StatusCode);
                var json = await reply.Content.ReadAsStringAsync(ct);
                first ??= json; Assert.Equal(first, json);
            }
        }
        var token = JsonSerializer.Deserialize<JsonElement>(first!).GetProperty(tokenField).GetString()!;
        Assert.NotEmpty(token);
        var receipt = await app.Services.GetRequiredService<IIdentityRecoveryRequestReplayStore>().ReadAsync(id, key, purpose, ct);
        Assert.NotNull(receipt); Assert.DoesNotContain(token, JsonSerializer.Serialize(receipt));
        using var consumed = verification
            ? await Mutate(client, HttpMethod.Post, "/auth/verify-email", new { token })
            : await Mutate(client, HttpMethod.Post, "/auth/password/reset", new { token, newPassword = "replacement-correct-horse" });
        Assert.Equal(HttpStatusCode.OK, consumed.StatusCode);
        using var replay = await Mutate(client, HttpMethod.Post, route, new { email }, key.ToString());
        Assert.Equal(HttpStatusCode.Accepted, replay.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await replay.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty(tokenField).ValueKind);
        Assert.Equal(receipt, await app.Services.GetRequiredService<IIdentityRecoveryRequestReplayStore>().ReadAsync(id, key, purpose, ct));
        using var unknown = await Mutate(client, HttpMethod.Post, route, new { email = "unknown-private-account@example.test" }, key.ToString());
        Assert.Equal(await replay.Content.ReadAsStringAsync(ct), await unknown.Content.ReadAsStringAsync(ct));
    }

    [Fact]
    public async Task Recovery_request_retry_does_not_renew_expired_tokens_and_a_new_intent_is_explicit()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new ReceiptTestClock();
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton<IClock>(clock));
        using var client = app.CreateClient();
        var email = $"recovery-expiry-{Guid.NewGuid():N}@example.test";
        using var registered = await Mutate(client, HttpMethod.Post, "/auth/register", new { email, password = "initial-correct-horse", displayName = "Recovery expiry" });
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        var key = Guid.NewGuid().ToString();
        using var first = await Mutate(client, HttpMethod.Post, "/auth/password/forgot", new { email }, key);
        var original = (await first.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("resetToken").GetString();
        clock.UtcNow = clock.UtcNow.AddMinutes(31);
        using var expired = await Mutate(client, HttpMethod.Post, "/auth/password/forgot", new { email }, key);
        Assert.Equal(HttpStatusCode.Accepted, expired.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await expired.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("resetToken").ValueKind);
        using var fresh = await Mutate(client, HttpMethod.Post, "/auth/password/forgot", new { email }, Guid.NewGuid().ToString());
        Assert.NotEqual(original, (await fresh.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("resetToken").GetString());
    }
}
