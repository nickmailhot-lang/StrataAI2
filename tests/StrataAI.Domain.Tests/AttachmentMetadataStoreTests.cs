using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using StrataAI.Application.Common;
using StrataAI.Infrastructure.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.Runtime;
using StrataAI.Application.WorkManagement;
using StrataAI.Domain.WorkManagement;
using StrataAI.Infrastructure.Organizations;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Infrastructure.WorkManagement;
using Xunit;

namespace StrataAI.Domain.Tests;

public sealed class AttachmentMetadataStoreTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-03T08:00:00Z", CultureInfo.InvariantCulture);
    private static ServiceProvider Demo()
    {
        var services = new ServiceCollection(); var runtime = new RuntimeDescriptor(RuntimeMode.Demo, "test", "test");
        services.AddSingleton<IClock, SystemClock>();
        services.AddStrataAiIdentity(new ConfigurationBuilder().Build(), runtime);
        services.AddStrataAiOrganizations(runtime); services.AddStrataAiWorkManagement(runtime);
        return services.BuildServiceProvider();
    }
    private static async Task<(Guid Organization, Guid User, CardRecord Card)> Parent(ServiceProvider services, CancellationToken ct)
    {
        var organization = Guid.NewGuid(); var user = Guid.NewGuid();
        await services.GetRequiredService<IOrganizationStore>().CreateOrganizationAsync(user, organization, "Attachments", null, Now, ct);
        var work = services.GetRequiredService<IWorkManagementStore>();
        var board = await work.CreateBoardAsync(organization, user, Guid.NewGuid(), "Private", null, BoardVisibility.Private, "COLOR", null, Now, ct);
        var list = await work.CreateListAsync(board.Id, Guid.NewGuid(), "List", null, Now, ct);
        return (organization, user, await work.CreateCardAsync(list.Id, Guid.NewGuid(), "Card", null, null, Now, ct));
    }
    [Fact]
    public async Task Bounded_timestamp_identity_paging_keeps_ties_without_duplicates_and_never_widens_card_or_tenant_scope()
    {
        var ct = TestContext.Current.CancellationToken; using var services = Demo();
        var parent = await Parent(services, ct); var other = await Parent(services, ct);
        var store = services.GetRequiredService<IAttachmentMetadataStore>();
        var expected = new List<AttachmentMetadata>();
        for (var index = 0; index < 63; index++) expected.Add(await store.CreateUrlAttachmentAsync(Guid.NewGuid(), parent.Organization,
            parent.Card.Id, parent.User, $"Link {index}", $"https://example.test/{index}", Now.AddTicks(index < 35 ? 1 : 10), ct));
        await store.CreateUrlAttachmentAsync(Guid.NewGuid(), other.Organization, other.Card.Id, other.User, "Foreign", "https://example.test/foreign", Now, ct);
        var first = await store.ListAttachmentsAsync(parent.Organization, parent.Card.Id, null, null, ct);
        Assert.Equal(51, first.Count); var anchor = first[49];
        var second = await store.ListAttachmentsAsync(parent.Organization, parent.Card.Id, anchor.CreatedAt, anchor.Id, ct);
        Assert.Equal(13, second.Count);
        var actual = first.Take(50).Concat(second).ToArray();
        Assert.Equal(expected.OrderByDescending(row => row.CreatedAt).ThenByDescending(row => row.Id).Select(row => row.Id), actual.Select(row => row.Id));
        Assert.Equal(63, actual.Select(row => row.Id).Distinct().Count());
        Assert.All(actual, row => { Assert.Equal(parent.Organization, row.OrganizationId); Assert.Equal(parent.Card.Id, row.CardId); Assert.Equal(0, row.CreatedAt.Ticks % 10); });
        Assert.Empty(await store.ListAttachmentsAsync(other.Organization, parent.Card.Id, null, null, ct));
        Assert.Empty(await store.ListAttachmentsAsync(parent.Organization, other.Card.Id, null, null, ct));
        Assert.Null(await store.FindAttachmentAsync(other.Organization, parent.Card.Id, expected[0].Id, ct));
        Assert.Null(await store.FindAttachmentAsync(parent.Organization, other.Card.Id, expected[0].Id, ct));
        Assert.Equal(expected[0], await store.FindAttachmentAsync(parent.Organization, parent.Card.Id, expected[0].Id, ct));
    }
    [Fact]
    public async Task URL_identity_metadata_has_no_binary_secrets_and_foreign_parent_or_uploader_cannot_create_a_record()
    {
        var ct = TestContext.Current.CancellationToken; using var services = Demo(); var parent = await Parent(services, ct); var other = await Parent(services, ct);
        var store = services.GetRequiredService<IAttachmentMetadataStore>(); var id = Guid.NewGuid();
        var row = await store.CreateUrlAttachmentAsync(id, parent.Organization, parent.Card.Id, parent.User, " Link ", "https://example.test/a?q=1#part", Now, ct);
        Assert.Equal("Link", row.DisplayName); Assert.Equal(AttachmentKind.Url, row.Kind); Assert.Equal(AttachmentScanStatus.NotApplicable, row.ScanStatus);
        Assert.Null(row.MimeType); Assert.Null(row.SizeBytes); Assert.Null(row.ScannedAt); Assert.Null(row.DeletedAt); Assert.Equal(1, row.Version);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(row)); Assert.False(json.RootElement.TryGetProperty("StorageKey", out _));
        await Assert.ThrowsAsync<ArgumentException>(() => store.CreateUrlAttachmentAsync(id, parent.Organization, parent.Card.Id, parent.User, "Replacement", "https://example.test/changed", Now, ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CreateUrlAttachmentAsync(Guid.NewGuid(), parent.Organization, other.Card.Id, parent.User, "Foreign", "https://example.test/", Now, ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CreateUrlAttachmentAsync(Guid.NewGuid(), parent.Organization, parent.Card.Id, other.User, "Foreign", "https://example.test/", Now, ct));
        Assert.Equal(row, await store.FindAttachmentAsync(parent.Organization, parent.Card.Id, id, ct));
        Assert.Single(await store.ListAttachmentsAsync(parent.Organization, parent.Card.Id, null, null, ct));
        await Assert.ThrowsAsync<ArgumentException>(() => store.ListAttachmentsAsync(parent.Organization, parent.Card.Id, Now, null, ct));
        await Assert.ThrowsAsync<ArgumentException>(() => store.ListAttachmentsAsync(parent.Organization, parent.Card.Id, null, Guid.NewGuid(), ct));
        await Assert.ThrowsAsync<ArgumentException>(() => store.ListAttachmentsAsync(parent.Organization, parent.Card.Id, Now, Guid.Empty, ct));
    }
    [Fact]
    public async Task File_creation_persists_only_pending_metadata_and_keeps_scope_bound_integrity_out_of_normal_projections()
    {
        var ct = TestContext.Current.CancellationToken; using var services = Demo(); var parent = await Parent(services, ct); var other = await Parent(services, ct);
        var store = services.GetRequiredService<IAttachmentMetadataStore>(); var reference = new AttachmentObjectReference(parent.Organization, Guid.NewGuid());
        var measured = new StoredAttachmentObject(reference, 128, new string('a', 64));
        var row = await store.CreateFileAttachmentAsync(measured, parent.Card.Id, parent.User, " Image name.png ", "image/png", Now.AddTicks(1), ct);
        Assert.Equal(reference.AttachmentId, row.Id); Assert.Equal(AttachmentKind.File, row.Kind); Assert.Equal(AttachmentScanStatus.Pending, row.ScanStatus);
        Assert.Equal("Image name.png", row.DisplayName); Assert.Equal("image/png", row.MimeType); Assert.Equal(128, row.SizeBytes);
        Assert.Null(row.Url); Assert.Null(row.ScannedAt); Assert.Null(row.DeletedAt); Assert.Equal(1, row.Version); Assert.Equal(Now, row.CreatedAt);
        var file = await store.FindFileAttachmentAsync(parent.Organization, parent.Card.Id, row.Id, ct); Assert.NotNull(file);
        Assert.Equal(row, file.Metadata); Assert.Equal(reference, file.Integrity.Reference); Assert.Equal(measured.Sha256, file.Integrity.Sha256); Assert.Equal(128, file.Integrity.SizeBytes);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(row)); Assert.False(json.RootElement.TryGetProperty("StorageKey", out _)); Assert.False(json.RootElement.TryGetProperty("Sha256", out _)); Assert.False(json.RootElement.TryGetProperty("Integrity", out _));
        using var privateJson = JsonDocument.Parse(JsonSerializer.Serialize(file)); Assert.False(privateJson.RootElement.TryGetProperty("Integrity", out _));
        Assert.DoesNotContain(reference.ObjectKey, privateJson.RootElement.GetRawText()); Assert.DoesNotContain(measured.Sha256, privateJson.RootElement.GetRawText());
        Assert.Null(await store.FindFileAttachmentAsync(other.Organization, parent.Card.Id, row.Id, ct));
        Assert.Null(await store.FindFileAttachmentAsync(parent.Organization, other.Card.Id, row.Id, ct));
        var sameTenantCard = await services.GetRequiredService<IWorkManagementStore>().CreateCardAsync(parent.Card.ListId, Guid.NewGuid(), "Other Card", null, null, Now, ct);
        Assert.Null(await store.FindFileAttachmentAsync(parent.Organization, sameTenantCard.Id, row.Id, ct));
        var url = await store.CreateUrlAttachmentAsync(Guid.NewGuid(), parent.Organization, parent.Card.Id, parent.User, "Link", "https://example.test/", Now, ct);
        Assert.Null(await store.FindFileAttachmentAsync(parent.Organization, parent.Card.Id, url.Id, ct));
        Assert.Equal(2, (await store.ListAttachmentsAsync(parent.Organization, parent.Card.Id, null, null, ct)).Count);
    }
    [Fact]
    public async Task File_identity_cannot_replace_metadata_or_integrity_and_foreign_parent_or_uploader_cannot_create()
    {
        var ct = TestContext.Current.CancellationToken; using var services = Demo(); var parent = await Parent(services, ct); var other = await Parent(services, ct);
        var store = services.GetRequiredService<IAttachmentMetadataStore>(); var measured = new StoredAttachmentObject(new(parent.Organization, Guid.NewGuid()), 128, new string('a', 64));
        var row = await store.CreateFileAttachmentAsync(measured, parent.Card.Id, parent.User, "Image", "image/png", Now, ct);
        await Assert.ThrowsAsync<ArgumentException>(() => store.CreateFileAttachmentAsync(measured with { Sha256 = new string('b', 64) }, parent.Card.Id, parent.User, "Changed", "image/jpeg", Now, ct));
        Assert.Equal(row, await store.FindAttachmentAsync(parent.Organization, parent.Card.Id, row.Id, ct));
        Assert.Equal(measured.Sha256, (await store.FindFileAttachmentAsync(parent.Organization, parent.Card.Id, row.Id, ct))!.Integrity.Sha256);
        var fresh = measured with { Reference = new(parent.Organization, Guid.NewGuid()) };
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CreateFileAttachmentAsync(fresh, other.Card.Id, parent.User, "Foreign parent", "image/png", Now, ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CreateFileAttachmentAsync(fresh, parent.Card.Id, other.User, "Foreign uploader", "image/png", Now, ct));
        Assert.Null(await store.FindFileAttachmentAsync(parent.Organization, parent.Card.Id, fresh.Reference.AttachmentId, ct));
        var url = await store.CreateUrlAttachmentAsync(fresh.Reference.AttachmentId, parent.Organization, parent.Card.Id, parent.User, "Link", "https://example.test/", Now, ct);
        await Assert.ThrowsAsync<ArgumentException>(() => store.CreateFileAttachmentAsync(fresh, parent.Card.Id, parent.User, "Replacement file", "image/png", Now, ct));
        Assert.Equal(url, await store.FindAttachmentAsync(parent.Organization, parent.Card.Id, fresh.Reference.AttachmentId, ct)); Assert.Null(await store.FindFileAttachmentAsync(parent.Organization, parent.Card.Id, fresh.Reference.AttachmentId, ct));
    }
    [Fact]
    public async Task Invalid_measured_digest_size_and_cancellation_cannot_partially_publish_file_metadata()
    {
        var ct = TestContext.Current.CancellationToken; using var services = Demo(); var parent = await Parent(services, ct); var store = services.GetRequiredService<IAttachmentMetadataStore>();
        var measured = new StoredAttachmentObject(new(parent.Organization, Guid.NewGuid()), 128, new string('a', 64));
        foreach (var invalid in new[] { measured with { Sha256 = "private-provider-detail" }, measured with { SizeBytes = 0 }, measured with { SizeBytes = 1073741825 } })
            await Assert.ThrowsAnyAsync<ArgumentException>(() => store.CreateFileAttachmentAsync(invalid, parent.Card.Id, parent.User, "Image", "image/png", Now, ct));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.CreateFileAttachmentAsync(measured, parent.Card.Id, parent.User, "Image", "image/png", Now, cancelled.Token));
        Assert.Empty(await store.ListAttachmentsAsync(parent.Organization, parent.Card.Id, null, null, ct)); Assert.Null(await store.FindFileAttachmentAsync(parent.Organization, parent.Card.Id, measured.Reference.AttachmentId, ct));
    }
    [Fact]
    public async Task Production_metadata_paths_refuse_unscoped_calls_before_database_access_or_metadata_validation()
    {
        var ct = TestContext.Current.CancellationToken; var services = new ServiceCollection();
        services.AddSingleton(new PostgresConnectionFactory("Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused;Timeout=1"));
        services.AddStrataAiWorkManagement(new(RuntimeMode.Production, "test", "test")); await using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IAttachmentMetadataStore>(); var organization = Guid.NewGuid(); var card = Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CreateUrlAttachmentAsync(Guid.Empty, organization, card, Guid.Empty, "", "javascript:bad", Now, ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.FindAttachmentAsync(organization, card, Guid.NewGuid(), ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ListAttachmentsAsync(organization, card, Now, null, ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CreateFileAttachmentAsync(new(new(organization, Guid.NewGuid()), 0, "invalid"), card, Guid.Empty, "", "invalid", Now, ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.FindFileAttachmentAsync(organization, card, Guid.NewGuid(), ct));
    }
}
