using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Organizations;
using Xunit;

namespace StrataAI.Api.Tests;
public sealed partial class ApiHostTests
{
    [Fact]
    public async Task PRD_27_Demo_operational_lifecycle_directory_retains_inactive_administrative_discovery_without_operational_access()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var member = app.CreateClient();
        await RegisterAndLogin(owner); await RegisterAndLogin(member);
        var (actor,parent) = await ConfigurationParent(owner, "Discover retained Organization", ct);
        var memberId = (await member.GetFromJsonAsync<JsonElement>("/me",ct)).GetProperty("id").GetGuid();
        var organizations = app.Services.GetRequiredService<IOrganizationStore>();
        var added = await app.Services.GetRequiredService<IOrganizationUnitOfWork>().ExecuteAsync<bool>(parent.Id,actor,memberId,false,
            async () => { await organizations.AddOrRestoreMemberAsync(parent.Id,memberId,OrganizationRole.Member,DateTimeOffset.UtcNow,ct); return OrganizationOperation<bool>.Success(true); },ct);
        Assert.True(added.Succeeded);
        var suspended = await app.Services.GetRequiredService<OrganizationOperationalLifecycleService>().ChangeAsync(parent.Id,actor,
            OrganizationStatus.Suspended,parent.Version,Guid.NewGuid(),"directory-suspension",ct); Assert.True(suspended.Succeeded);
        using var response = await owner.GetAsync("/organizations/operational-lifecycle-directory",ct);
        Assert.Equal(HttpStatusCode.OK,response.StatusCode); Assert.True(response.Headers.CacheControl!.Private); Assert.True(response.Headers.CacheControl.NoStore);
        var page = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(actor,page.GetProperty("actorId").GetGuid());
        var item = Assert.Single(page.GetProperty("items").EnumerateArray(), row => row.GetProperty("organizationId").GetGuid()==parent.Id);
        Assert.Equal("SUSPENDED",item.GetProperty("status").GetString()); Assert.Equal(parent.Name,item.GetProperty("name").GetString());
        var denied = await member.GetFromJsonAsync<JsonElement>("/organizations/operational-lifecycle-directory",ct);
        Assert.Empty(denied.GetProperty("items").EnumerateArray());
        var normal = await owner.GetFromJsonAsync<JsonElement>("/organizations/directory",ct);
        Assert.DoesNotContain(normal.GetProperty("items").EnumerateArray(), row => row.GetProperty("organization").GetProperty("id").GetGuid()==parent.Id);
        using var work = await Mutate(owner,HttpMethod.Post,"/boards",new { organizationId=parent.Id,name="Blocked" }); Assert.Equal(HttpStatusCode.NotFound,work.StatusCode);
    }
    [Fact]
    public async Task PRD_27_Demo_operational_lifecycle_directory_pages_only_current_actor_membership_hints()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var actor = (await owner.GetFromJsonAsync<JsonElement>("/me",ct)).GetProperty("id").GetGuid();
        var store = app.Services.GetRequiredService<IOrganizationStore>();
        var ids = Enumerable.Range(1,51).Select(value => Guid.Parse($"01000000-0000-4000-8000-{value:000000000000}")).ToArray();
        foreach (var id in ids) await store.CreateOrganizationAsync(actor,id,"Lifecycle page "+id,null,DateTimeOffset.UtcNow,ct);
        var page = await owner.GetFromJsonAsync<JsonElement>("/organizations/operational-lifecycle-directory",ct);
        Assert.Equal(50,page.GetProperty("items").GetArrayLength()); Assert.Equal(ids[49],page.GetProperty("nextCursor").GetGuid());
        var next = await owner.GetFromJsonAsync<JsonElement>($"/organizations/operational-lifecycle-directory?after={ids[49]}",ct);
        Assert.Equal(ids[50],Assert.Single(next.GetProperty("items").EnumerateArray()).GetProperty("organizationId").GetGuid());
        Assert.Equal(JsonValueKind.Null,next.GetProperty("nextCursor").ValueKind);
        using var invalid = await owner.GetAsync("/organizations/operational-lifecycle-directory?after=invalid",ct); Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);
        using var mismatched = new HttpRequestMessage(HttpMethod.Get,"/organizations/operational-lifecycle-directory");
        mismatched.Headers.Add("X-StrataAI-Expected-Actor",Guid.NewGuid().ToString("D"));
        using var denied = await owner.SendAsync(mismatched,ct); Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);
    }
}
