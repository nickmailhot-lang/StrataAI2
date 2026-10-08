using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Onboarding;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    // FOUND-FR-009; PRD-03/60: the canonical record includes the clocks and
    // revision already held by invitation persistence. No new public DTO fields.
    [Fact]
    public async Task Canonical_invitation_records_retain_creation_acceptance_revocation_metadata_and_refused_repeats()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new InvitationMetadataClock();
        await using var app = new ApiFactory(configureServices: services => {
            services.AddSingleton<IClock>(clock);
            services.AddSingleton<ICommandActorAuthorization, InvitationMetadataActor>();
        });
        using var owner = app.CreateClient(); using var member = app.CreateClient();
        var f = await NotificationFixture(app,owner,member,ct);
        var identities = app.Services.GetRequiredService<IIdentityStore>();
        var recipient = await identities.FindUserByIdAsync(f.Recipient,ct); Assert.NotNull(recipient);
        var invitations = app.Services.GetRequiredService<IInvitationStore>();
        var service = app.Services.GetRequiredService<IInvitationService>();
        var created = await service.CreateAsync(f.Organization,f.Owner,recipient.Email,InvitationSurface.Internal,"MEMBER","fixture",ct);
        Assert.True(created.Succeeded); var original = created.Value!.Invitation;
        AssertMetadata(original,clock.UtcNow,1);
        clock.Now = clock.Now.AddMinutes(1);
        var accepted = await service.AcceptAsync(f.Recipient,created.Value.RawToken,"fixture",ct);
        Assert.True(accepted.Succeeded);
        var consumed = await invitations.FindByIdAsync(f.Organization,original.Id,ct); Assert.NotNull(consumed);
        Assert.Equal(original.CreatedAt,consumed.CreatedAt); AssertMetadata(consumed,clock.UtcNow,2);
        Assert.Equal(clock.UtcNow,consumed.AcceptedAt);
        clock.Now = clock.Now.AddMinutes(1);
        Assert.False((await service.AcceptAsync(f.Recipient,created.Value.RawToken,"fixture",ct)).Succeeded);
        Assert.True((await service.AcceptPendingAsync(f.Recipient,original.Id,"fixture",ct)).Succeeded);
        Assert.False((await service.RevokeAsync(f.Organization,f.Owner,original.Id,"fixture",ct)).Succeeded);
        Assert.Equal(consumed,await invitations.FindByIdAsync(f.Organization,original.Id,ct));
        var next = await service.CreateAsync(f.Organization,f.Owner,recipient.Email,InvitationSurface.Internal,"MEMBER","fixture",ct);
        Assert.True(next.Succeeded); AssertMetadata(next.Value!.Invitation,clock.UtcNow,1);
        clock.Now = clock.Now.AddMinutes(1);
        Assert.True((await service.RevokeAsync(f.Organization,f.Owner,next.Value.Invitation.Id,"fixture",ct)).Succeeded);
        var revoked = await invitations.FindByIdAsync(f.Organization,next.Value.Invitation.Id,ct); Assert.NotNull(revoked);
        Assert.Equal(next.Value.Invitation.CreatedAt,revoked.CreatedAt); AssertMetadata(revoked,clock.UtcNow,2);
        clock.Now = clock.Now.AddMinutes(1);
        await service.RevokeAsync(f.Organization,f.Owner,revoked.Id,"fixture",ct);
        Assert.Equal(revoked,await invitations.FindByIdAsync(f.Organization,revoked.Id,ct));
    }

    private static void AssertMetadata(InvitationRecord record,DateTimeOffset updated,long version)
    {
        var value = JsonSerializer.SerializeToElement(record,new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.True(value.TryGetProperty("updatedAt",out var actual)); Assert.Equal(updated,actual.GetDateTimeOffset());
        Assert.True(value.TryGetProperty("version",out var revision)); Assert.Equal(version,revision.GetInt64());
    }
    private sealed class InvitationMetadataClock : IClock
    {
        public DateTimeOffset Now { get; set; } = new(2030,1,2,3,4,5,TimeSpan.Zero);
        public DateTimeOffset UtcNow => Now;
    }
    private sealed class InvitationMetadataActor : ICommandActorAuthorization
    {
        public Task<bool> VerifyAsync(Guid actorId,CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
