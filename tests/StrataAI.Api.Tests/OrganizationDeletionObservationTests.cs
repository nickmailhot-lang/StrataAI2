using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task PRD_03_Owner_observes_only_original_pending_request_after_normal_access_withdrawal()
    {
        var ct=TestContext.Current.CancellationToken;
        await using var app=new ApiFactory();using var owner=app.CreateClient();using var other=app.CreateClient();
        await RegisterAndLogin(owner);await RegisterAndLogin(other);
        var actor=(await owner.GetFromJsonAsync<JsonElement>("/me",ct)).GetProperty("id").GetGuid();
        var outsider=(await other.GetFromJsonAsync<JsonElement>("/me",ct)).GetProperty("id").GetGuid();
        using var created=await Mutate(owner,HttpMethod.Post,"/organizations",new{name="Private observation fixture"});
        var org=(await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        var request=Guid.NewGuid();var organizations=app.Services.GetRequiredService<IOrganizationStore>();
        var publisher=app.Services.GetRequiredService<IOrganizationDeletionJobPublisher>();
        var unit=app.Services.GetRequiredService<IOrganizationUnitOfWork>();
        Assert.True((await unit.ExecuteAsync(org,actor,null,false,async()=>{
            Assert.True(await organizations.MarkDeletingAsync(org,1,DateTimeOffset.UtcNow,ct));
            Assert.True(await publisher.PublishAsync(org,actor,request,2,"observation",ct));
            return OrganizationOperation<bool>.Success(true);
        },ct)).Succeeded);
        using var ordinary=await owner.GetAsync($"/organizations/{org}",ct);Assert.Equal(HttpStatusCode.NotFound,ordinary.StatusCode);
        var path=$"/organizations/{org}/deletion-requests/{request}?expectedActorId={actor}";
        using var observed=await owner.GetAsync(path,ct);Assert.Equal(HttpStatusCode.OK,observed.StatusCode);
        Assert.Contains("no-store",observed.Headers.CacheControl!.ToString());
        var json=await observed.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(new[]{"completedAt","eventId","requestId","state","version"},json.EnumerateObject().Select(p=>p.Name).Order().ToArray());
        Assert.Equal(request,json.GetProperty("requestId").GetGuid());Assert.Equal("PENDING",json.GetProperty("state").GetString());
        Assert.Equal(2,json.GetProperty("version").GetInt64());Assert.Equal(JsonValueKind.Null,json.GetProperty("eventId").ValueKind);
        Assert.Equal(JsonValueKind.Null,json.GetProperty("completedAt").ValueKind);
        using var switched=await other.GetAsync(path,ct);Assert.Equal(HttpStatusCode.Unauthorized,switched.StatusCode);
        await organizations.AddOrRestoreMemberAsync(org,outsider,OrganizationRole.Owner,DateTimeOffset.UtcNow,ct);
        using var differentOwner=await other.GetAsync($"/organizations/{org}/deletion-requests/{request}",ct);
        Assert.Equal(HttpStatusCode.NotFound,differentOwner.StatusCode);Assert.DoesNotContain("Private observation fixture",await differentOwner.Content.ReadAsStringAsync(ct));
        using var wrong=await owner.GetAsync($"/organizations/{org}/deletion-requests/{Guid.NewGuid()}",ct);Assert.Equal(HttpStatusCode.NotFound,wrong.StatusCode);
        using var foreign=await owner.GetAsync($"/organizations/{Guid.NewGuid()}/deletion-requests/{request}",ct);Assert.Equal(HttpStatusCode.NotFound,foreign.StatusCode);
        await organizations.AddOrRestoreMemberAsync(org,actor,OrganizationRole.Admin,DateTimeOffset.UtcNow,ct);
        using var demoted=await owner.GetAsync(path,ct);Assert.Equal(HttpStatusCode.NotFound,demoted.StatusCode);
        Assert.DoesNotContain("Private observation fixture",await demoted.Content.ReadAsStringAsync(ct));
    }

    [Fact]
    public async Task PRD_03_Observation_discards_status_after_final_actor_fence_failure()
    {
        var ct=TestContext.Current.CancellationToken;var fence=new ObservationActorFence();
        await using var app=new ApiFactory(configureServices:s=>s.AddSingleton<ICommandActorAuthorization>(fence));
        using var owner=app.CreateClient();await RegisterAndLogin(owner);
        var actor=(await owner.GetFromJsonAsync<JsonElement>("/me",ct)).GetProperty("id").GetGuid();
        using var created=await Mutate(owner,HttpMethod.Post,"/organizations",new{name="Private final fence"});
        var org=(await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        var request=Guid.NewGuid();var organizations=app.Services.GetRequiredService<IOrganizationStore>();
        var publisher=app.Services.GetRequiredService<IOrganizationDeletionJobPublisher>();
        var unit=app.Services.GetRequiredService<IOrganizationUnitOfWork>();
        Assert.True((await unit.ExecuteAsync(org,actor,null,false,async()=>{
            Assert.True(await organizations.MarkDeletingAsync(org,1,DateTimeOffset.UtcNow,ct));
            Assert.True(await publisher.PublishAsync(org,actor,request,2,"observation",ct));
            return OrganizationOperation<bool>.Success(true);
        },ct)).Succeeded);
        fence.Arm();
        using var refused=await owner.GetAsync($"/organizations/{org}/deletion-requests/{request}",ct);
        Assert.Equal(HttpStatusCode.Unauthorized,refused.StatusCode);Assert.Equal(2,fence.Calls);
        var body=await refused.Content.ReadAsStringAsync(ct);
        Assert.DoesNotContain(request.ToString(),body);Assert.DoesNotContain("PENDING",body);Assert.DoesNotContain("Private final fence",body);
    }
    private sealed class ObservationActorFence:ICommandActorAuthorization
    {
        private bool _armed;public int Calls{get;private set;}
        public void Arm(){_armed=true;Calls=0;}
        public Task<bool> VerifyAsync(Guid actorId,CancellationToken cancellationToken=default)
        {cancellationToken.ThrowIfCancellationRequested();return Task.FromResult(!_armed || ++Calls==1);}
    }
}
