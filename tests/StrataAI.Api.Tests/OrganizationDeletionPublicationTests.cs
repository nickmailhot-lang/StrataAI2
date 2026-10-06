using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Theory]
    [InlineData("success")]
    [InlineData("failure")]
    [InlineData("actor")]
    [InlineData("exception")]
    [InlineData("cancel")]
    public async Task PRD_03_Demo_deletion_publication_requires_Organization_ownership_and_rolls_back_with_parent(string outcome)
    {
        var ct=TestContext.Current.CancellationToken;
        var fence=new OrganizationTransactionActorFixture();
        await using var app=new ApiFactory(configureServices: services => services.AddSingleton<ICommandActorAuthorization>(fence));
        using var owner=app.CreateClient(); await RegisterAndLogin(owner);
        var actor=(await owner.GetFromJsonAsync<JsonElement>("/me",ct)).GetProperty("id").GetGuid();
        using var created=await Mutate(owner,HttpMethod.Post,"/organizations",new{name="Publication fixture"});
        var org=(await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        var publisher=app.Services.GetRequiredService<IOrganizationDeletionJobPublisher>();
        var organizations=app.Services.GetRequiredService<IOrganizationStore>();
        var unit=app.Services.GetRequiredService<IOrganizationUnitOfWork>();
        var workUnit=app.Services.GetRequiredService<IWorkManagementUnitOfWork>();
        var request=Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>publisher.PublishAsync(org,actor,request,2,"first",ct));
        // A Work transaction owns only the Work scope and cannot borrow this
        // Organization journal without its rollback participants/account gate.
        await Assert.ThrowsAsync<InvalidOperationException>(()=>workUnit.ExecuteReadAsync(org,actor,"unavailable",
            ()=>Task.FromResult(true),async()=>{
                await publisher.PublishAsync(org,actor,request,2,"first",ct);
                return WorkOperation<bool>.Success(true);
            },ct));
        using var cancel=CancellationTokenSource.CreateLinkedTokenSource(ct);
        async Task<OrganizationOperation<bool>> Publish()
        {
            Assert.True(await organizations.MarkDeletingAsync(org,1,DateTimeOffset.UtcNow,ct));
            Assert.True(await publisher.PublishAsync(org,actor,request,2,"first",ct));
            Assert.False(await publisher.PublishAsync(org,actor,request,2,"later",ct));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>publisher.PublishAsync(org,actor,Guid.NewGuid(),2,"other",ct));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>publisher.PublishAsync(Guid.NewGuid(),actor,request,2,"other",ct));
            if(outcome=="actor")fence.Allowed=false;
            if(outcome=="exception")throw new InvalidOperationException("after publication");
            if(outcome=="cancel")cancel.Cancel();
            return outcome=="failure"?OrganizationOperation<bool>.Failure("refused"):OrganizationOperation<bool>.Success(true);
        }
        if(outcome=="exception")
            await Assert.ThrowsAsync<InvalidOperationException>(()=>unit.ExecuteAsync(org,actor,null,false,Publish,cancel.Token));
        else if(outcome=="cancel")
            await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>unit.ExecuteAsync(org,actor,null,false,Publish,cancel.Token));
        else
            Assert.Equal(outcome=="success",(await unit.ExecuteAsync(org,actor,null,false,Publish,cancel.Token)).Succeeded);
        Assert.Equal(outcome=="success"?OrganizationStatus.Deleting:OrganizationStatus.Active,
            (await organizations.FindOrganizationAsync(org,ct))!.Status);
        fence.Allowed=true;
        Assert.True((await unit.ExecuteAsync(org,actor,null,false,async()=>{
            if(outcome!="success")Assert.True(await organizations.MarkDeletingAsync(org,1,DateTimeOffset.UtcNow,ct));
            // A failed owning command must remove the original publication; a
            // committed command must keep its immutable duplicate identity.
            Assert.Equal(outcome!="success",await publisher.PublishAsync(org,actor,request,2,"recovered",ct));
            return OrganizationOperation<bool>.Success(true);
        },ct,allowDeletionRecovery:true)).Succeeded);
    }
}
