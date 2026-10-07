using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;
using StrataAI.Domain.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task PRD_03_Demo_graph_pages_include_archives_preserve_attribution_and_leave_other_tenants_unchanged()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var recipient = app.CreateClient();
        var f = await NotificationFixture(app, owner, recipient, ct);
        var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var organizations = app.Services.GetRequiredService<IOrganizationStore>();
        var unit = app.Services.GetRequiredService<IOrganizationUnitOfWork>();
        var graph = app.Services.GetRequiredService<IOrganizationDeletionGraphSimulation>();
        var publisher = app.Services.GetRequiredService<IOrganizationDeletionJobPublisher>();
        var attachments = app.Services.GetRequiredService<IAttachmentMetadataStore>();
        var now = DateTimeOffset.UtcNow; var request = Guid.NewGuid();
        List<CardRecord> archived = [];
        for (var i = 0; i < 130; i++)
        {
            var card = await work.CreateCardAsync(f.List, Guid.NewGuid(), "Archived graph child", "Retained description", null, now, ct);
            archived.Add((await work.SetCardLifecycleAsync(card.Id, WorkItemLifecycleState.Active,
                WorkItemLifecycleState.Archived, 1, now.AddSeconds(1), ct))!);
        }
        var attachment = await attachments.CreateUrlAttachmentAsync(Guid.NewGuid(), f.Organization, archived[0].Id,
            f.Owner, "Historical URL", "https://example.test/history", now, ct);
        attachment = (await attachments.ChangeAttachmentLifecycleAsync(f.Organization, archived[0].Id, attachment.Id,
            1, AttachmentLifecycleState.Active, AttachmentLifecycleState.Archived, f.Owner, now.AddSeconds(2), ct))!;
        // Measured-file fixture seeds metadata/proof only, without provider I/O.
        var fileReference = new AttachmentObjectReference(f.Organization, Guid.NewGuid());
        var file = await attachments.CreateFileAttachmentAsync(new(fileReference, 128, new string('a', 64)), archived[0].Id,
            f.Owner, "Retained quarantined file", "image/png", now, ct);
        var integrity = (System.Collections.IDictionary)work.GetType().GetField("_attachmentIntegrity", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(work)!;
        var originalIntegrity = integrity[file.Id];
        var alreadyDeleted = await work.CreateCardAsync(f.List, Guid.NewGuid(), "Prior deletion", null, null, now, ct);
        await work.SetCardLifecycleAsync(alreadyDeleted.Id, WorkItemLifecycleState.Active, WorkItemLifecycleState.Archived, 1, now.AddSeconds(1), ct);
        alreadyDeleted = (await work.SetCardLifecycleAsync(alreadyDeleted.Id, WorkItemLifecycleState.Archived,
            WorkItemLifecycleState.Deleted, 2, now.AddSeconds(2), ct, f.Recipient))!;
        // Retained-reference recovery also includes already-deleted parents.
        var covers = (Dictionary<Guid, Guid>)work.GetType().GetField("_cardCovers", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(work)!;
        covers.Add(alreadyDeleted.Id, attachment.Id);
        var deletedBoard = await work.CreateBoardAsync(f.Organization, f.Owner, Guid.NewGuid(), "Old image Board", null,
            BoardVisibility.Private, "IMAGE", Guid.NewGuid().ToString(), now, ct);
        await work.SetBoardLifecycleAsync(deletedBoard.Id, BoardLifecycleState.Active, BoardLifecycleState.Archived, 1, now.AddSeconds(1), ct);
        deletedBoard = (await work.SetBoardLifecycleAsync(deletedBoard.Id, BoardLifecycleState.Archived,
            BoardLifecycleState.Deleted, 2, now.AddSeconds(2), ct, f.Recipient))!;
        var list = (await work.SetListLifecycleAsync(f.List, WorkItemLifecycleState.Active, WorkItemLifecycleState.Archived, 1, now.AddSeconds(1), ct))!;
        var board = (await work.SetBoardLifecycleAsync(f.Board, BoardLifecycleState.Active, BoardLifecycleState.Archived, 1, now.AddSeconds(1), ct))!;
        using var otherResponse = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Other tenant" });
        var other = (await otherResponse.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        var otherBoard = await work.CreateBoardAsync(other, f.Owner, Guid.NewGuid(), "Unaffected", null, BoardVisibility.Private, "COLOR", "blue", now, ct);
        var otherList = await work.CreateListAsync(otherBoard.Id, Guid.NewGuid(), "Unaffected", null, now, ct);
        var otherCard = await work.CreateCardAsync(otherList.Id, Guid.NewGuid(), "Unaffected", null, null, now, ct);
        Assert.True((await unit.ExecuteAsync(f.Organization, f.Owner, null, false, async () =>
        {
            Assert.True(await organizations.MarkDeletingAsync(f.Organization, 1, now.AddSeconds(3), ct));
            Assert.True(await publisher.PublishAsync(f.Organization, f.Owner, request, 2, "original-graph", ct));
            return OrganizationOperation<bool>.Success(true);
        }, ct)).Succeeded);
        List<int> pages = [];
        for (var i = 0; i < 8; i++)
        {
            var result = await unit.ExecuteAsync(f.Organization, f.Owner, null, false, async () =>
                OrganizationOperation<int>.Success(await graph.ApplyPageAsync(f.Organization, f.Owner, request, 2, 128, ct)),
                ct, allowDeletionRecovery: true);
            Assert.True(result.Succeeded); pages.Add(result.Value); if (result.Value == 0) break;
        }
        Assert.Equal(new[] { 2, 128, 3, 1, 2, 0 }, pages);
        foreach (var original in archived)
        {
            var tombstone = (await work.FindCardAsync(original.Id, ct, includeDeleted: true))!;
            Assert.Equal(WorkItemLifecycleState.Deleted, tombstone.LifecycleState); Assert.Equal(f.Owner, tombstone.DeletedBy);
            Assert.Equal(original.ArchivedAt, tombstone.ArchivedAt); Assert.Equal(original.Description, tombstone.Description);
            Assert.True(tombstone.UpdatedAt >= original.UpdatedAt); Assert.True(tombstone.Version > original.Version);
        }
        var historicalCard = (await work.FindCardAsync(alreadyDeleted.Id, ct, includeDeleted: true))!;
        Assert.Equal(alreadyDeleted.DeletedAt, historicalCard.DeletedAt); Assert.Equal(f.Recipient, historicalCard.DeletedBy);
        Assert.False(covers.ContainsKey(alreadyDeleted.Id));
        var historicalBoard = (await work.FindBoardAsync(deletedBoard.Id, ct, includeDeleted: true))!;
        Assert.Equal(deletedBoard.DeletedAt, historicalBoard.DeletedAt); Assert.Equal(f.Recipient, historicalBoard.DeletedBy);
        Assert.Equal("COLOR", historicalBoard.BackgroundType); Assert.Null(historicalBoard.BackgroundValue);
        var removedAttachment = (await attachments.FindLifecycleAttachmentAsync(f.Organization, attachment.CardId, attachment.Id, ct))!;
        Assert.Equal(AttachmentLifecycleState.Deleted, removedAttachment.LifecycleState); Assert.Equal(attachment.ArchivedAt, removedAttachment.ArchivedAt);
        Assert.Equal(attachment.Url, removedAttachment.Url); Assert.Equal(f.Owner, removedAttachment.DeletedBy);
        var removedFile = (await attachments.FindLifecycleAttachmentAsync(f.Organization, file.CardId, file.Id, ct))!;
        Assert.Equal(AttachmentLifecycleState.Deleted, removedFile.LifecycleState); Assert.Equal(file.ScanStatus, removedFile.ScanStatus);
        Assert.Equal(file.MimeType, removedFile.MimeType); Assert.Equal(file.SizeBytes, removedFile.SizeBytes);
        Assert.Equal(originalIntegrity, integrity[file.Id]);
        Assert.Equal(list.ArchivedAt, (await work.FindListAsync(f.List, ct, includeDeleted: true))!.ArchivedAt);
        Assert.Equal(board.ArchivedAt, (await work.FindBoardAsync(f.Board, ct, includeDeleted: true))!.ArchivedAt);
        Assert.Equal(otherBoard, await work.FindBoardAsync(otherBoard.Id, ct)); Assert.Equal(otherList, await work.FindListAsync(otherList.Id, ct));
        Assert.Equal(otherCard, await work.FindCardAsync(otherCard.Id, ct));
        // Exhausted graph is not a fabricated terminal source or acknowledgment.
        Assert.Equal(OrganizationStatus.Deleting, (await organizations.FindOrganizationAsync(f.Organization, ct))!.Status);
        Assert.Equal("PENDING", (await app.Services.GetRequiredService<IOrganizationDeletionObservationReader>()
            .ReadAsync(f.Organization, f.Owner, request, ct)).Value!.State);
    }

    [Theory]
    [InlineData("failure")]
    [InlineData("actor")]
    [InlineData("event")]
    [InlineData("cancel")]
    public async Task PRD_03_Demo_graph_page_rolls_back_children_and_events_when_owning_command_cannot_commit(string outcome)
    {
        var ct = TestContext.Current.CancellationToken; var fence = new OrganizationTransactionActorFixture();
        GraphEventFailure? events = null;
        await using var app = new ApiFactory(configureServices: services =>
        {
            services.AddSingleton<ICommandActorAuthorization>(fence);
            var original = services.Last(d => d.ServiceType == typeof(IWorkEventStore));
            services.AddSingleton<IWorkEventStore>(provider => events = new((IWorkEventStore)original.ImplementationFactory!(provider)));
        });
        using var owner = app.CreateClient(); using var recipient = app.CreateClient(); var f = await NotificationFixture(app, owner, recipient, ct);
        var work = app.Services.GetRequiredService<IWorkManagementStore>(); var organizations = app.Services.GetRequiredService<IOrganizationStore>();
        var unit = app.Services.GetRequiredService<IOrganizationUnitOfWork>(); var graph = app.Services.GetRequiredService<IOrganizationDeletionGraphSimulation>();
        var publisher = app.Services.GetRequiredService<IOrganizationDeletionJobPublisher>(); var request = Guid.NewGuid();
        var card = await work.CreateCardAsync(f.List, Guid.NewGuid(), "Rollback descendant", null, null, DateTimeOffset.UtcNow, ct);
        var attachments = app.Services.GetRequiredService<IAttachmentMetadataStore>();
        var attachment = await attachments.CreateUrlAttachmentAsync(Guid.NewGuid(), f.Organization, card.Id, f.Owner,
            "Rollback URL", "https://example.test/rollback", DateTimeOffset.UtcNow, ct);
        var retainedAudits = (System.Collections.IDictionary)graph.GetType().GetField("_audits", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(graph)!;
        var beforeAudits = retainedAudits.Count;
        var eventReader = app.Services.GetRequiredService<IWorkEventReader>();
        var retainedEvents = (System.Collections.IDictionary)eventReader.GetType().GetField("_events", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(eventReader)!;
        var beforeEvents = retainedEvents.Count;
        await Assert.ThrowsAsync<OrganizationDeletionPublicationUnavailableException>(() => graph.ApplyPageAsync(f.Organization, f.Owner, request, 2, 128, ct));
        Assert.True((await unit.ExecuteAsync(f.Organization, f.Owner, null, false, async () =>
        {
            await Assert.ThrowsAsync<OrganizationDeletionPublicationUnavailableException>(() => graph.ApplyPageAsync(f.Organization, f.Owner, request, 2, 128, ct));
            Assert.True(await organizations.MarkDeletingAsync(f.Organization, 1, DateTimeOffset.UtcNow, ct));
            Assert.True(await publisher.PublishAsync(f.Organization, f.Owner, request, 2, "graph-rollback", ct));
            return OrganizationOperation<bool>.Success(true);
        }, ct)).Succeeded);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
        async Task<OrganizationOperation<bool>> Page()
        {
            if (outcome == "event") events!.Armed = true;
            Assert.Equal(1, await graph.ApplyPageAsync(f.Organization, f.Owner, request, 2, 128, cancel.Token));
            if (outcome == "actor") fence.Allowed = false;
            if (outcome == "cancel") cancel.Cancel();
            return outcome == "failure" ? OrganizationOperation<bool>.Failure("refused") : OrganizationOperation<bool>.Success(true);
        }
        if (outcome == "event") await Assert.ThrowsAsync<InvalidOperationException>(() => unit.ExecuteAsync(f.Organization, f.Owner, null, false, Page, ct, allowDeletionRecovery: true));
        else if (outcome == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => unit.ExecuteAsync(f.Organization, f.Owner, null, false, Page, cancel.Token, allowDeletionRecovery: true));
        else Assert.False((await unit.ExecuteAsync(f.Organization, f.Owner, null, false, Page, ct, allowDeletionRecovery: true)).Succeeded);
        Assert.Equal(card, await work.FindCardAsync(card.Id, ct));
        Assert.Equal(beforeEvents, retainedEvents.Count);
        Assert.Equal(attachment, await attachments.FindLifecycleAttachmentAsync(f.Organization, card.Id, attachment.Id, ct));
        Assert.Equal(beforeAudits, retainedAudits.Count);
        fence.Allowed = true; events!.Armed = false;
        Assert.True((await unit.ExecuteAsync(f.Organization, f.Owner, null, false, async () =>
        {
            Assert.Equal(1, await graph.ApplyPageAsync(f.Organization, f.Owner, request, 2, 128, ct));
            return OrganizationOperation<bool>.Success(true);
        }, ct, allowDeletionRecovery: true)).Succeeded);
        Assert.Equal(2, (await work.FindCardAsync(card.Id, ct, includeDeleted: true))!.Version);
        Assert.Equal(beforeEvents + 1, retainedEvents.Count);
        Assert.Equal(beforeAudits + 1, retainedAudits.Count);
        Assert.Equal(AttachmentLifecycleState.Deleted, (await attachments.FindLifecycleAttachmentAsync(f.Organization, card.Id, attachment.Id, ct))!.LifecycleState);
    }

    [Fact]
    public async Task PRD_03_Demo_graph_requires_exact_root_version_current_owner_and_bounded_page()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var recipient = app.CreateClient();
        var f = await NotificationFixture(app, owner, recipient, ct);
        var work = app.Services.GetRequiredService<IWorkManagementStore>(); var organizations = app.Services.GetRequiredService<IOrganizationStore>();
        var unit = app.Services.GetRequiredService<IOrganizationUnitOfWork>(); var graph = app.Services.GetRequiredService<IOrganizationDeletionGraphSimulation>();
        var publisher = app.Services.GetRequiredService<IOrganizationDeletionJobPublisher>(); var request = Guid.NewGuid();
        var card = await work.CreateCardAsync(f.List, Guid.NewGuid(), "Protected graph child", null, null, DateTimeOffset.UtcNow, ct);
        Assert.True((await unit.ExecuteAsync(f.Organization, f.Owner, null, false, async () =>
        {
            Assert.True(await organizations.MarkDeletingAsync(f.Organization, 1, DateTimeOffset.UtcNow, ct));
            Assert.True(await publisher.PublishAsync(f.Organization, f.Owner, request, 2, "root-authority", ct));
            return OrganizationOperation<bool>.Success(true);
        }, ct)).Succeeded);
        Assert.True((await unit.ExecuteAsync(f.Organization, f.Owner, null, false, async () =>
        {
            foreach (var limit in new[] { 0, 129 })
                await Assert.ThrowsAsync<OrganizationDeletionPublicationUnavailableException>(() => graph.ApplyPageAsync(f.Organization, f.Owner, request, 2, limit, ct));
            await Assert.ThrowsAsync<OrganizationDeletionPublicationUnavailableException>(() => graph.ApplyPageAsync(f.Organization, f.Owner, Guid.NewGuid(), 2, 128, ct));
            await Assert.ThrowsAsync<OrganizationDeletionPublicationUnavailableException>(() => graph.ApplyPageAsync(f.Organization, f.Owner, request, 3, 128, ct));
            await Assert.ThrowsAsync<OrganizationDeletionPublicationUnavailableException>(() => graph.ApplyPageAsync(f.Organization, f.Recipient, request, 2, 128, ct));
            await Assert.ThrowsAsync<OrganizationDeletionPublicationUnavailableException>(() => graph.ApplyPageAsync(Guid.NewGuid(), f.Owner, request, 2, 128, ct));
            await organizations.AddOrRestoreMemberAsync(f.Organization, f.Owner, OrganizationRole.Admin, DateTimeOffset.UtcNow, ct);
            await Assert.ThrowsAsync<OrganizationDeletionPublicationUnavailableException>(() => graph.ApplyPageAsync(f.Organization, f.Owner, request, 2, 128, ct));
            return OrganizationOperation<bool>.Success(true);
        }, ct, allowDeletionRecovery: true)).Succeeded);
        Assert.Equal(card, await work.FindCardAsync(card.Id, ct));
    }

    private sealed class GraphEventFailure(IWorkEventStore inner) : IWorkEventStore
    {
        public bool Armed { get; set; }
        public async Task AppendAsync(WorkEvent change, CancellationToken cancellationToken = default)
        {
            await inner.AppendAsync(change, cancellationToken);
            if (Armed) throw new InvalidOperationException("Failure after actual graph event append.");
        }
    }
}
