using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    // PRD-02-TC-05/06/07: expiry after receipt publication must restore the original proof.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Token_expiry_after_real_consumption_receipt_publication_rolls_back_before_same_key_retry(bool verification)
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new SignInReceiptExpiryClock();
        var context = new CredentialRollbackContext();
        await using var app = new ApiFactory(configureServices: services => {
            services.AddSingleton<IClock>(clock);
            services.AddSingleton<IIdentityCommandContext>(context);
            services.AddSingleton(new IdentityPolicy(true, verification, 12, TimeSpan.FromHours(12), TimeSpan.FromMinutes(30)));
        });
        using var client = app.CreateClient();
        var service = app.Services.GetRequiredService<IIdentityService>();
        var store = app.Services.GetRequiredService<IIdentityStore>();
        var receipts = app.Services.GetRequiredService<IIdentityTokenConsumptionReplayStore>();
        var tokens = app.Services.GetRequiredService<ISecureTokenService>();
        var email = $"late-token-expiry-{Guid.NewGuid():N}@example.test";
        var registered = await service.RegisterAsync(email, "original-token-correct-horse", "Final token admission", null, null, "fixture", ct);
        Assert.True(registered.Succeeded);
        var userId = registered.Value!.User.Id;
        var original = await store.FindUserByIdAsync(userId, ct);
        var originalEvents = (await store.ReadEventsAsync(userId, 0, ct)).Value!.Events.ToArray();
        var started = clock.Instant;
        var token = verification ? registered.Value.VerificationToken! : (await service.RequestPasswordResetAsync(email, "fixture", ct)).ResetToken!;
        var purpose = verification ? IdentityTokenPurpose.VerifyEmail : IdentityTokenPurpose.ResetPassword;
        var proof = await store.FindSecurityTokenRetryProofAsync(tokens.Hash(token), purpose, started, ct);
        Assert.NotNull(proof); Assert.Null(proof.UsedAt);
        var key = Guid.NewGuid(); context.IdempotencyKey = key;
        var observedPublication = false;
        clock.AfterReceipt = () => {
            var receipt = receipts.ReadAsync(userId, key, purpose, ct).GetAwaiter().GetResult();
            if (receipt is null) return null;
            Assert.Equal(proof.TokenId, receipt.TokenId);
            observedPublication = true;
            return proof.ExpiresAt;
        };
        Task<IdentityOperation<UserProfile>> Consume() => verification
            ? service.VerifyEmailAsync(token, "fixture", ct)
            : service.ResetPasswordAsync(token, "replacement-token-correct-horse", "fixture", ct);
        var denied = await Consume();
        Assert.True(observedPublication);
        Assert.False(denied.Succeeded); Assert.Equal("invalid_or_expired_token", denied.ErrorCode); Assert.Null(denied.Value);
        clock.AfterReceipt = null; clock.Instant = started;
        Assert.Equal(original, await store.FindUserByIdAsync(userId, ct));
        Assert.Equal(proof, await store.FindSecurityTokenRetryProofAsync(tokens.Hash(token), purpose, started, ct));
        Assert.Null(await receipts.ReadAsync(userId, key, purpose, ct));
        Assert.Equal(originalEvents, (await store.ReadEventsAsync(userId, 0, ct)).Value!.Events.ToArray());
        var retry = await Consume().WaitAsync(TimeSpan.FromSeconds(10), ct);
        Assert.True(retry.Succeeded);
        var replay = await Consume();
        Assert.True(replay.Succeeded); Assert.Equal(retry.Value, replay.Value);
        Assert.NotNull(await receipts.ReadAsync(userId, key, purpose, ct));
        Assert.Equal(originalEvents.Length + 1, (await store.ReadEventsAsync(userId, 0, ct)).Value!.Events.Count);
    }
}
