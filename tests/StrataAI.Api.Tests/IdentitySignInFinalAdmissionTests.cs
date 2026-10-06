using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    // PRD-02-TC-05/06/07: receipt publication is not proof a session is still usable.
    [Fact]
    public async Task Sign_in_expiry_after_real_receipt_publication_refuses_and_rolls_back_before_same_key_retry()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new SignInReceiptExpiryClock();
        var context = new CredentialRollbackContext();
        await using var app = new ApiFactory(configureServices: services => {
            services.AddSingleton<IClock>(clock);
            services.AddSingleton<IIdentityCommandContext>(context);
        });
        using var client = app.CreateClient();
        var service = app.Services.GetRequiredService<IIdentityService>();
        var store = app.Services.GetRequiredService<IIdentityStore>();
        var receipts = app.Services.GetRequiredService<IIdentityLoginReplayStore>();
        var secrets = app.Services.GetRequiredService<IIdentityLoginRetrySecrets>();
        var tokens = app.Services.GetRequiredService<ISecureTokenService>();
        var email = $"late-login-expiry-{Guid.NewGuid():N}@example.test";
        const string password = "late-login-expiry-correct-horse";
        var registered = await service.RegisterAsync(email, password, "Final admission", null, null, "fixture", ct);
        Assert.True(registered.Succeeded);
        var userId = registered.Value!.User.Id;
        var original = await store.FindUserByIdAsync(userId, ct);
        var started = clock.Instant;
        var key = Guid.NewGuid(); context.IdempotencyKey = key;
        string? failedToken = null;
        clock.AfterReceipt = () => {
            // The Demo receipt reader and revocation proof reader do not read the
            // clock. This callback observes real stored state, without replacing
            // either store or assuming a count/order of earlier clock reads.
            var receipt = receipts.ReadAsync(userId, key, ct).GetAwaiter().GetResult();
            if (receipt is null) return null;
            Assert.True(secrets.TryDeriveSession(userId, receipt.SessionId, receipt.KeyVersion, out failedToken));
            var session = store.FindRevocationSessionProofAsync(tokens.Hash(failedToken!), ct).GetAwaiter().GetResult();
            Assert.NotNull(session); Assert.Equal(receipt.SessionId, session.SessionId); Assert.False(session.Revoked);
            return session.ExpiresAt;
        };
        var denied = await service.LoginAsync(email, password, "fixture", ct);
        Assert.False(denied.Succeeded); Assert.Equal("session_unavailable", denied.ErrorCode); Assert.Null(denied.Value);
        Assert.NotNull(failedToken);
        clock.AfterReceipt = null; clock.Instant = started;
        Assert.Null(await store.FindActiveSessionAsync(tokens.Hash(failedToken), started, ct));
        Assert.Null(await store.FindRevocationSessionProofAsync(tokens.Hash(failedToken), ct));
        Assert.Null(await receipts.ReadAsync(userId, key, ct));
        Assert.Equal(original, await store.FindUserByIdAsync(userId, ct));
        var retry = await service.LoginAsync(email, password, "fixture", ct).WaitAsync(TimeSpan.FromSeconds(10), ct);
        Assert.True(retry.Succeeded); Assert.NotEqual(failedToken, retry.Value!.SessionToken);
        var replay = await service.LoginAsync(email, password, "fixture", ct);
        Assert.True(replay.Succeeded); Assert.Equal(retry.Value, replay.Value);
        Assert.NotNull(await store.FindActiveSessionAsync(tokens.Hash(retry.Value.SessionToken), started, ct));
        Assert.NotNull(await receipts.ReadAsync(userId, key, ct));
    }

    private sealed class SignInReceiptExpiryClock : IClock
    {
        public DateTimeOffset Instant { get; set; } = DateTimeOffset.UtcNow;
        public Func<DateTimeOffset?>? AfterReceipt { get; set; }
        public DateTimeOffset UtcNow => AfterReceipt?.Invoke() ?? Instant;
    }
}
