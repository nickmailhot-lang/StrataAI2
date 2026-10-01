using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Identity;
using StrataAI.Application.Onboarding;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    // PRD-05/60: upgrading target persistence must not silently turn Board
    // invitations into Organization invitations while consumers are staged.
    [Fact]
    public async Task Unsupported_Board_target_never_grants_Organization_access_or_signup_proof()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory();
        var identities = app.Services.GetRequiredService<IIdentityStore>();
        var now = DateTimeOffset.UtcNow;
        async Task<UserIdentity> User()
        {
            var id = Guid.NewGuid(); var email = $"board-{id:N}@example.test";
            var user = new UserIdentity(id, email, email.ToUpperInvariant(), "Board fixture", null, "en", "UTC",
                AccountStatus.Active, true, "unused-fixture-hash", now, now, 1);
            Assert.True(await identities.TryCreateUserAsync(user, null, null, ct));
            return user;
        }
        var issuer = await User(); var recipient = await User();
        var organizations = app.Services.GetRequiredService<IOrganizationService>();
        var org = (await organizations.CreateAsync(issuer.Id, "Board target fixture", null, "fixture", ct)).Value!.Organization.Id;
        var tokens = app.Services.GetRequiredService<ISecureTokenService>();
        var raw = tokens.Generate();
        var store = app.Services.GetRequiredService<IInvitationStore>();
        var invitation = new InvitationRecord(Guid.NewGuid(), org, recipient.Email, recipient.EmailNormalized,
            tokens.Hash(raw), InvitationSurface.Internal, "MEMBER", issuer.Id, now, now.AddDays(1), null, null,
            BoardTarget: new(Guid.NewGuid(), BoardRole.Admin));
        var persisted = await store.CreateAsync(invitation, ct);
        Assert.Equal(invitation.BoardTarget, persisted.BoardTarget);
        var service = app.Services.GetRequiredService<IInvitationService>();
        Assert.Equal("invalid_or_expired_invitation", (await service.AcceptAsync(recipient.Id, raw, "fixture", ct)).ErrorCode);
        Assert.Equal("invalid_or_expired_invitation", (await service.AcceptPendingAsync(recipient.Id, invitation.Id, "fixture", ct)).ErrorCode);
        Assert.Equal("invalid_or_expired_invitation", (await service.ReviewTokenAsync(recipient.Id, raw, ct)).ErrorCode);
        Assert.Empty((await service.ListPendingAsync(recipient.Id, cancellationToken: ct)).Value!.Items);
        Assert.False((await store.AcceptAsync(tokens.Hash(raw), recipient.Id, recipient.EmailNormalized, now, ct)).Succeeded);
        Assert.Null(await app.Services.GetRequiredService<IOrganizationStore>().FindMembershipAsync(org, recipient.Id, ct));
        Assert.Null(await app.Services.GetRequiredService<IInvitationRegistrationProofStore>().PrepareAsync(
            tokens.Hash(raw), recipient.EmailNormalized, ct));
    }
}
