using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Identity;
using StrataAI.Application.Onboarding;
using StrataAI.Application.Organizations;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    // PRD-03-TC-04/05, PRD-60 ONBOARD-FR-004/009: current grants and surface separation.
    [Fact]
    public async Task Invitations_preserve_ownership_and_recheck_issuer_permissions()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory();
        var identities = app.Services.GetRequiredService<IIdentityStore>();
        async Task<UserIdentity> User()
        {
            var id = Guid.NewGuid(); var email = $"invite-{id:N}@example.test"; var now = DateTimeOffset.UtcNow;
            var user = new UserIdentity(id, email, email.ToUpperInvariant(), "Invite fixture", null, "en", "UTC",
                AccountStatus.Active, true, "unused-fixture-hash", now, now, 1);
            Assert.True(await identities.TryCreateUserAsync(user, null, null, ct)); return user;
        }
        var owner = await User(); var admin = await User(); var invited = await User();
        var organizations = app.Services.GetRequiredService<IOrganizationService>();
        var store = app.Services.GetRequiredService<IOrganizationStore>();
        var service = app.Services.GetRequiredService<IInvitationService>();
        var org = (await organizations.CreateAsync(owner.Id, "Invitation scopes", null, "fixture", ct)).Value!.Organization.Id;
        await store.AddOrRestoreMemberAsync(org, admin.Id, OrganizationRole.Admin, DateTimeOffset.UtcNow, ct);
        var denied = await service.CreateAsync(org, admin.Id, invited.Email, InvitationSurface.Internal, "OWNER", "fixture", ct);
        Assert.Equal("insufficient_permission", denied.ErrorCode);
        var portal = await service.CreateAsync(org, admin.Id, invited.Email, InvitationSurface.Portal, "OWNER", "fixture", ct);
        Assert.True(portal.Succeeded);
        Assert.True((await service.AcceptAsync(invited.Id, portal.Value!.RawToken, "fixture", ct)).Succeeded);
        Assert.Null(await store.FindMembershipAsync(org, invited.Id, ct));
        var downgrade = await service.CreateAsync(org, owner.Id, owner.Email, InvitationSurface.Internal, "MEMBER", "fixture", ct);
        Assert.True(downgrade.Succeeded);
        Assert.Equal("ownership_change_requires_confirmation", (await service.AcceptAsync(owner.Id, downgrade.Value!.RawToken, "fixture", ct)).ErrorCode);
        Assert.Equal(OrganizationRole.Owner, (await store.FindMembershipAsync(org, owner.Id, ct))!.Role);
        var staleGrant = await service.CreateAsync(org, admin.Id, invited.Email, InvitationSurface.Internal, "MEMBER", "fixture", ct);
        Assert.True(staleGrant.Succeeded);
        Assert.True((await organizations.RemoveMemberAsync(org, owner.Id, admin.Id, "fixture", ct)).Succeeded);
        Assert.Equal("invalid_or_expired_invitation", (await service.AcceptAsync(invited.Id, staleGrant.Value!.RawToken, "fixture", ct)).ErrorCode);
        Assert.Null(await store.FindMembershipAsync(org, invited.Id, ct));
        var legitimate = await service.CreateAsync(org, owner.Id, invited.Email, InvitationSurface.Internal, "MEMBER", "fixture", ct);
        Assert.True((await service.AcceptAsync(invited.Id, legitimate.Value!.RawToken, "fixture", ct)).Succeeded);
        Assert.Equal(OrganizationRole.Member, (await store.FindMembershipAsync(org, invited.Id, ct))!.Role);
        Assert.Equal("invalid_or_expired_invitation", (await service.AcceptAsync(invited.Id, legitimate.Value!.RawToken, "fixture", ct)).ErrorCode);
    }
}
