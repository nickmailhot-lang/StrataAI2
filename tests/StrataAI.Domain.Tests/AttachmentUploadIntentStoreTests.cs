using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Common;
using StrataAI.Application.Organizations;
using StrataAI.Application.Runtime;
using StrataAI.Application.WorkManagement;
using StrataAI.Domain.WorkManagement;
using StrataAI.Infrastructure.Identity;
using StrataAI.Infrastructure.Organizations;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Infrastructure.WorkManagement;
using Xunit;

namespace StrataAI.Domain.Tests;

public sealed class AttachmentUploadIntentStoreTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-03T12:00:00Z", CultureInfo.InvariantCulture);
    private static ServiceProvider Demo()
    {
        var services = new ServiceCollection(); var runtime = new RuntimeDescriptor(RuntimeMode.Demo, "test", "test");
        services.AddSingleton<IClock, SystemClock>(); services.AddStrataAiIdentity(new ConfigurationBuilder().Build(), runtime);
        services.AddStrataAiOrganizations(runtime); services.AddStrataAiWorkManagement(runtime); return services.BuildServiceProvider();
    }
    private static async Task<AttachmentUploadIntent> Intent(ServiceProvider services, CancellationToken ct)
    {
        var organization = Guid.NewGuid(); var user = Guid.NewGuid();
        await services.GetRequiredService<IOrganizationStore>().CreateOrganizationAsync(user, organization, "Uploads", null, Now, ct);
        var work = services.GetRequiredService<IWorkManagementStore>();
        var board = await work.CreateBoardAsync(organization, user, Guid.NewGuid(), "Private", null, BoardVisibility.Private, "COLOR", null, Now, ct);
        var list = await work.CreateListAsync(board.Id, Guid.NewGuid(), "List", null, Now, ct);
        var card = await work.CreateCardAsync(list.Id, Guid.NewGuid(), "Card", null, null, Now, ct);
        return AttachmentUploadIntent.Prepare(Guid.NewGuid(), organization, card.Id, user, Guid.NewGuid(), card.Version,
            "Image", 128, new string('a',64), Now.AddHours(1), Now);
    }
    private static Task<AttachmentUploadRecord?> Change(IAttachmentUploadIntentStore store, AttachmentUploadIntent value,
        long version, AttachmentUploadChange change, CancellationToken ct) => store.TryChangeUploadAsync(value.OrganizationId,
            value.CardId, value.UploaderId, value.Id, version, change, ct);
    private static StoredAttachmentObject Measured(AttachmentUploadIntent value, string? digest = null) =>
        new(new(value.OrganizationId, value.Id), value.ExpectedSizeBytes, digest ?? value.ExpectedSha256);

    [Fact]
    public async Task Durable_original_identity_survives_writers_and_publication_requires_matching_pending_metadata()
    {
        var ct = TestContext.Current.CancellationToken; using var services = Demo(); var value = await Intent(services, ct);
        var store = services.GetRequiredService<IAttachmentUploadIntentStore>(); var prepared = await store.PrepareUploadAsync(value, ct); Assert.NotNull(prepared);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(prepared)); Assert.False(json.RootElement.TryGetProperty("ExpectedSha256", out _));
        var lease = Guid.NewGuid(); var writing = await Change(store, value, 1, new(AttachmentUploadAction.StartWrite, Now, lease, Now.AddMinutes(5)), ct);
        Assert.NotNull(writing); Assert.Equal(2, writing.Version);
        Assert.Null(await Change(store, value, 1, new(AttachmentUploadAction.UnknownWrite, Now, lease), ct));
        var stored = await Change(store, value, 2, new(AttachmentUploadAction.RecordStored, Now.AddMinutes(1), lease, Measured:Measured(value), VerifiedMimeType:"image/png"), ct);
        Assert.NotNull(stored); Assert.Equal(AttachmentUploadState.Stored, stored.State); Assert.Null(stored.WriteLeaseId);
        Assert.Null(await Change(store, value, 3, new(AttachmentUploadAction.Publish, Now.AddMinutes(1)), ct));
        await services.GetRequiredService<IAttachmentMetadataStore>().CreateFileAttachmentAsync(Measured(value), value.CardId,
            value.UploaderId, value.DisplayName, "image/png", Now.AddMinutes(1), ct);
        var published = await Change(store, value, 3, new(AttachmentUploadAction.Publish, Now.AddMinutes(2)), ct); Assert.NotNull(published);
        Assert.Equal(AttachmentUploadState.Published, published.State); Assert.Equal(4, published.Version);
        Assert.Equal(value.CardVersion, published.OriginalCardVersion); Assert.Equal(value.ExpectedSha256, published.ExpectedSha256);
        Assert.Null(await Change(store, value, 4, new(AttachmentUploadAction.Abandon, Now.AddMinutes(3)), ct));
        Assert.Equal(published, await store.FindUploadByRetryAsync(value.OrganizationId, value.UploaderId, value.RetryKey, ct));
        var privateFile=await services.GetRequiredService<IAttachmentMetadataStore>().FindFileAttachmentAsync(value.OrganizationId,value.CardId,value.Id,ct);
        Assert.NotNull(privateFile); var jobs=services.GetRequiredService<IAttachmentScanJobPublisher>();
        Assert.True(await jobs.PublishScanAsync(published,privateFile,value.UploaderId,"scan-first",ct));
        Assert.False(await jobs.PublishScanAsync(published,privateFile,value.UploaderId,"scan-retry",ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => jobs.PublishScanAsync(published with {Version=published.Version+1},privateFile,value.UploaderId,"scan-stale",ct));
    }
    [Fact]
    public async Task Competing_claims_have_one_winner_and_external_domain_mutation_cannot_change_persisted_original_intent()
    {
        var ct = TestContext.Current.CancellationToken; using var services = Demo(); var value = await Intent(services, ct);
        var store = services.GetRequiredService<IAttachmentUploadIntentStore>(); var original = await store.PrepareUploadAsync(value, ct);
        value.StartWrite(Guid.NewGuid(), Now.AddMinutes(2), Now);
        Assert.Equal(original, await store.FindUploadByRetryAsync(value.OrganizationId, value.UploaderId, value.RetryKey, ct));
        var attempts = await Task.WhenAll(Enumerable.Range(0,8).Select(_ => Task.Run(() => Change(store, value, 1,
            new(AttachmentUploadAction.StartWrite, Now, Guid.NewGuid(), Now.AddMinutes(5)), ct), ct)));
        Assert.Single(attempts, x => x is not null);
        var duplicate = AttachmentUploadIntent.Prepare(Guid.NewGuid(), value.OrganizationId, value.CardId, value.UploaderId,
            value.RetryKey, 99, "Changed", 256, new string('b',64), Now.AddHours(1), Now);
        Assert.Null(await store.PrepareUploadAsync(duplicate, ct));
        var current = await store.FindUploadByRetryAsync(value.OrganizationId, value.UploaderId, value.RetryKey, ct); Assert.NotNull(current);
        Assert.Equal(value.Id, current.Id); Assert.Equal(128, current.ExpectedSizeBytes); Assert.Equal("Image", current.DisplayName);
    }
    [Fact]
    public async Task Unknown_provider_outcome_requires_reconciliation_and_rejects_old_nonce_or_wrong_measurements()
    {
        var ct = TestContext.Current.CancellationToken; using var services = Demo(); var value = await Intent(services, ct);
        var store = services.GetRequiredService<IAttachmentUploadIntentStore>(); await store.PrepareUploadAsync(value, ct);
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        await Change(store, value, 1, new(AttachmentUploadAction.StartWrite, Now, first, Now.AddMinutes(5)), ct);
        Assert.Null(await Change(store, value, 2, new(AttachmentUploadAction.UnknownWrite, Now, second), ct));
        await Change(store, value, 2, new(AttachmentUploadAction.UnknownWrite, Now.AddMinutes(1), first), ct);
        Assert.Null(await Change(store, value, 3, new(AttachmentUploadAction.StartWrite, Now.AddMinutes(1), second, Now.AddMinutes(5)), ct));
        Assert.Null(await Change(store, value, 3, new(AttachmentUploadAction.RecordReconciled, Now.AddMinutes(1), Measured:Measured(value,new string('b',64)), VerifiedMimeType:"image/png"), ct));
        await Change(store, value, 3, new(AttachmentUploadAction.ConfirmMissing, Now.AddMinutes(1)), ct);
        await Change(store, value, 4, new(AttachmentUploadAction.StartWrite, Now.AddMinutes(1), second, Now.AddMinutes(5)), ct);
        Assert.Null(await Change(store, value, 5, new(AttachmentUploadAction.RecordStored, Now.AddMinutes(2), first, Measured:Measured(value), VerifiedMimeType:"image/png"), ct));
        var result = await Change(store, value, 5, new(AttachmentUploadAction.RecordStored, Now.AddMinutes(2), second, Measured:Measured(value), VerifiedMimeType:"image/png"), ct);
        Assert.NotNull(result); Assert.Equal(AttachmentUploadState.Stored,result.State); Assert.Equal(6,result.Version);
    }
    [Fact]
    public async Task Wrong_scope_and_expired_writer_never_become_permission_to_reuse_retained_retry_identity()
    {
        var ct = TestContext.Current.CancellationToken; using var services = Demo(); var value = await Intent(services, ct); var other = await Intent(services, ct);
        var store = services.GetRequiredService<IAttachmentUploadIntentStore>(); var prepared = await store.PrepareUploadAsync(value, ct);
        Assert.Null(await store.FindUploadByRetryAsync(other.OrganizationId,value.UploaderId,value.RetryKey,ct));
        Assert.Null(await store.FindUploadByRetryAsync(value.OrganizationId,other.UploaderId,value.RetryKey,ct));
        foreach (var scope in new[] { (other.OrganizationId,value.CardId,value.UploaderId), (value.OrganizationId,other.CardId,value.UploaderId), (value.OrganizationId,value.CardId,other.UploaderId) })
            Assert.Null(await store.TryChangeUploadAsync(scope.Item1,scope.Item2,scope.Item3,value.Id,1,new(AttachmentUploadAction.Abandon,Now),ct));
        Assert.Equal(prepared,await store.FindUploadByRetryAsync(value.OrganizationId,value.UploaderId,value.RetryKey,ct));
        await Change(store,value,1,new(AttachmentUploadAction.StartWrite,Now,Guid.NewGuid(),Now.AddMinutes(5)),ct);
        Assert.Null(await Change(store,value,2,new(AttachmentUploadAction.ExpiredWriter,Now.AddMinutes(4)),ct));
        await Change(store,value,2,new(AttachmentUploadAction.ExpiredWriter,Now.AddHours(1)),ct);
        Assert.Null(await Change(store,value,3,new(AttachmentUploadAction.ConfirmMissing,Now.AddHours(1)),ct));
        var abandoned = await Change(store,value,3,new(AttachmentUploadAction.Abandon,Now.AddHours(1)),ct); Assert.NotNull(abandoned);
        Assert.Equal(AttachmentUploadState.Abandoned,abandoned.State); Assert.Null(await store.PrepareUploadAsync(value,ct));
    }
    [Fact]
    public async Task Invalid_inputs_cancellation_and_unscoped_production_calls_fail_before_provider_database_IO()
    {
        var ct = TestContext.Current.CancellationToken; using var services = Demo(); var value = await Intent(services,ct);
        var store = services.GetRequiredService<IAttachmentUploadIntentStore>(); var prepared = await store.PrepareUploadAsync(value,ct);
        await Assert.ThrowsAsync<ArgumentException>(() => Change(store,value,1,new(AttachmentUploadAction.StartWrite,Now,Guid.Empty,Now.AddMinutes(5)),ct));
        await Assert.ThrowsAsync<ArgumentException>(() => Change(store,value,1,new(AttachmentUploadAction.RecordReconciled,Now,Measured:Measured(value),VerifiedMimeType:"IMAGE/PNG"),ct));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Change(store,value,1,new(AttachmentUploadAction.Abandon,Now),cancel.Token));
        Assert.Equal(prepared,await store.FindUploadByRetryAsync(value.OrganizationId,value.UploaderId,value.RetryKey,ct));
        var productionServices = new ServiceCollection(); productionServices.AddSingleton(new PostgresConnectionFactory("Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused;Timeout=1"));
        productionServices.AddSingleton<StrataAI.Infrastructure.BackgroundJobs.PostgresBackgroundJobStore>();
        productionServices.AddStrataAiWorkManagement(new RuntimeDescriptor(RuntimeMode.Production,"test","test"));
        await using var production = productionServices.BuildServiceProvider(); var pg = production.GetRequiredService<IAttachmentUploadIntentStore>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => pg.PrepareUploadAsync(value,ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => pg.FindUploadByRetryAsync(value.OrganizationId,value.UploaderId,value.RetryKey,ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Change(pg,value,1,new(AttachmentUploadAction.Abandon,Now),ct));
        var pgJobs=production.GetRequiredService<IAttachmentScanJobPublisher>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => pgJobs.PublishScanAsync(AttachmentUploadRecord.From(value),null!,value.UploaderId,"",ct));
    }
}
