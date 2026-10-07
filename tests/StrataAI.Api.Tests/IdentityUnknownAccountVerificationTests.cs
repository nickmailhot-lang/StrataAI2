using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Identity;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    // AUTH-FR-009 / PRD-02-TC-03/04/07: lifecycle disclosure requires credential proof.
    [Theory]
    [InlineData(null, false)]
    [InlineData(null, true)]
    [InlineData(AccountStatus.Active, false)]
    [InlineData(AccountStatus.Active, true)]
    [InlineData(AccountStatus.PendingVerification, false)]
    [InlineData(AccountStatus.PendingVerification, true)]
    [InlineData(AccountStatus.Suspended, false)]
    [InlineData(AccountStatus.Suspended, true)]
    [InlineData(AccountStatus.Deactivated, false)]
    [InlineData(AccountStatus.Deactivated, true)]
    public async Task Incorrect_password_does_not_disclose_account_lifecycle_or_publish_retry_receipt(
        AccountStatus? status, bool requireVerifiedEmail)
    {
        var ct = TestContext.Current.CancellationToken;
        var hashes = new AdaptiveVerificationProbe();
        await using var app = new ApiFactory(configureServices: services => {
            services.AddSingleton<IPasswordHashService>(hashes);
            services.AddSingleton(new IdentityPolicy(true, requireVerifiedEmail, 12,
                TimeSpan.FromHours(12), TimeSpan.FromMinutes(30)));
        });
        using var client = app.CreateClient();
        var store = app.Services.GetRequiredService<IIdentityStore>();
        var receipts = app.Services.GetRequiredService<IIdentityLoginReplayStore>();
        var actor = Guid.NewGuid();
        var email = $"private-lifecycle-{Guid.NewGuid():N}@example.test";
        const string name = "Protected lifecycle subject";
        const string wrongPassword = "incorrect-private-lifecycle-password";
        if (status is not null)
        {
            var now = DateTimeOffset.UtcNow;
            var user = new UserIdentity(actor, email, email.ToUpperInvariant(), name,
                null, "en", "UTC", status.Value, status != AccountStatus.PendingVerification,
                hashes.Hash(actor, "private-lifecycle-correct-horse"), now, now, 1);
            Assert.True(await store.TryCreateUserAsync(user, null, null, ct));
        }
        // Resolve the actual singleton before counting verification/hash work.
        _ = app.Services.GetRequiredService<IIdentityService>();
        var before = await store.FindUserByIdAsync(actor, ct);
        var events = (await store.ReadEventsAsync(actor, 0, ct)).Value!.Events.ToArray();
        var hashCount = hashes.HashCount;
        hashes.Subjects.Clear();
        var key = Guid.NewGuid();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var denied = await Mutate(client, HttpMethod.Post, "/auth/login",
                new { email = email.ToUpperInvariant(), password = wrongPassword }, key.ToString());
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            Assert.False(denied.Headers.Contains("Set-Cookie"));
            var problem = await denied.Content.ReadFromJsonAsync<JsonElement>(ct);
            Assert.Equal("invalid_credentials", problem.GetProperty("code").GetString());
            var body = await denied.Content.ReadAsStringAsync(ct);
            Assert.DoesNotContain(email, body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(name, body);
            Assert.DoesNotContain(wrongPassword, body);
            Assert.DoesNotContain(actor.ToString(), body);
            Assert.DoesNotContain("email_verification_required", body);
            Assert.DoesNotContain("account_unavailable", body);
            Assert.Equal(before, await store.FindUserByIdAsync(actor, ct));
            Assert.Equal(events, (await store.ReadEventsAsync(actor, 0, ct)).Value!.Events.ToArray());
            Assert.Null(await receipts.ReadAsync(actor, key, ct));
            Assert.Null(await receipts.ReadAsync(Guid.Empty, key, ct));
        }
        var verifiedSubject = status is null ? Guid.Empty : actor;
        Assert.Equal(new[] { verifiedSubject, verifiedSubject }, hashes.Subjects);
        Assert.Equal(hashCount, hashes.HashCount);
        Assert.Null(await store.FindUserByIdAsync(Guid.Empty, ct));
        if (status is null)
            Assert.Null(await store.FindUserByNormalizedEmailAsync(email.ToUpperInvariant(), ct));
    }

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
