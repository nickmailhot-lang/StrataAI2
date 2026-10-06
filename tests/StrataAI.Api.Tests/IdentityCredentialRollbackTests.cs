using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Identity;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    // PRD-02-TC-05/06/07: failed credential commands cannot leave recoverable side effects.
    [Theory]
    [InlineData("failure")]
    [InlineData("exception")]
    [InlineData("cancel")]
    public async Task Demo_sign_in_rolls_back_real_session_and_retry_receipt_before_same_key_retry(string outcome)
    {
        var ct = TestContext.Current.CancellationToken;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var context = new CredentialRollbackContext();
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton<IIdentityCommandContext>(context));
        using var client = app.CreateClient();
        var service = app.Services.GetRequiredService<IIdentityService>();
        var inner = app.Services.GetRequiredService<IdentityService>();
        var unit = app.Services.GetRequiredService<IIdentityUnitOfWork>();
        var store = app.Services.GetRequiredService<IIdentityStore>();
        var receipts = app.Services.GetRequiredService<IIdentityLoginReplayStore>();
        var tokens = app.Services.GetRequiredService<ISecureTokenService>();
        var email = $"credential-rollback-{Guid.NewGuid():N}@example.test";
        const string password = "credential-rollback-correct-horse";
        var registered = await service.RegisterAsync(email, password, "Credential rollback", null, null, "fixture", ct);
        Assert.True(registered.Succeeded);
        var userId = registered.Value!.User.Id;
        var original = await store.FindUserByIdAsync(userId, ct);
        var originalEvents = (await store.ReadEventsAsync(userId, 0, ct)).Value!.Events.ToArray();
        context.IdempotencyKey = Guid.NewGuid();
        LoginOutcome? written = null;
        var changing = unit.ExecuteSignInAsync(async () => {
            var result = await inner.LoginAsync(email, password, "fixture", cancellation.Token);
            Assert.True(result.Succeeded); written = result.Value!;
            Assert.NotNull(await store.FindActiveSessionAsync(tokens.Hash(written.SessionToken), DateTimeOffset.UtcNow, ct));
            Assert.NotNull(await receipts.ReadAsync(userId, context.IdempotencyKey.Value, ct));
            if (outcome == "exception") throw new InvalidOperationException("Sign-in failed after receipt publication.");
            if (outcome == "cancel") cancellation.Cancel();
            return outcome == "failure" ? IdentityOperation<LoginOutcome>.Failure("fixture_refused") : result;
        }, cancellation.Token);
        if (outcome == "exception") await Assert.ThrowsAsync<InvalidOperationException>(() => changing);
        else if (outcome == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => changing);
        else Assert.Equal("fixture_refused", (await changing).ErrorCode);
        Assert.NotNull(written);
        Assert.Null(await store.FindActiveSessionAsync(tokens.Hash(written.SessionToken), DateTimeOffset.UtcNow, ct));
        Assert.Null(await receipts.ReadAsync(userId, context.IdempotencyKey.Value, ct));
        Assert.Equal(original, await store.FindUserByIdAsync(userId, ct));
        Assert.Equal(originalEvents, (await store.ReadEventsAsync(userId, 0, ct)).Value!.Events.ToArray());
        var retry = await service.LoginAsync(email, password, "fixture", ct).WaitAsync(TimeSpan.FromSeconds(10), ct);
        Assert.True(retry.Succeeded);
        Assert.NotEqual(written.SessionToken, retry.Value!.SessionToken);
        var replay = await service.LoginAsync(email, password, "fixture", ct);
        Assert.True(replay.Succeeded); Assert.Equal(retry.Value, replay.Value);
        Assert.NotNull(await store.FindActiveSessionAsync(tokens.Hash(retry.Value.SessionToken), DateTimeOffset.UtcNow, ct));
    }

    [Theory]
    [InlineData(false, "failure")]
    [InlineData(true, "failure")]
    [InlineData(false, "exception")]
    [InlineData(true, "exception")]
    [InlineData(false, "cancel")]
    [InlineData(true, "cancel")]
    public async Task Demo_token_consumption_restores_account_original_token_sessions_event_and_receipt(bool verification, string outcome)
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
        var receipts = app.Services.GetRequiredService<IIdentityTokenConsumptionReplayStore>();
        var tokens = app.Services.GetRequiredService<ISecureTokenService>();
        var email = $"token-rollback-{Guid.NewGuid():N}@example.test";
        const string password = "token-original-correct-horse";
        const string replacement = "token-replacement-correct-horse";
        var registered = await service.RegisterAsync(email, password, "Token rollback", null, null, "fixture", ct);
        Assert.True(registered.Succeeded);
        var userId = registered.Value!.User.Id;
        var original = await store.FindUserByIdAsync(userId, ct);
        var originalEvents = (await store.ReadEventsAsync(userId, 0, ct)).Value!.Events.ToArray();
        string token;
        string? originalSession = null;
        if (verification) token = registered.Value.VerificationToken!;
        else
        {
            originalSession = (await service.LoginAsync(email, password, "fixture", ct)).Value!.SessionToken;
            token = (await service.RequestPasswordResetAsync(email, "fixture", ct)).ResetToken!;
        }
        var purpose = verification ? IdentityTokenPurpose.VerifyEmail : IdentityTokenPurpose.ResetPassword;
        var proofBefore = await store.FindSecurityTokenRetryProofAsync(tokens.Hash(token), purpose, DateTimeOffset.UtcNow, ct);
        Assert.NotNull(proofBefore); Assert.Null(proofBefore.UsedAt);
        context.IdempotencyKey = Guid.NewGuid();
        Task<IdentityOperation<UserProfile>> Consume(CancellationToken cancellationToken) => verification
            ? inner.VerifyEmailAsync(token, "fixture", cancellationToken)
            : inner.ResetPasswordAsync(token, replacement, "fixture", cancellationToken);
        var changing = unit.ExecuteTokenProofAsync(async () => {
            var result = await Consume(cancellation.Token);
            Assert.True(result.Succeeded);
            Assert.NotNull(await receipts.ReadAsync(userId, context.IdempotencyKey.Value, purpose, ct));
            Assert.NotNull((await store.FindSecurityTokenRetryProofAsync(tokens.Hash(token), purpose, DateTimeOffset.UtcNow, ct))!.UsedAt);
            if (outcome == "exception") throw new InvalidOperationException("Token consumption failed after receipt publication.");
            if (outcome == "cancel") cancellation.Cancel();
            return outcome == "failure" ? IdentityOperation<UserProfile>.Failure("fixture_refused") : result;
        }, cancellation.Token);
        if (outcome == "exception") await Assert.ThrowsAsync<InvalidOperationException>(() => changing);
        else if (outcome == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => changing);
        else Assert.Equal("fixture_refused", (await changing).ErrorCode);
        Assert.Equal(original, await store.FindUserByIdAsync(userId, ct));
        Assert.Equal(proofBefore, await store.FindSecurityTokenRetryProofAsync(tokens.Hash(token), purpose, DateTimeOffset.UtcNow, ct));
        Assert.Null(await receipts.ReadAsync(userId, context.IdempotencyKey.Value, purpose, ct));
        Assert.Equal(originalEvents, (await store.ReadEventsAsync(userId, 0, ct)).Value!.Events.ToArray());
        if (originalSession is not null) Assert.NotNull(await store.FindActiveSessionAsync(tokens.Hash(originalSession), DateTimeOffset.UtcNow, ct));
        var retry = await unit.ExecuteTokenProofAsync(() => Consume(ct), ct).WaitAsync(TimeSpan.FromSeconds(10), ct);
        Assert.True(retry.Succeeded);
        var replay = await unit.ExecuteTokenProofAsync(() => Consume(ct), ct);
        Assert.True(replay.Succeeded); Assert.Equal(retry.Value, replay.Value);
        Assert.NotNull(await receipts.ReadAsync(userId, context.IdempotencyKey.Value, purpose, ct));
        Assert.Equal(originalEvents.Length + 1, (await store.ReadEventsAsync(userId, 0, ct)).Value!.Events.Count);
        if (originalSession is not null) Assert.Null(await store.FindActiveSessionAsync(tokens.Hash(originalSession), DateTimeOffset.UtcNow, ct));
    }

    private sealed class CredentialRollbackContext : IIdentityCommandContext
    {
        public Guid? IdempotencyKey { get; set; }
        public string? RevocationSessionTokenHash => null;
    }
}
