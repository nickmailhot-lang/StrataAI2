using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Organizations;
using StrataAI.Application.Identity;
using StrataAI.Domain.Organizations;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task PRD_27_configuration_activity_keeps_original_names_and_capacity_after_profile_role_and_account_withdrawal()
    {
        var ct=TestContext.Current.CancellationToken; await using var app=new ApiFactory();
        using var owner=app.CreateClient(); await RegisterAndLogin(owner);
        var (_,parent)=await ConfigurationParent(owner,"Historical attribution parent",ct);
        using var administrator=app.CreateClient(); await RegisterAndLogin(administrator);
        var profile=await administrator.GetFromJsonAsync<JsonElement>("/me",ct);
        var actor=profile.GetProperty("id").GetGuid();
        using(var named=await Mutate(administrator,HttpMethod.Patch,"/me",new {displayName="Original configuration administrator",version=profile.GetProperty("version").GetInt64()}))
            Assert.Equal(HttpStatusCode.OK,named.StatusCode);
        var members=app.Services.GetRequiredService<IOrganizationStore>();
        await members.AddOrRestoreMemberAsync(parent.Id,actor,OrganizationRole.Admin,DateTimeOffset.UtcNow,ct);
        var changes=app.Services.GetRequiredService<OrganizationConfigurationService>();
        var first=await changes.ChangeAsync(parent.Id,actor,new("Historical legal details","CA-BC","UTC"),0,Guid.NewGuid(),"Historical attribution",ct);
        Assert.True(first.Succeeded);
        profile=await administrator.GetFromJsonAsync<JsonElement>("/me",ct);
        using(var renamed=await Mutate(administrator,HttpMethod.Patch,"/me",new {displayName="Later configuration administrator",version=profile.GetProperty("version").GetInt64()}))
            Assert.Equal(HttpStatusCode.OK,renamed.StatusCode);
        var second=await changes.ChangeAsync(parent.Id,actor,new("Later legal details","CA-BC","UTC"),1,Guid.NewGuid(),"Later attribution",ct);
        Assert.True(second.Succeeded);
        await members.AddOrRestoreMemberAsync(parent.Id,actor,OrganizationRole.Member,DateTimeOffset.UtcNow,ct);
        Assert.True(await app.Services.GetRequiredService<IIdentityStore>().DeactivateUserAsync(actor,DateTimeOffset.UtcNow,ct));
        using var response=await owner.GetAsync($"/organizations/{parent.Id}/configuration/activity",ct);
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var page=(await response.Content.ReadFromJsonAsync<OrganizationConfigurationActivityPage>(ct))!;
        Assert.Equal(new[]{"Later configuration administrator","Original configuration administrator"},page.Items.Select(row=>row.ActorLabel));
        Assert.All(page.Items,row=>{Assert.Equal(actor,row.ActorId);Assert.Equal("ORGANIZATION_ADMIN",row.ActingCapacity);Assert.Empty(row.Metadata);});
        var wire=await response.Content.ReadAsStringAsync(ct);
        Assert.DoesNotContain("Historical legal details",wire);Assert.DoesNotContain("Later legal details",wire);
    }

    [Fact]
    public async Task PRD_27_configuration_activity_projects_retained_sources_without_values_and_binds_private_paging()
    {
        var ct=TestContext.Current.CancellationToken;
        await using var app=new ApiFactory(); using var owner=app.CreateClient(); await RegisterAndLogin(owner);
        var (actor,parent)=await ConfigurationParent(owner,"Private Activity parent",ct);
        var path=$"/organizations/{parent.Id}/configuration/activity";
        var empty=await owner.GetFromJsonAsync<OrganizationConfigurationActivityPage>(path,ct);
        Assert.Empty(empty!.Items);Assert.Null(empty.NextCursor);
        List<OrganizationConfigurationRecord> sources=[];
        // Real registered owning commands create the complete bounded page;
        // direct store seeding would bypass audit, receipts and admission.
        var service=app.Services.GetRequiredService<OrganizationConfigurationService>();
        for(var version=0;version<53;version++)
        {
            var result=await service.ChangeAsync(parent.Id,actor,
                new OrganizationConfigurationData("Private Activity legal name","CA-BC","UTC"),version,Guid.NewGuid(),"Activity original source",ct);
            Assert.True(result.Succeeded);sources.Add(result.Value!);
        }
        using var response=await owner.GetAsync(path,ct);Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        Assert.True(response.Headers.CacheControl!.Private);Assert.True(response.Headers.CacheControl.NoStore);
        var wire=await response.Content.ReadAsStringAsync(ct);Assert.DoesNotContain("Private Activity legal name",wire);
        Assert.DoesNotContain("Private Activity parent",wire);
        var first=(await response.Content.ReadFromJsonAsync<OrganizationConfigurationActivityPage>(ct))!;
        Assert.Equal(50,first.Items.Count);Assert.NotNull(first.NextCursor);
        for(var index=0;index<50;index++)
        {
            var source=sources[52-index];var item=first.Items[index];
            Assert.Equal(source.EventId,item.EventId);Assert.Equal(source.ActorId,item.ActorId);
            Assert.Equal(source.UpdatedAt,item.CreatedAt);Assert.Equal(source.CorrelationId,item.CorrelationId);
            Assert.Equal(source.Version,item.Version);Assert.Null(item.BoardId);Assert.Empty(item.Metadata);
        }
        var second=await owner.GetFromJsonAsync<OrganizationConfigurationActivityPage>(path+"?cursor="+Uri.EscapeDataString(first.NextCursor),ct);
        Assert.Equal(new long[]{3,2,1},second!.Items.Select(row=>row.Version));Assert.Null(second.NextCursor);
        var live=await owner.GetFromJsonAsync<OrganizationConfigurationSyncPage>($"/organizations/{parent.Id}/configuration/events",ct);
        foreach(var token in new[]{"tampered",live!.Cursor})
        {
            using var invalid=await owner.GetAsync(path+"?cursor="+Uri.EscapeDataString(token),ct);
            Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);
            Assert.Equal("invalid_configuration_activity_cursor",(await invalid.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
        }
        var (_,other)=await ConfigurationParent(owner,"Other private Activity",ct);
        using var foreign=await owner.GetAsync($"/organizations/{other.Id}/configuration/activity?cursor="+Uri.EscapeDataString(first.NextCursor),ct);
        Assert.Equal(HttpStatusCode.BadRequest,foreign.StatusCode);
    }

    [Theory]
    [InlineData("?cursor=")][InlineData("?cursor=a&cursor=b")]
    public async Task PRD_27_configuration_activity_checks_current_private_admission_before_cursor_errors(string query)
    {
        var ct=TestContext.Current.CancellationToken;await using var app=new ApiFactory();
        using var owner=app.CreateClient();await RegisterAndLogin(owner);
        var (_,parent)=await ConfigurationParent(owner,"Private Activity admission",ct);
        using var member=app.CreateClient();await RegisterAndLogin(member);
        var memberId=(await member.GetFromJsonAsync<JsonElement>("/me",ct)).GetProperty("id").GetGuid();
        await app.Services.GetRequiredService<IOrganizationStore>().AddOrRestoreMemberAsync(parent.Id,memberId,OrganizationRole.Member,DateTimeOffset.UtcNow,ct);
        using var allowed=await owner.GetAsync($"/organizations/{parent.Id}/configuration/activity"+query,ct);
        Assert.Equal(HttpStatusCode.BadRequest,allowed.StatusCode);
        using var denied=await member.GetAsync($"/organizations/{parent.Id}/configuration/activity"+query,ct);
        Assert.Equal(HttpStatusCode.NotFound,denied.StatusCode);
        Assert.DoesNotContain("Private Activity admission",await denied.Content.ReadAsStringAsync(ct));
    }
}
