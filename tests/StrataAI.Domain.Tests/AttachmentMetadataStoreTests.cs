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
    public async Task Production_metadata_paths_refuse_unscoped_calls_before_database_access_or_metadata_validation()
    {
        var ct = TestContext.Current.CancellationToken; var services = new ServiceCollection();
        services.AddSingleton(new PostgresConnectionFactory("Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused;Timeout=1"));
        services.AddStrataAiWorkManagement(new(RuntimeMode.Production, "test", "test")); await using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IAttachmentMetadataStore>(); var organization = Guid.NewGuid(); var card = Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CreateUrlAttachmentAsync(Guid.Empty, organization, card, Guid.Empty, "", "javascript:bad", Now, ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.FindAttachmentAsync(organization, card, Guid.NewGuid(), ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ListAttachmentsAsync(organization, card, Now, null, ct));
    }
}
