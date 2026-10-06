using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Identity;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Demo_recovery_request_rolls_back_new_token_and_receipt_after_exception_or_cancellation(bool verification, bool cancel)
    {
        var ct = TestContext.Current.CancellationToken;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var context = new CredentialRollbackContext();
        await using var app = new ApiFactory(configureServices: services => {
            services.AddSingleton<IIdentityCommandContext>(context);
            services.AddSingleton(new IdentityPolicy(true, verification, 12, TimeSpan.FromHours(12), TimeSpan.FromMinutes(30)));
        });
        using var client = app.CreateClient();
        var service = app.Services.GetRequiredService<IIdentityService>();
        var inner = app.Services.GetRequiredService<IdentityService>();
        var unit = app.Services.GetRequiredService<IIdentityUnitOfWork>();
        var store = app.Services.GetRequiredService<IIdentityStore>();
        var receipts = app.Services.GetRequiredService<IIdentityRecoveryRequestReplayStore>();
        var tokens = app.Services.GetRequiredService<ISecureTokenService>();
        var email = $"recovery-rollback-{Guid.NewGuid():N}@example.test";
        var registered = await service.RegisterAsync(email, "recovery-rollback-correct-horse", "Recovery rollback", null, null, "fixture", ct);
        Assert.True(registered.Succeeded);
        var userId = registered.Value!.User.Id;
        var original = await store.FindUserByIdAsync(userId, ct);
        var originalEvents = (await store.ReadEventsAsync(userId, 0, ct)).Value!.Events.ToArray();
        var purpose = verification ? IdentityTokenPurpose.VerifyEmail : IdentityTokenPurpose.ResetPassword;
        context.IdempotencyKey = Guid.NewGuid();
        async Task<string?> Request(CancellationToken token) => verification
            ? await inner.RequestEmailVerificationAsync(email, "fixture", token)
            : (await inner.RequestPasswordResetAsync(email, "fixture", token)).ResetToken;
        string? failedToken = null;
        var changing = unit.ExecuteRecoveryRequestAsync<string?>(async () => {
            failedToken = await Request(cancellation.Token);
            Assert.NotNull(failedToken);
            Assert.NotNull(await receipts.ReadAsync(userId, context.IdempotencyKey.Value, purpose, ct));
            Assert.Equal(userId, (await store.FindSecurityTokenRetryProofAsync(tokens.Hash(failedToken), purpose, DateTimeOffset.UtcNow, ct))!.User.Id);
            if (!cancel) throw new InvalidOperationException("Recovery request failed after receipt publication.");
            cancellation.Cancel();
            return failedToken;
        }, null, cancellation.Token);
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => changing);
        else Assert.Null(await changing);
        Assert.NotNull(failedToken);
        Assert.Null(await store.FindSecurityTokenRetryProofAsync(tokens.Hash(failedToken), purpose, DateTimeOffset.UtcNow, ct));
        Assert.Null(await receipts.ReadAsync(userId, context.IdempotencyKey.Value, purpose, ct));
        Assert.Equal(original, await store.FindUserByIdAsync(userId, ct));
        Assert.Equal(originalEvents, (await store.ReadEventsAsync(userId, 0, ct)).Value!.Events.ToArray());
        // Rollback must preserve any pre-existing registration verification proof.
        if (verification) Assert.Equal(userId, await store.GetEmailVerificationUserIdAsync(
            tokens.Hash(registered.Value.VerificationToken!), DateTimeOffset.UtcNow, ct));
        var retry = await unit.ExecuteRecoveryRequestAsync<string?>(() => Request(ct), null, ct).WaitAsync(TimeSpan.FromSeconds(10), ct);
        Assert.NotNull(retry); Assert.NotEqual(failedToken, retry);
        var receipt = await receipts.ReadAsync(userId, context.IdempotencyKey.Value, purpose, ct);
        Assert.NotNull(receipt);
        var replay = await unit.ExecuteRecoveryRequestAsync<string?>(() => Request(ct), null, ct);
        Assert.Equal(retry, replay);
        Assert.Equal(receipt, await receipts.ReadAsync(userId, context.IdempotencyKey.Value, purpose, ct));
        Assert.Equal(userId, (await store.FindSecurityTokenRetryProofAsync(tokens.Hash(retry), purpose, DateTimeOffset.UtcNow, ct))!.User.Id);
        var unknown = await unit.ExecuteRecoveryRequestAsync<string?>(async () => verification
            ? await inner.RequestEmailVerificationAsync($"unknown-{email}", "fixture", ct)
            : (await inner.RequestPasswordResetAsync($"unknown-{email}", "fixture", ct)).ResetToken, null, ct);
        Assert.Null(unknown);
        Assert.Equal(original, await store.FindUserByIdAsync(userId, ct));
        Assert.Equal(originalEvents, (await store.ReadEventsAsync(userId, 0, ct)).Value!.Events.ToArray());
    }
}
