using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    // Synthetic publication metadata only. Session/Board authorization, private
    // full-object verification and HTTP delivery execute normally; PostgreSQL
    // publication/grants/RLS have their own restricted-adapter contract.
    [Fact]
    public async Task PRD_14_Preview_HTTP_requires_published_clean_source_verifies_private_bytes_and_readmits_current_access()
    {
        if (!OperatingSystem.IsLinux()) return;
        var ct=TestContext.Current.CancellationToken;var objects=new UploadObjects();
        await using var app=UploadFactory(objects,downloads:true,images:true);
        using var owner=app.CreateClient();using var member=app.CreateClient();using var outsider=app.CreateClient();using var anonymous=app.CreateClient();
        var f=await NotificationFixture(app,owner,member,ct);await RegisterAndLogin(outsider);
        var card=await app.Services.GetRequiredService<IWorkManagementStore>().CreateCardAsync(f.List,Guid.NewGuid(),"Preview fixture",null,null,DateTimeOffset.UtcNow,ct);
        var bytes=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==");
        var originalBytes=bytes.Concat("PRIVATE ORIGINAL METADATA"u8.ToArray()).ToArray();
        using var upload=FileRequest($"/cards/{card.Id}/attachments",originalBytes,Guid.NewGuid(),"Untrusted original.png");
        using var created=await member.SendAsync(upload,ct);Assert.Equal(HttpStatusCode.OK,created.StatusCode);
        var file=(await created.Content.ReadFromJsonAsync<AttachmentChange>(ct))!.Attachment;
        var path=$"/cards/{card.Id}/attachments/{file.Id}/preview";
        using(var pending=await member.GetAsync(path,ct))Assert.Equal(HttpStatusCode.NotFound,pending.StatusCode);
        var metadata=Assert.IsType<DownloadMetadata>(app.Services.GetRequiredService<IAttachmentMetadataStore>());metadata.Clean=true;
        using(var unpublished=await member.GetAsync(path+"-options",ct))Assert.Equal(HttpStatusCode.NotFound,unpublished.StatusCode);
        using(var cleanOnly=await member.GetAsync(path,ct))Assert.Equal(HttpStatusCode.NotFound,cleanOnly.StatusCode);
        Assert.Equal(0,objects.Reads);
        var reference=AttachmentObjectReference.ForPreview(f.Organization,Guid.NewGuid());
        var stored=await objects.WritePrivateAsync(reference,new MemoryStream(bytes,false),bytes.Length,ct);
        metadata.PublishedPreview=new(new(reference,stored.SizeBytes,stored.Sha256),1,1);
        using var optionsResponse=await member.GetAsync(path+"-options",ct);
        var options=(await optionsResponse.Content.ReadFromJsonAsync<AttachmentDownloadOptions>(ct))!;
        Assert.Equal(f.Board,options.BoardId);Assert.Equal(f.Recipient,options.ActorId);Assert.Equal(3,options.AttachmentVersion);
        var optionsJson=await optionsResponse.Content.ReadAsStringAsync(ct);
        Assert.DoesNotContain(reference.ObjectKey,optionsJson,StringComparison.Ordinal);
        Assert.DoesNotContain(stored.Sha256,optionsJson,StringComparison.Ordinal);
        Assert.True(optionsResponse.Headers.CacheControl!.NoStore);Assert.Equal(0,objects.Reads);
        foreach(var deniedPath in new[]{path+$"?actorId={f.Owner}",path+"?attachmentVersion=2",$"/cards/{Guid.NewGuid()}/attachments/{file.Id}/preview"})
        {using var denied=await member.GetAsync(deniedPath,ct);Assert.Equal(HttpStatusCode.NotFound,denied.StatusCode);}
        using(var denied=await outsider.GetAsync(path,ct))Assert.Equal(HttpStatusCode.NotFound,denied.StatusCode);
        using(var denied=await anonymous.GetAsync(path,ct))Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);
        Assert.Equal(0,objects.Reads);
        using var request=new HttpRequestMessage(HttpMethod.Get,path+$"?actorId={f.Recipient}&attachmentVersion=3");
        request.Headers.Range=new(0,0);
        using var response=await member.SendAsync(request,ct);Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        Assert.Equal(bytes,await response.Content.ReadAsByteArrayAsync(ct));Assert.Equal(bytes.Length,response.Content.Headers.ContentLength);
        Assert.Equal("image/png",response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("inline",response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.True(response.Headers.CacheControl!.NoStore);Assert.True(response.Headers.CacheControl.Private);
        Assert.Equal("nosniff",Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("sandbox; default-src 'none'",Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
        Assert.Equal("none",Assert.Single(response.Headers.AcceptRanges));Assert.Null(response.Headers.ETag);Assert.Null(response.Content.Headers.LastModified);
        using(var canonical=await member.GetAsync($"/attachments/{file.Id}/preview?cardId={card.Id}",ct))
        {Assert.Equal(HttpStatusCode.OK,canonical.StatusCode);Assert.Equal(bytes,await canonical.Content.ReadAsByteArrayAsync(ct));}
        objects.CorruptReads=true;
        using(var corrupt=await member.GetAsync(path,ct))
        {Assert.Equal(HttpStatusCode.ServiceUnavailable,corrupt.StatusCode);Assert.Null(corrupt.Content.Headers.ContentDisposition);}
        objects.CorruptReads=false;
        metadata.Clean=false;var reads=objects.Reads;
        using(var quarantined=await member.GetAsync(path,ct))Assert.Equal(HttpStatusCode.NotFound,quarantined.StatusCode);
        Assert.Equal(reads,objects.Reads);metadata.Clean=true;
        objects.AfterReadClosed=async()=>{
            using var removed=await Mutate(owner,HttpMethod.Delete,$"/boards/{f.Board}/members/{f.Recipient}",new{});
            Assert.Equal(HttpStatusCode.NoContent,removed.StatusCode);
        };
        using(var revoked=await member.GetAsync(path,ct))
        {Assert.Equal(HttpStatusCode.NotFound,revoked.StatusCode);Assert.Null(revoked.Content.Headers.ContentDisposition);}
        reads=objects.Reads;
        using(var retry=await member.GetAsync(path,ct))Assert.Equal(HttpStatusCode.NotFound,retry.StatusCode);
        Assert.Equal(reads,objects.Reads);
    }
}
