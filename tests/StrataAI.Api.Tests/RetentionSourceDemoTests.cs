using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.Records;
using StrataAI.Application.WorkManagement;
using StrataAI.Domain.Records;
using StrataAI.Infrastructure.Organizations;
using StrataAI.Infrastructure.Records;
using StrataAI.Infrastructure.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task PRD_70_Demo_sources_capture_real_producers_original_context_and_atomic_audit()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(configureServices: services =>
        {
            services.AddSingleton<InMemoryRetentionSourceStore>();
            services.AddSingleton<IDemoOrganizationTransactionParticipant>(p => p.GetRequiredService<InMemoryRetentionSourceStore>());
        });
        using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (actor, parent) = await ConfigurationParent(owner, "Trusted Demo sources", ct);
        var catalog = app.Services.GetRequiredService<InMemoryRetentionSourceStore>();
        var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var organizations = app.Services.GetRequiredService<IOrganizationStore>();
        var unit = app.Services.GetRequiredService<IOrganizationUnitOfWork>();
        var auth = new RetentionWorkAuthorization(catalog, catalog, work, app.Services.GetRequiredService<IWorkBoardAuthorization>(),
            organizations, app.Services.GetRequiredService<ICommandActorAuthorization>());
        var reviewAuth = new RetentionProducerAuthorization(catalog, work, app.Services.GetRequiredService<IWorkBoardAuthorization>(),
            organizations, app.Services.GetRequiredService<ICommandActorAuthorization>());
        var reviewEvidence = new List<RetentionProducerReference>();
        var board = Guid.NewGuid(); var destination = Guid.NewGuid(); var list = Guid.NewGuid(); var destinationList = Guid.NewGuid();
        var card = Guid.NewGuid(); var comment = Guid.NewGuid(); var attachment = Guid.NewGuid(); var rollbackCard = Guid.NewGuid();
        var items = new[] { (card, RetentionProducerKind.Card, RetentionRecordClass.OperationalWork),
            (comment, RetentionProducerKind.CardComment, RetentionRecordClass.OperationalWork),
            (attachment, RetentionProducerKind.Attachment, RetentionRecordClass.Document) };
        DateTimeOffset Now() => AttachmentMetadataMapping.DatabaseTimestamp(app.Services.GetRequiredService<IClock>().UtcNow);
        await ConfigurationOwned(unit, parent.Id, actor, null, false, async () =>
        {
            await work.CreateBoardAsync(parent.Id, actor, board, "Original", null, BoardVisibility.Private, "COLOR", null, Now(), ct);
            await work.CreateBoardAsync(parent.Id, actor, destination, "Destination", null, BoardVisibility.Private, "COLOR", null, Now(), ct);
            await work.CreateListAsync(board, list, "Original list", null, Now(), ct);
            await work.CreateListAsync(destination, destinationList, "Destination list", null, Now(), ct);
            await work.CreateCardAsync(list, card, "Private work", null, null, Now(), ct);
            await work.CreateCardAsync(list, rollbackCard, "Rollback work", null, null, Now(), ct);
            await ((ICardCommentStore)work).CreateAsync(comment, parent.Id, card, actor, "Private comment", Now(), ct);
            await ((IAttachmentMetadataStore)work).CreateUrlAttachmentAsync(attachment, parent.Id, card, actor, "Private file", "https://example.test/private", Now(), ct);
            var member = (await organizations.FindMembershipAsync(parent.Id, actor, ct))!;
            foreach (var item in items)
            {
                var producer = await catalog.ReadAsync(parent.Id, item.Item2, item.Item1, ct);
                Assert.NotNull(producer); Assert.Equal(board, producer.BoardId); Assert.Equal(1, producer.SourceVersion);
                Assert.Equal(1, producer.CardVersion); Assert.Equal(1, producer.ListVersion); Assert.Equal(TimeSpan.Zero, producer.CreatedAt.Offset);
                Assert.Equal(item.Item2 == RetentionProducerKind.Card ? null : (Guid?)card, producer.ParentCardId);
                Assert.Null(await catalog.ReadSourceAsync(parent.Id, item.Item3, item.Item1, ct));
                Assert.True(await reviewAuth.VerifyAsync(producer, actor, ct));
                Assert.False(await reviewAuth.VerifyAsync(producer, Guid.NewGuid(), ct));
                Assert.False(await reviewAuth.VerifyAsync(producer with { SourceVersion = 99 }, actor, ct));
                Assert.False(await reviewAuth.VerifyAsync(producer with { Kind = RetentionProducerKind.AuditEvent }, actor, ct));
                reviewEvidence.Add(producer);
                var write = new RetentionSourceWrite(parent.Id, item.Item3, item.Item1, item.Item2, "private.fixture", "explicit jurisdiction",
                    1, 1, actor, member.Id, member.Version, member.Role, Guid.NewGuid(), "demo-source", Now(), producer);
                Assert.Null(await catalog.CaptureAsync(write with { MembershipId = Guid.NewGuid() }, ct));
                Assert.Null(await catalog.CaptureAsync(write with { SourceVersion = 2 }, ct));
                Assert.Null(await catalog.CaptureAsync(write with { Kind = RetentionProducerKind.AuditEvent }, ct));
                Assert.Null(await catalog.CaptureAsync(write with { Review = producer with { BoardId = destination } }, ct));
                Assert.Null(await catalog.CaptureAsync(write with { Review = producer with { ListVersion = 2 } }, ct));
                var captured = await catalog.CaptureAsync(write, ct);
                Assert.NotNull(captured); Assert.Equal(board, captured.OriginalBoardId); Assert.Equal(1, captured.SourceVersion);
                Assert.Equal(item.Item2 == RetentionProducerKind.Card ? null : (Guid?)card, captured.ParentCardId);
                Assert.Null(await catalog.CaptureAsync(write with { Category = "rewritten", EventId = Guid.NewGuid() }, ct));
            }
            Assert.NotNull(await work.MoveCardAsync(card, destinationList, null, 1, Now(), ct));
            foreach (var previous in reviewEvidence)
            {
                Assert.False(await reviewAuth.VerifyAsync(previous, actor, ct));
                var moved = await catalog.ReadAsync(parent.Id, previous.Kind, previous.RecordId, ct);
                Assert.NotNull(moved); Assert.True(await reviewAuth.VerifyAsync(moved, actor, ct));
                var changed = new RetentionProducerAuthorization(new ChangedReviewProducer(catalog), work,
                    app.Services.GetRequiredService<IWorkBoardAuthorization>(), organizations,
                    app.Services.GetRequiredService<ICommandActorAuthorization>());
                Assert.False(await changed.VerifyAsync(moved, actor, ct));
            }
            foreach (var item in items)
            {
                var current = await catalog.ReadAsync(parent.Id, item.Item3, item.Item1, ct);
                Assert.NotNull(current); Assert.Equal(destination, current.BoardId); Assert.Equal(2, current.CardVersion);
                Assert.Equal(board, current.Classification.OriginalBoardId); Assert.Equal(1, current.Classification.SourceVersion);
                Assert.True(await auth.VerifySourceAsync(parent.Id, item.Item3, item.Item1, actor, ct));
            }
            Assert.Null(await catalog.ReadSourceAsync(parent.Id, RetentionRecordClass.Document, card, ct));
            Assert.Null(await catalog.ReadSourceAsync(parent.Id, RetentionRecordClass.OperationalWork, Guid.NewGuid(), ct));
            Assert.Null(await catalog.ReadAsync(parent.Id, RetentionProducerKind.AuditEvent, card, ct));
            Assert.Null(await catalog.ReadAsync(parent.Id, RetentionProducerKind.Attachment, card, ct));
            Assert.Null(await catalog.ReadAsync(parent.Id, RetentionProducerKind.Card, Guid.NewGuid(), ct));
            return OrganizationOperation<bool>.Success(true);
        }, ct);
        var audits = ((InMemoryWorkManagementStore)work).AuditSnapshot(parent.Id).Where(a => a.EventType == "RECORD_RETENTION_SOURCE_REGISTERED").ToArray();
        Assert.Equal(3, audits.Length);
        Assert.All(audits, audit =>
        {
            Assert.NotNull(audit.SourceMetadata);
            using var metadata = JsonDocument.Parse(JsonSerializer.Serialize(audit.SourceMetadata));
            Assert.Equal(3, metadata.RootElement.EnumerateObject().Count());
            Assert.DoesNotContain("private.fixture", metadata.RootElement.GetRawText(), StringComparison.Ordinal);
            Assert.DoesNotContain(board.ToString(), metadata.RootElement.GetRawText(), StringComparison.Ordinal);
        });
        var failed = await unit.ExecuteAsync(parent.Id, actor, null, false, async () =>
        {
            var member = (await organizations.FindMembershipAsync(parent.Id, actor, ct))!;
            Assert.NotNull(await catalog.CaptureAsync(new(parent.Id, RetentionRecordClass.OperationalWork, rollbackCard,
                RetentionProducerKind.Card, "private.rollback", null, 1, 1, actor, member.Id, member.Version, member.Role,
                Guid.NewGuid(), "source-rollback", Now(), (await catalog.ReadAsync(parent.Id, RetentionProducerKind.Card, rollbackCard, ct))!), ct));
            return OrganizationOperation<bool>.Failure("source_fixture_rollback");
        }, ct);
        Assert.False(failed.Succeeded);
        await ConfigurationOwned(unit, parent.Id, actor, null, false, async () =>
        {
            Assert.Null(await catalog.ReadSourceAsync(parent.Id, RetentionRecordClass.OperationalWork, rollbackCard, ct));
            return OrganizationOperation<bool>.Success(true);
        }, ct);
        Assert.Equal(3, ((InMemoryWorkManagementStore)work).AuditSnapshot(parent.Id).Count(a => a.EventType == "RECORD_RETENTION_SOURCE_REGISTERED"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => catalog.ReadSourceAsync(parent.Id, RetentionRecordClass.OperationalWork, card, ct));
    }
    // Explicit race control: the actual producer changes after Board admission.
    private sealed class ChangedReviewProducer(IRetentionProducerReferences inner) : IRetentionProducerReferences
    {
        private int reads;
        public async Task<RetentionProducerReference?> ReadAsync(Guid organization, RetentionProducerKind kind, Guid record, CancellationToken ct)
        {
            var value = await inner.ReadAsync(organization, kind, record, ct);
            return ++reads > 1 && value is not null ? value with { SourceVersion = value.SourceVersion + 1 } : value;
        }
    }
}
