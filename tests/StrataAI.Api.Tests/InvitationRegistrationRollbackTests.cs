using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Onboarding;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    // PRD-02 / PRD-03 / PRD-60: admission expiry must not leave an account or retry proof.
    [Theory]
    [InlineData("INTERNAL")]
    [InlineData("PORTAL")]
    [InlineData("BOARD")]
    public async Task Invitation_signup_expiry_after_account_event_and_receipt_writes_rolls_back_before_same_key_retry(string surface)
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new ReceiptTestClock();
        SignupExpiryProofFixture? proof = null;
        await using var app = new ApiFactory(configureServices: services => {
            services.AddSingleton<IClock>(clock);
            services.AddSingleton(new IdentityPolicy(false, true, 12, TimeSpan.FromHours(12), TimeSpan.FromMinutes(30)));
            var providerType = services.Last(row => row.ServiceType == typeof(IInvitationRegistrationProofStore)).ImplementationType!;
            services.AddSingleton<IInvitationRegistrationProofStore>(provider => proof = new SignupExpiryProofFixture(
                (IInvitationRegistrationProofStore)ActivatorUtilities.CreateInstance(provider, providerType)));
        });
        var f = await BoardInvitationFixtureAsync(app, ct);
        var email = $"signup-expiry-{Guid.NewGuid():N}@example.test";
        var created = surface == "BOARD"
            ? await app.Services.GetRequiredService<BoardInvitationService>().CreateAsync(f.Board.Id, f.Owner.Id, email, BoardRole.Member, "fixture", ct)
            : await app.Services.GetRequiredService<IInvitationService>().CreateAsync(f.Board.OrganizationId, f.Owner.Id, email,
                surface == "PORTAL" ? InvitationSurface.Portal : InvitationSurface.Internal,
                surface == "PORTAL" ? "OWNER" : "MEMBER", "fixture", ct);
        Assert.True(created.Succeeded);
        var invitation = created.Value!;
        var identities = app.Services.GetRequiredService<IIdentityStore>();
        var receipts = app.Services.GetRequiredService<IIdentityRegistrationReplayStore>();
        var tokens = app.Services.GetRequiredService<ISecureTokenService>();
        var key = Guid.NewGuid();
        var started = clock.UtcNow;
        Guid failedUser = Guid.Empty;
        string? failedVerification = null;
        // Intercept the final proof only after the real account, token, event and receipt
        // have been written. Forward to the actual provider after advancing its clock.
        _ = app.Services.GetRequiredService<IInvitationRegistrationProofStore>();
        Assert.True(proof!.RequiresFinalCheck);
        proof.BeforeFinalCheck = async () => {
            var user = await identities.FindUserByNormalizedEmailAsync(email.ToUpperInvariant(), ct);
            Assert.NotNull(user); failedUser = user.Id;
            Assert.Equal(AccountStatus.PendingVerification, user.Status);
            var receipt = await receipts.ReadAsync(user.Id, key, ct);
            Assert.NotNull(receipt);
            Assert.True(app.Services.GetRequiredService<IIdentityRegistrationRetrySecrets>().TryDeriveVerification(
                user.Id, receipt.VerificationTokenId!.Value, receipt.VerificationKeyVersion!, out failedVerification));
            Assert.Equal(user.Id, await identities.GetEmailVerificationUserIdAsync(tokens.Hash(failedVerification!), clock.UtcNow, ct));
            Assert.Single((await identities.ReadEventsAsync(user.Id, 0, ct)).Value!.Events);
            clock.UtcNow = invitation.Invitation.ExpiresAt;
        };
        using var client = app.CreateClient();
        var body = new { email, password = "signup-expiry-correct-horse", displayName = "Expiry recipient", invitationToken = invitation.RawToken };
        using var denied = await Mutate(client, HttpMethod.Post, "/auth/register", body, key.ToString());
        Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        var problem = await denied.Content.ReadAsStringAsync(ct);
        Assert.Contains("invalid_or_expired_invitation", problem);
        Assert.DoesNotContain(failedUser.ToString(), problem);
        Assert.DoesNotContain(invitation.RawToken, problem);
        Assert.DoesNotContain(failedVerification!, problem);
        clock.UtcNow = started;
        Assert.NotEqual(Guid.Empty, failedUser);
        Assert.Null(await identities.FindUserByIdAsync(failedUser, ct));
        Assert.Null(await identities.FindUserByNormalizedEmailAsync(email.ToUpperInvariant(), ct));
        Assert.Null(await receipts.ReadAsync(failedUser, key, ct));
        Assert.Null(await identities.GetEmailVerificationUserIdAsync(tokens.Hash(failedVerification!), started, ct));
        Assert.Empty((await identities.ReadEventsAsync(failedUser, 0, ct)).Value!.Events);
        var organizations = app.Services.GetRequiredService<IOrganizationStore>();
        var invitations = app.Services.GetRequiredService<IInvitationStore>();
        Assert.Equal(invitation.Invitation, await invitations.FindByIdAsync(f.Board.OrganizationId, invitation.Invitation.Id, ct));
        Assert.Null(await organizations.FindMembershipAsync(f.Board.OrganizationId, failedUser, ct));
        Assert.False(await invitations.HasActivePortalAccessAsync(f.Board.OrganizationId, failedUser, ct));
        Assert.Null(await app.Services.GetRequiredService<IWorkManagementStore>().FindBoardMemberAsync(f.Board.Id, failedUser, ct));
        proof.BeforeFinalCheck = null;
        using var retry = await Mutate(client, HttpMethod.Post, "/auth/register", body, key.ToString());
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        var acknowledgment = await retry.Content.ReadFromJsonAsync<JsonElement>(ct);
        var committedUser = acknowledgment.GetProperty("user").GetProperty("id").GetGuid();
        Assert.NotEqual(failedUser, committedUser);
        Assert.NotNull(await receipts.ReadAsync(committedUser, key, ct));
        Assert.Single((await identities.ReadEventsAsync(committedUser, 0, ct)).Value!.Events);
        Assert.Null(await organizations.FindMembershipAsync(f.Board.OrganizationId, committedUser, ct));
        Assert.False(await invitations.HasActivePortalAccessAsync(f.Board.OrganizationId, committedUser, ct));
        Assert.Null(await app.Services.GetRequiredService<IWorkManagementStore>().FindBoardMemberAsync(f.Board.Id, committedUser, ct));
        Assert.Equal(invitation.Invitation, await invitations.FindByIdAsync(f.Board.OrganizationId, invitation.Invitation.Id, ct));
        using var replay = await Mutate(client, HttpMethod.Post, "/auth/register", body, key.ToString());
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(acknowledgment.GetRawText(), (await replay.Content.ReadFromJsonAsync<JsonElement>(ct)).GetRawText());
        Assert.Single((await identities.ReadEventsAsync(committedUser, 0, ct)).Value!.Events);
        // Final admission also applies to credential-checked acknowledgments.
        // Refusing one must retain the previously committed account and receipt.
        var committedIdentity = await identities.FindUserByIdAsync(committedUser, ct);
        var committedReceipt = await receipts.ReadAsync(committedUser, key, ct);
        proof.BeforeFinalCheck = () => {
            clock.UtcNow = invitation.Invitation.ExpiresAt;
            return Task.CompletedTask;
        };
        using var expiredReplay = await Mutate(client, HttpMethod.Post, "/auth/register", body, key.ToString());
        Assert.Equal(HttpStatusCode.BadRequest, expiredReplay.StatusCode);
        var expiredProblem = await expiredReplay.Content.ReadAsStringAsync(ct);
        Assert.Contains("invalid_or_expired_invitation", expiredProblem);
        Assert.DoesNotContain(committedUser.ToString(), expiredProblem);
        Assert.DoesNotContain(acknowledgment.GetProperty("verificationToken").GetString()!, expiredProblem);
        clock.UtcNow = started;
        proof.BeforeFinalCheck = null;
        Assert.Equal(committedIdentity, await identities.FindUserByIdAsync(committedUser, ct));
        Assert.Equal(committedReceipt, await receipts.ReadAsync(committedUser, key, ct));
        Assert.Single((await identities.ReadEventsAsync(committedUser, 0, ct)).Value!.Events);
        Assert.Equal(invitation.Invitation, await invitations.FindByIdAsync(f.Board.OrganizationId, invitation.Invitation.Id, ct));
    }

    private sealed class SignupExpiryProofFixture(IInvitationRegistrationProofStore inner) : IInvitationRegistrationProofStore
    {
        public Func<Task>? BeforeFinalCheck { get; set; }
        public bool RequiresFinalCheck => inner.RequiresFinalCheck;
        public Task<InvitationRegistrationProof?> PrepareAsync(string tokenHash, string emailNormalized, CancellationToken ct) =>
            inner.PrepareAsync(tokenHash, emailNormalized, ct);
        public async Task<bool> CheckAsync(InvitationRegistrationProof proof, string emailNormalized, CancellationToken ct)
        {
            if (BeforeFinalCheck is { } before) await before();
            return await inner.CheckAsync(proof, emailNormalized, ct);
        }
    }
}
