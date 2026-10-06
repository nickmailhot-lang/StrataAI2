using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Identity;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task Unknown_account_runs_adaptive_verification_without_persisting_dummy_identity_or_disclosing_registration()
    {
        var ct = TestContext.Current.CancellationToken;
        var hashes = new AdaptiveVerificationProbe();
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton<IPasswordHashService>(hashes));
        using var client = app.CreateClient();
        var service = app.Services.GetRequiredService<IIdentityService>();
        var store = app.Services.GetRequiredService<IIdentityStore>();
        var email = $"verification-work-{Guid.NewGuid():N}@example.test";
        var registration = await service.RegisterAsync(email, "verification-work-correct-horse", "Private subject", null, null, "fixture", ct);
        Assert.True(registration.Succeeded); var actor = registration.Value!.User.Id;
        var original = await store.FindUserByIdAsync(actor, ct);
        var events = (await store.ReadEventsAsync(actor, 0, ct)).Value!.Events.ToArray();
        var unknown = $"unknown-{Guid.NewGuid():N}@example.test";
        var hashCount = hashes.HashCount; hashes.Subjects.Clear();
        foreach (var address in new[] { unknown, email, unknown })
        {
            var denied = await service.LoginAsync(address, "incorrect-private-password", "fixture", ct);
            Assert.False(denied.Succeeded); Assert.Equal("invalid_credentials", denied.ErrorCode); Assert.Null(denied.Value);
        }
        Assert.Equal(new[] { Guid.Empty, actor, Guid.Empty }, hashes.Subjects);
        Assert.Equal(hashCount, hashes.HashCount);
        Assert.Null(await store.FindUserByIdAsync(Guid.Empty, ct));
        Assert.Null(await store.FindUserByNormalizedEmailAsync(unknown.ToUpperInvariant(), ct));
        Assert.Equal(original, await store.FindUserByIdAsync(actor, ct));
        Assert.Equal(events, (await store.ReadEventsAsync(actor, 0, ct)).Value!.Events.ToArray());
    }

    private sealed class AdaptiveVerificationProbe : IPasswordHashService
    {
        private readonly PasswordHasher<object> _hasher = new();
        public int HashCount { get; private set; }
        public List<Guid> Subjects { get; } = [];
        public string Hash(Guid userId, string password) { HashCount++; return _hasher.HashPassword(new object(), password); }
        public PasswordVerification Verify(Guid userId, string passwordHash, string providedPassword)
        {
            Subjects.Add(userId);
            var result = _hasher.VerifyHashedPassword(new object(), passwordHash, providedPassword);
            return new(result != PasswordVerificationResult.Failed, result == PasswordVerificationResult.SuccessRehashNeeded);
        }
    }
}
