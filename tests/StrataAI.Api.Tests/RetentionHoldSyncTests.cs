using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Records;
using StrataAI.Domain.Records;
using Xunit;
using System.Net;

namespace StrataAI.Api.Tests;
public sealed partial class ApiHostTests
{
    [Fact]
    public async Task PRD_70_hold_sync_replays_original_audit_identity_resets_ahead_cursor_and_denies_foreign_scope()
    {
        var ct=TestContext.Current.CancellationToken;
        await using var app=new ApiFactory();using var owner=app.CreateClient();
        var f=await WorkDispositionFixture(app,owner,ct);var service=app.Services.GetRequiredService<RetentionHoldService>();
        var id=Guid.NewGuid();var applied=await service.ApplyReviewedAsync(f.Organization,f.Actor,id,
            new(f.Organization,null,null,null),"Private replay reason",null,app.Services.GetRequiredService<RetentionProducerAuthorization>(),Guid.NewGuid(),"original-apply",ct);
        Assert.True(applied.Succeeded);
        var first=await service.ReadSyncAsync(f.Organization,f.Actor,id,0,ct);Assert.True(first.Succeeded);
        var original=Assert.Single(first.Value!.Events);Assert.Equal("RETENTION_HOLD_APPLIED",original.EventType);
        Assert.Equal(id,original.EntityId);Assert.Equal(1,original.EntityVersion);Assert.Equal(f.Actor,original.ActorId);
        Assert.Equal("ORGANIZATION_OWNER",original.ActingCapacity);Assert.Equal("ORGANIZATION",original.Metadata.ScopeKind);
        Assert.Equal("original-apply",original.CorrelationId);Assert.Equal(applied.Value!.AppliedAt,original.CreatedAt);
        var quiet=await service.ReadSyncAsync(f.Organization,f.Actor,id,1,ct);Assert.Empty(quiet.Value!.Events);
        var ahead=await service.ReadSyncAsync(f.Organization,f.Actor,id,2,ct);Assert.True(ahead.Value!.ResetRequired);
        Assert.Equal(original,Assert.Single(ahead.Value.Events));
        Assert.True((await service.ReleaseAsync(f.Organization,f.Actor,id,1,Guid.NewGuid(),"original-release",ct)).Succeeded);
        var missed=await service.ReadSyncAsync(f.Organization,f.Actor,id,1,ct);var released=Assert.Single(missed.Value!.Events);
        Assert.Equal("RETENTION_HOLD_RELEASED",released.EventType);Assert.Equal(2,released.EntityVersion);
        var full=await service.ReadSyncAsync(f.Organization,f.Actor,id,0,ct);
        Assert.Equal(new[]{original,released},full.Value!.Events);Assert.Equal(2,full.Value.Current.Version);
        Assert.False((await service.ReadSyncAsync(Guid.NewGuid(),f.Actor,id,0,ct)).Succeeded);
        Assert.False((await service.ReadSyncAsync(f.Organization,Guid.NewGuid(),id,0,ct)).Succeeded);
        Assert.Equal("invalid_retention_hold_cursor",(await service.ReadSyncAsync(f.Organization,f.Actor,id,3,ct)).ErrorCode);
    }
    [Theory]
    [InlineData(null)]
    [InlineData("http://evil.example.test")]
    public async Task PRD_70_hold_live_refuses_missing_or_foreign_origin(string? origin)
    {
        await using var app=new ApiFactory();using var client=app.CreateClient();
        using var request=new HttpRequestMessage(HttpMethod.Post,"/organizations/live/retention-holds/negotiate?negotiateVersion=1");
        request.Headers.Add("X-StrataAI-Request","1");if(origin is not null)request.Headers.Add("Origin",origin);
        using var response=await client.SendAsync(request,TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
    }
}
