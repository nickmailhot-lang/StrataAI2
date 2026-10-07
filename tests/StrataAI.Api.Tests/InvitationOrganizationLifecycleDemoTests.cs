using System.Net;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Onboarding;
using StrataAI.Application.Organizations;
using Xunit;

namespace StrataAI.Api.Tests;
public sealed partial class ApiHostTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PRD_03_60_Demo_invitation_authority_deletion_request_rolls_back_and_delivers_cutoff(bool actorLoss)
    {
        var ct=TestContext.Current.CancellationToken; var fence=new OrganizationTransactionActorFixture();
        PublicationFailureFixture? publication=null;
        await using var app=new ApiFactory(configureServices:services=>{
            services.AddSingleton<ICommandActorAuthorization>(fence);
            var original=services.Last(d=>d.ServiceType==typeof(IOrganizationDeletionJobPublisher));
            services.AddSingleton<IOrganizationDeletionJobPublisher>(provider=>publication=new(
                (IOrganizationDeletionJobPublisher)original.ImplementationFactory!(provider),fence,actorLoss));
        });
        using var owner=app.CreateClient(); using var member=app.CreateClient();
        var f=await NotificationFixture(app,owner,member,ct);
        var invitations=app.Services.GetRequiredService<IInvitationStore>(); var identities=app.Services.GetRequiredService<IIdentityStore>();
        var organizations=app.Services.GetRequiredService<IOrganizationStore>();
        var replay=app.Services.GetRequiredService<TransactionalInvitationRecipientSynchronization>();
        var codec=app.Services.GetRequiredService<IInvitationRecipientCursorCodec>();
        var now=app.Services.GetRequiredService<IClock>().UtcNow;
        var last=Guid.NewGuid(); var future=Guid.NewGuid();
        foreach(var id in new[]{last,future})
            Assert.True(await identities.TryCreateUserAsync(new(id,$"lifecycle-{id:N}@example.test",$"lifecycle-{id:N}@example.test".ToUpperInvariant(),
                "Lifecycle recipient",null,"en-CA","UTC",AccountStatus.Active,true,"unused-fixture-hash",now,now,1),null,null,ct));
        for(var index=1;index<=206;index++)
        {
            var email=index==205?$"lifecycle-{last:N}@example.test":index==206?$"lifecycle-{future:N}@example.test":"demo@strataai.test";
            await invitations.CreateAsync(new(Guid.NewGuid(),f.Organization,email,email.ToUpperInvariant(),index.ToString("x64"),
                InvitationSurface.Portal,"OWNER",f.Owner,index==206?now.AddDays(1):now.AddSeconds(-1).AddTicks(index),now.AddDays(7),null,null),ct);
        }
        var before=await replay.ReadAsync(DemoRecipient,null,cancellationToken:ct);
        var lastBefore=await replay.ReadAsync(last,null,cancellationToken:ct);
        var futureBefore=await replay.ReadAsync(future,null,cancellationToken:ct);
        Assert.True(before.Succeeded); Assert.True(lastBefore.Succeeded); Assert.True(futureBefore.Succeeded);
        var parent=await organizations.FindOrganizationAsync(f.Organization,ct);
        var key=Guid.NewGuid(); var path=$"/organizations/{f.Organization}?version=1&expectedActorId={f.Owner}";
        using var refused=await Mutate(owner,HttpMethod.Delete,path,new{},key.ToString());
        Assert.Equal(actorLoss?HttpStatusCode.Unauthorized:HttpStatusCode.ServiceUnavailable,refused.StatusCode);
        Assert.NotNull(publication); Assert.True(publication.LastPublished);
        Assert.Equal(parent,await organizations.FindOrganizationAsync(f.Organization,ct));
        // Restore the fixture's global account gate before reading recipient
        // cursors; actor-loss refusal itself has already completed and rolled back.
        fence.Allowed=true;
        Assert.True((await replay.IsCursorCurrentAsync(DemoRecipient,before.Value!.Cursor,ct)).Value);
        Assert.True((await replay.IsCursorCurrentAsync(last,lastBefore.Value!.Cursor,ct)).Value);
        publication.Armed=false; fence.Allowed=true;
        using var accepted=await Mutate(owner,HttpMethod.Delete,path,new{},key.ToString());
        Assert.Equal(HttpStatusCode.Accepted,accepted.StatusCode);
        using var duplicate=await Mutate(owner,HttpMethod.Delete,path,new{},key.ToString());
        Assert.Equal(HttpStatusCode.Accepted,duplicate.StatusCode);
        Assert.Equal(OrganizationStatus.Deleting,(await organizations.FindOrganizationAsync(f.Organization,ct))!.Status);
        foreach(var pair in new[]{(Id:DemoRecipient,Email:"DEMO@STRATAAI.TEST",Cursor:before.Value.Cursor),
            (Id:last,Email:$"lifecycle-{last:N}@example.test".ToUpperInvariant(),Cursor:lastBefore.Value.Cursor)})
        {
            Assert.False((await replay.IsCursorCurrentAsync(pair.Id,pair.Cursor,ct)).Value);
            var reset=await replay.ReadAsync(pair.Id,pair.Cursor,cancellationToken:ct);
            Assert.True(reset.Succeeded); Assert.True(reset.Value!.ResetRequired); Assert.Empty(reset.Value.Events);
            Assert.True(codec.TryDecode(new(pair.Id,pair.Email,1,1),reset.Value.Cursor,out _));
            var quiet=await replay.ReadAsync(pair.Id,reset.Value.Cursor,cancellationToken:ct);
            Assert.True(quiet.Succeeded); Assert.False(quiet.Value!.ResetRequired); Assert.Empty(quiet.Value.Events);
        }
        Assert.True((await replay.IsCursorCurrentAsync(future,futureBefore.Value!.Cursor,ct)).Value);
        Assert.Null(await organizations.FindMembershipAsync(f.Organization,last,ct));
    }
}
