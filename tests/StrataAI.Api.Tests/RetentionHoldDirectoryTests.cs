using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Records;
using StrataAI.Domain.Records;
using Xunit;

namespace StrataAI.Api.Tests;
public sealed partial class ApiHostTests
{
    [Fact]
    public async Task PRD_70_hold_directory_pages_visible_heads_and_excludes_unavailable_source_before_pagination()
    {
        var ct=TestContext.Current.CancellationToken;
        await using var app=new ApiFactory();using var owner=app.CreateClient();using var foreign=app.CreateClient();
        var f=await WorkDispositionFixture(app,owner,ct);await RegisterAndLogin(foreign);
        var service=app.Services.GetRequiredService<RetentionHoldService>();
        var reviewer=app.Services.GetRequiredService<RetentionProducerAuthorization>();
        var source=$"/organizations/{f.Organization}/retention-sources/card/{f.Card}";
        using var reviewResponse=await owner.GetAsync(source+"/review",ct);var review=await reviewResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        using var classified=await Mutate(owner,HttpMethod.Post,source+"/classification",new{review,category="directory.private",jurisdiction=(string?)null,confirmed=true},Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.OK,classified.StatusCode);
        var hiddenId=Guid.Parse("00000001-0000-4000-8000-000000000001");
        var hiddenPath=$"/organizations/{f.Organization}/retention-holds/{hiddenId}";
        using var applied=await Mutate(owner,HttpMethod.Post,hiddenPath+"/apply",new{recordClass="operationalWork",category=(string?)null,recordId=f.Card,
            reason="Unavailable private source reason",review,confirmed=true},Guid.NewGuid().ToString());Assert.Equal(HttpStatusCode.OK,applied.StatusCode);
        using var released=await Mutate(owner,HttpMethod.Post,hiddenPath+"/release",new{version=1,confirmed=true},Guid.NewGuid().ToString());Assert.Equal(HttpStatusCode.OK,released.StatusCode);
        using var archive=await Mutate(owner,HttpMethod.Post,$"/cards/{f.Card}/archive",new{version=1});Assert.Equal(HttpStatusCode.OK,archive.StatusCode);
        using var deleted=await Mutate(owner,HttpMethod.Delete,$"/cards/{f.Card}?version=2&confirmed=true",new{},Guid.NewGuid().ToString());Assert.Equal(HttpStatusCode.OK,deleted.StatusCode);
        var visible=new List<Guid>();
        for(var index=1;index<=27;index++)
        {
            var id=Guid.Parse($"10000000-0000-4000-8000-{index:000000000000}");visible.Add(id);
            Assert.True((await service.ApplyReviewedAsync(f.Organization,f.Actor,id,new(f.Organization,RetentionRecordClass.Governance,null,null),
                $"Visible governance hold {index}",null,reviewer,Guid.NewGuid(),"directory-seed",ct)).Succeeded);
        }
        var path=$"/organizations/{f.Organization}/retention-holds";
        using var first=await owner.GetAsync(path,ct);Assert.Equal(HttpStatusCode.OK,first.StatusCode);
        Assert.True(first.Headers.CacheControl!.Private);Assert.True(first.Headers.CacheControl.NoStore);
        var page=await first.Content.ReadFromJsonAsync<JsonElement>(ct);var rows=page.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(25,rows.Length);Assert.Equal(visible.Take(25),rows.Select(x=>x.GetProperty("id").GetGuid()));
        Assert.Equal(visible[24],page.GetProperty("nextAfter").GetGuid());
        var text=await first.Content.ReadAsStringAsync(ct);Assert.DoesNotContain(hiddenId.ToString(),text);Assert.DoesNotContain("Unavailable private source reason",text);
        using var next=await owner.GetAsync(path+$"?after={visible[24]}",ct);Assert.Equal(HttpStatusCode.OK,next.StatusCode);
        var second=await next.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(visible.Skip(25),second.GetProperty("items").EnumerateArray().Select(x=>x.GetProperty("id").GetGuid()));
        Assert.Equal(JsonValueKind.Null,second.GetProperty("nextAfter").ValueKind);
        using var malformed=await owner.GetAsync(path+"?after=not-a-cursor",ct);Assert.Equal(HttpStatusCode.BadRequest,malformed.StatusCode);
        using var denied=await foreign.GetAsync(path+"?after=not-a-cursor",ct);Assert.Equal(HttpStatusCode.NotFound,denied.StatusCode);
        Assert.DoesNotContain("Visible governance",await denied.Content.ReadAsStringAsync(ct));
    }
}
