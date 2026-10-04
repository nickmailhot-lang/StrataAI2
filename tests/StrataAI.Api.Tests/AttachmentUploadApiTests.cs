using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StrataAI.Api.WorkManagement;
using StrataAI.Application.WorkManagement;
using StrataAI.Domain.WorkManagement;
using StrataAI.Infrastructure.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PRD_14_Pending_upload_after_cross_Board_move_keeps_original_revision_and_requires_fresh_review(bool stored)
    {
        var ct = TestContext.Current.CancellationToken; var objects = new UploadObjects(); await using var app = UploadFactory(objects);
        using var owner = app.CreateClient(); using var member = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct);
        var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var admission = app.Services.GetRequiredService<AttachmentUploadAdmissionService>();
        var intents = app.Services.GetRequiredService<IAttachmentUploadIntentStore>();
        var card = await work.CreateCardAsync(f.List, Guid.NewGuid(), "Pending upload movement", null, null, DateTimeOffset.UtcNow, ct);
        var bytes = new byte[512]; "%PDF-1.7\n"u8.CopyTo(bytes); var key = Guid.NewGuid();
        var input = new PrepareAttachmentUploadInput("Original.pdf", bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)), 1);
        var prepared = await admission.PrepareAsync(card.Id, f.Recipient, key, input, ct);
        Assert.True(prepared.Succeeded); var pending = prepared.Value!;
        if (stored)
        {
            // Server-owned measured storage fixture; the retry must perform no provider I/O.
            var claimed = await admission.ClaimAsync(card.Id, f.Recipient, pending.Id, key, pending.Version, ct);
            Assert.True(claimed.Succeeded);
            var recorded = await admission.RecordStoredAsync(card.Id, f.Recipient, claimed.Value!, new("application/pdf"),
                new(new(f.Organization, pending.Id), bytes.Length, input.Sha256), ct);
            Assert.True(recorded.Succeeded); pending = recorded.Value!;
        }
        using var boardResponse = await Mutate(owner, HttpMethod.Post, "/boards", new { organizationId = f.Organization, name = "Upload destination", visibility = "PRIVATE" });
        Assert.Equal(HttpStatusCode.Created, boardResponse.StatusCode);
        var destination = (await boardResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(ct)).GetProperty("id").GetGuid();
        using var grant = await Mutate(owner, HttpMethod.Patch, $"/boards/{destination}/members/{f.Recipient}", new { role = "MEMBER" });
        Assert.Equal(HttpStatusCode.OK, grant.StatusCode);
        using var listResponse = await Mutate(owner, HttpMethod.Post, $"/boards/{destination}/lists", new { name = "Current parent" });
        var list = (await listResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(ct)).GetProperty("id").GetGuid();
        using var moved = await Mutate(member, HttpMethod.Post, $"/cards/{card.Id}/move",
            new { sourceBoardId = f.Board, destinationListId = list, expectedVersion = 1 });
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode); var current = (await work.FindCardAsync(card.Id, ct))!;
        var path = $"/cards/{card.Id}/attachments";
        using var retry = FileRequest(path, bytes, key, input.DisplayName, 1);
        using var refused = await member.SendAsync(retry, ct);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode); Assert.Contains("version_conflict", await refused.Content.ReadAsStringAsync(ct));
        Assert.Equal(0, objects.Writes); Assert.Equal(0, objects.Reads);
        Assert.Equal(pending, await intents.FindUploadByRetryAsync(f.Organization, f.Recipient, key, ct));
        Assert.Equal(current, await work.FindCardAsync(card.Id, ct));
        Assert.Empty((await member.GetFromJsonAsync<AttachmentPage>(path, ct))!.Items);
        var options = (await member.GetFromJsonAsync<AttachmentUploadOptions>($"/cards/{card.Id}/attachment-upload-options", ct))!;
        Assert.Equal(destination, options.BoardId); Assert.Equal(2, options.CardVersion);
        using var fresh = FileRequest(path, bytes, Guid.NewGuid(), "Reviewed.pdf", options.CardVersion);
        using var published = await member.SendAsync(fresh, ct); Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        Assert.Equal(1, objects.Writes); Assert.Equal(0, objects.Reads);
        Assert.Equal(3, (await work.FindCardAsync(card.Id, ct))!.Version);
        Assert.Single((await member.GetFromJsonAsync<AttachmentPage>(path, ct))!.Items);
        using var removal = await Mutate(owner, HttpMethod.Delete, $"/boards/{destination}/members/{f.Recipient}", new { });
        Assert.Equal(HttpStatusCode.NoContent, removal.StatusCode);
        using var hidden = FileRequest(path, bytes, key, input.DisplayName, 1); using var denied = await member.SendAsync(hidden, ct);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode); Assert.Equal(1, objects.Writes); Assert.Equal(0, objects.Reads);
    }

    private sealed class UploadObjects : IAttachmentObjectStorage
    {
        private readonly Dictionary<AttachmentObjectReference, byte[]> _bytes = new();
        public int Writes, Reads;
        public bool CorruptReads;
        public Func<Task>? AfterReadClosed;
        public async Task<StoredAttachmentObject> WritePrivateAsync(AttachmentObjectReference reference, Stream source, long maximumBytes, CancellationToken ct)
        {
            Writes++; using var bytes = new MemoryStream(); await source.CopyToAsync(bytes, ct);
            if (bytes.Length > maximumBytes) throw new AttachmentStorageException("object_too_large");
            var content = bytes.ToArray(); _bytes.Add(reference, content);
            return new(reference, content.LongLength, Convert.ToHexStringLower(SHA256.HashData(content)));
        }
        public Task<Stream?> OpenPrivateReadAsync(AttachmentObjectReference reference, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); Reads++; if (!_bytes.TryGetValue(reference, out var bytes)) return Task.FromResult<Stream?>(null);
            var copy = bytes.ToArray(); if (CorruptReads) copy[^1] ^= 1;
            var callback = AfterReadClosed; AfterReadClosed = null;
            return Task.FromResult<Stream?>(new DownloadRead(copy, callback));
        }
        private sealed class DownloadRead(byte[] bytes, Func<Task>? closed) : MemoryStream(bytes, writable: false)
        {
            private int _closed;
            public override async ValueTask DisposeAsync()
            { await base.DisposeAsync(); if (Interlocked.Exchange(ref _closed, 1) == 0 && closed is not null) await closed(); }
        }
        public Task<bool> DeletePrivateAsync(AttachmentObjectReference reference, CancellationToken ct) => throw new InvalidOperationException("Upload must not delete objects.");
    }
    private static HttpRequestMessage FileRequest(string path, byte[] bytes, Guid? key = null, string name = "Looks like image.png", long version = 1)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new ByteArrayContent(bytes) };
        request.Content.Headers.ContentType = new("application/octet-stream");
        request.Headers.Add("X-StrataAI-Request", "1");
        if (key.HasValue) request.Headers.Add("Idempotency-Key", key.Value.ToString("D"));
        request.Headers.Add("X-Attachment-Name", Convert.ToBase64String(Encoding.UTF8.GetBytes(name)));
        request.Headers.Add("X-Attachment-Size", bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        request.Headers.Add("X-Attachment-SHA256", Convert.ToHexStringLower(SHA256.HashData(bytes)));
        request.Headers.Add("X-Card-Version", version.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return request;
    }
    // The normal Demo host remains disabled. This test explicitly injects a
    // synthetic transport/provider fixture; PostgreSQL atomicity is separate.
    private static ApiFactory UploadFactory(UploadObjects objects, bool downloads = false, bool images = false, ICardAttachmentCoverStore? covers = null) => new(configureServices: services =>
    {
        services.Replace(ServiceDescriptor.Singleton(new AttachmentUploadAvailability(true)));
        services.AddSingleton<IAttachmentObjectStorage>(objects);
        services.AddSingleton(new AttachmentUploadPolicy(1024, images ? ["image/png"] : ["application/pdf"]));
        services.AddSingleton<IAttachmentFileTypeInspector, AttachmentFileTypeInspector>();
        services.AddSingleton<IAttachmentDownloadPreparer, PrivateAttachmentDownloadPreparer>();
        if (downloads) services.Replace(ServiceDescriptor.Singleton<IAttachmentMetadataStore>(provider =>
            new DownloadMetadata((IAttachmentMetadataStore)provider.GetRequiredService<IWorkManagementStore>())));
        if (covers is not null) services.Replace(ServiceDescriptor.Singleton(covers));
    });

    [Fact]
    public async Task PRD_14_File_HTTP_transport_measures_actual_type_returns_private_pending_receipt_and_readmits_original_retry()
    {
        var ct = TestContext.Current.CancellationToken; var objects = new UploadObjects(); await using var app = UploadFactory(objects);
        using var owner = app.CreateClient(); using var member = app.CreateClient(); using var outsider = app.CreateClient(); using var anonymous = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct); await RegisterAndLogin(outsider);
        var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var card = await work.CreateCardAsync(f.List, Guid.NewGuid(), "File transport", "Preserved", null, DateTimeOffset.UtcNow, ct);
        var path = $"/cards/{card.Id}/attachments"; var key = Guid.NewGuid(); var bytes = new byte[512]; "%PDF-1.7\n"u8.CopyTo(bytes);
        var optionsPath = $"/cards/{card.Id}/attachment-upload-options";
        var options = (await member.GetFromJsonAsync<AttachmentUploadOptions>(optionsPath, ct))!;
        Assert.Equal(f.Organization, options.OrganizationId); Assert.Equal(f.Board, options.BoardId); Assert.Equal(card.Id, options.CardId);
        Assert.Equal(1, options.CardVersion); Assert.Equal(1024, options.MaximumBytes); Assert.Equal("application/pdf", Assert.Single(options.AllowedMimeTypes));
        using var outsiderOptions = await outsider.GetAsync(optionsPath, ct); Assert.Equal(HttpStatusCode.NotFound, outsiderOptions.StatusCode);
        using var anonymousOptions = await anonymous.GetAsync(optionsPath, ct); Assert.Equal(HttpStatusCode.Unauthorized, anonymousOptions.StatusCode);
        using (var denied = FileRequest(path, bytes))
        { using var response = await outsider.SendAsync(denied, ct); Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); }
        using (var denied = FileRequest(path, bytes, key))
        { using var response = await anonymous.SendAsync(denied, ct); Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode); }
        Assert.Equal(0, objects.Writes);
        using var first = FileRequest(path, bytes, key); using var added = await member.SendAsync(first, ct);
        Assert.Equal(HttpStatusCode.OK, added.StatusCode); var receipt = await added.Content.ReadAsStringAsync(ct);
        var change = (await added.Content.ReadFromJsonAsync<AttachmentChange>(ct))!;
        Assert.Equal(2, change.CardVersion); Assert.Equal(AttachmentKind.File, change.Attachment.Kind);
        Assert.Equal(AttachmentScanStatus.Pending, change.Attachment.ScanStatus); Assert.Equal("application/pdf", change.Attachment.MimeType);
        Assert.Equal(bytes.Length, change.Attachment.SizeBytes); Assert.Equal(f.Recipient, change.Attachment.UploaderId); Assert.Null(change.Attachment.Url);
        Assert.DoesNotContain(Convert.ToHexStringLower(SHA256.HashData(bytes)), receipt); Assert.DoesNotContain("storageKey", receipt);
        using var repeat = FileRequest(path, bytes, key); using var replay = await member.SendAsync(repeat, ct);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode); Assert.Equal(receipt, await replay.Content.ReadAsStringAsync(ct));
        Assert.Equal(1, objects.Writes); Assert.Equal(0, objects.Reads);
        using var changed = FileRequest(path, bytes, key, "Changed name.pdf"); using var refused = await member.SendAsync(changed, ct);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode); Assert.Contains("idempotency_key_reused", await refused.Content.ReadAsStringAsync(ct));
        var page = (await owner.GetFromJsonAsync<AttachmentPage>(path, ct))!; Assert.Equal(change.Attachment, Assert.Single(page.Items));
        Assert.Equal("Preserved", (await work.FindCardAsync(card.Id, ct))!.Description);
        using var removed = await Mutate(owner, HttpMethod.Delete, $"/boards/{f.Board}/members/{f.Recipient}", new { });
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        using var revoked = FileRequest(path, bytes, key); using var revokedResponse = await member.SendAsync(revoked, ct);
        Assert.Equal(HttpStatusCode.NotFound, revokedResponse.StatusCode); Assert.Equal(1, objects.Writes);
        using var revokedOptions = await member.GetAsync(optionsPath, ct); Assert.Equal(HttpStatusCode.NotFound, revokedOptions.StatusCode);
    }

    [Fact]
    public async Task PRD_14_File_HTTP_refuses_missing_retry_wrong_content_type_configured_size_and_forged_file_prefix_before_provider_write()
    {
        var ct = TestContext.Current.CancellationToken; var objects = new UploadObjects(); await using var app = UploadFactory(objects);
        using var owner = app.CreateClient(); using var member = app.CreateClient(); var f = await NotificationFixture(app, owner, member, ct);
        var work = app.Services.GetRequiredService<IWorkManagementStore>(); var card = await work.CreateCardAsync(f.List, Guid.NewGuid(), "Unchanged file", null, null, DateTimeOffset.UtcNow, ct);
        var path = $"/cards/{card.Id}/attachments"; var bytes = new byte[512]; "%PDF-1.7\n"u8.CopyTo(bytes);
        using var missing = FileRequest(path, bytes); using var missingResponse = await owner.SendAsync(missing, ct);
        Assert.Equal(HttpStatusCode.BadRequest, missingResponse.StatusCode); Assert.Contains("invalid_idempotency_key", await missingResponse.Content.ReadAsStringAsync(ct));
        using var wrong = FileRequest(path, bytes, Guid.NewGuid()); wrong.Content!.Headers.ContentType = new("image/png");
        using var wrongResponse = await owner.SendAsync(wrong, ct); Assert.Equal(HttpStatusCode.BadRequest, wrongResponse.StatusCode);
        using var large = FileRequest(path, new byte[2048], Guid.NewGuid()); using var largeResponse = await owner.SendAsync(large, ct);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, largeResponse.StatusCode);
        using var forged = FileRequest(path, new byte[512], Guid.NewGuid()); using var forgedResponse = await owner.SendAsync(forged, ct);
        Assert.Equal(HttpStatusCode.BadRequest, forgedResponse.StatusCode); Assert.Contains("attachment_type_not_allowed", await forgedResponse.Content.ReadAsStringAsync(ct));
        Assert.Equal(0, objects.Writes); Assert.Equal(card, await work.FindCardAsync(card.Id, ct));
        Assert.Empty((await owner.GetFromJsonAsync<AttachmentPage>(path, ct))!.Items);
    }

    [Fact]
    public async Task PRD_14_Disabled_Demo_keeps_URL_creation_and_does_not_map_binary_upload()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var member = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct); var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var card = await work.CreateCardAsync(f.List, Guid.NewGuid(), "URL retained", null, null, DateTimeOffset.UtcNow, ct); var path = $"/cards/{card.Id}/attachments";
        using var file = FileRequest(path, [1, 2, 3], Guid.NewGuid()); using var missing = await owner.SendAsync(file, ct);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, missing.StatusCode);
        using var link = await Mutate(owner, HttpMethod.Post, path + "/url", new CreateUrlAttachmentInput("Reference", "https://example.test/", 1));
        Assert.Equal(HttpStatusCode.OK, link.StatusCode);
    }
}

public sealed class AttachmentUploadTransportTests
{
    private static HttpRequest Valid()
    {
        var request = new DefaultHttpContext().Request; request.ContentType = "application/octet-stream"; request.ContentLength = 512;
        request.Headers["X-Attachment-Name"] = Convert.ToBase64String(Encoding.UTF8.GetBytes("Résumé.pdf"));
        request.Headers["X-Attachment-SHA256"] = new string('a', 64); request.Headers["X-Attachment-Size"] = "512"; request.Headers["X-Card-Version"] = "1";
        return request;
    }
    [Fact]
    public void PRD_14_Raw_transport_preserves_unicode_name_and_rejects_ambiguous_or_unbounded_claims_without_reading_body()
    {
        var valid = Valid(); Assert.Equal("Résumé.pdf", AttachmentUploadTransport.Read(valid)!.DisplayName);
        foreach (var (header, value) in new[] { ("X-Attachment-Name", "bad"), ("X-Attachment-Name", "/w=="),
            ("X-Attachment-Name", " " + valid.Headers["X-Attachment-Name"]), ("X-Attachment-Name", new string('A', 1364)),
            ("X-Attachment-SHA256", new string('A',64)), ("X-Attachment-SHA256", new string('a',63)),
            ("X-Attachment-Size","0"), ("X-Attachment-Size","+512"), ("X-Attachment-Size","513"), ("X-Attachment-Size","1073741825"),
            ("X-Card-Version","0"), ("X-Card-Version","9223372036854775807"), ("X-Card-Version"," 1") })
        { var request = Valid(); request.Headers[header] = value; Assert.Null(AttachmentUploadTransport.Read(request)); }
        var duplicate = Valid(); duplicate.Headers.Append("X-Attachment-SHA256", new string('a',64)); Assert.Null(AttachmentUploadTransport.Read(duplicate));
        var unknownLength = Valid(); unknownLength.ContentLength = null; Assert.NotNull(AttachmentUploadTransport.Read(unknownLength));
    }
}
