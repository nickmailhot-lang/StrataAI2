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
    public async Task PRD_70_First_classification_review_owns_real_sources_refuses_changed_or_classified_evidence_and_does_not_write()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(configureServices: services =>
        {
            services.AddSingleton<InMemoryRetentionSourceStore>();
            services.AddSingleton<IDemoOrganizationTransactionParticipant>(p => p.GetRequiredService<InMemoryRetentionSourceStore>());
        });
        using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (actor, parent) = await ConfigurationParent(owner, "Classification review", ct);
        var catalog = app.Services.GetRequiredService<InMemoryRetentionSourceStore>();
        var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var organizations = app.Services.GetRequiredService<IOrganizationStore>();
        var unit = app.Services.GetRequiredService<IOrganizationUnitOfWork>();
        var actors = app.Services.GetRequiredService<ICommandActorAuthorization>();
        var boards = app.Services.GetRequiredService<IWorkBoardAuthorization>();
        var authorization = new RetentionProducerAuthorization(catalog, work, boards, organizations, actors);
        var service = new RetentionSourceReviewService(catalog, catalog, authorization, organizations, actors, unit);
        var board = Guid.NewGuid(); var list = Guid.NewGuid(); var card = Guid.NewGuid();
        var comment = Guid.NewGuid(); var attachment = Guid.NewGuid();
        DateTimeOffset Now() => AttachmentMetadataMapping.DatabaseTimestamp(app.Services.GetRequiredService<IClock>().UtcNow);
        await ConfigurationOwned(unit, parent.Id, actor, null, false, async () =>
        {
            await work.CreateBoardAsync(parent.Id, actor, board, "Private review", null, BoardVisibility.Private, "COLOR", null, Now(), ct);
            await work.CreateListAsync(board, list, "Review list", null, Now(), ct);
            await work.CreateCardAsync(list, card, "Private title", "Private body", null, Now(), ct);
            await ((ICardCommentStore)work).CreateAsync(comment, parent.Id, card, actor, "Private comment", Now(), ct);
            await ((IAttachmentMetadataStore)work).CreateUrlAttachmentAsync(attachment, parent.Id, card, actor, "Private file", "https://example.test/private", Now(), ct);
            return OrganizationOperation<bool>.Success(true);
        }, ct);
        RetentionProducerReference? cardReview = null;
        foreach (var (id, kind) in new[] { (card, RetentionProducerKind.Card), (comment, RetentionProducerKind.CardComment), (attachment, RetentionProducerKind.Attachment) })
        {
            var result = await service.ReviewAsync(parent.Id, actor, kind, id, ct);
            Assert.True(result.Succeeded); Assert.NotNull(result.Value);
            Assert.Equal(id, result.Value.RecordId); Assert.Equal(kind, result.Value.Kind);
            Assert.Equal(parent.Id, result.Value.OrganizationId); Assert.Equal(board, result.Value.BoardId);
            Assert.Equal(list, result.Value.ListId); Assert.Equal(card, result.Value.CardId);
            if (kind == RetentionProducerKind.Card) cardReview = result.Value;
        }
        Assert.False((await service.ReviewAsync(parent.Id, Guid.NewGuid(), RetentionProducerKind.Card, card, ct)).Succeeded);
        Assert.False((await service.ReviewAsync(parent.Id, actor, RetentionProducerKind.Attachment, card, ct)).Succeeded);
        Assert.False((await service.ReviewAsync(parent.Id, actor, RetentionProducerKind.AuditEvent, card, ct)).Succeeded);
        Assert.False((await service.ReviewAsync(parent.Id, actor, RetentionProducerKind.Card, Guid.NewGuid(), ct)).Succeeded);
        var changed = new LateClassificationReviewProducer(catalog);
        var racing = new RetentionSourceReviewService(changed, catalog,
            new RetentionProducerAuthorization(changed, work, boards, organizations, actors), organizations, actors, unit);
        var denied = await racing.ReviewAsync(parent.Id, actor, RetentionProducerKind.Card, card, ct);
        Assert.False(denied.Succeeded); Assert.Null(denied.Value);
        Assert.DoesNotContain(((InMemoryWorkManagementStore)work).AuditSnapshot(parent.Id), a => a.EventType == "RECORD_RETENTION_SOURCE_REGISTERED");
        await ConfigurationOwned(unit, parent.Id, actor, null, false, async () =>
        {
            var member = (await organizations.FindMembershipAsync(parent.Id, actor, ct))!;
            Assert.NotNull(await catalog.CaptureAsync(new(parent.Id, RetentionRecordClass.OperationalWork, card,
                RetentionProducerKind.Card, "explicit.review", null, 1, 1, actor, member.Id, member.Version,
                member.Role, Guid.NewGuid(), "review-capture", Now(), cardReview!), ct));
            return OrganizationOperation<bool>.Success(true);
        }, ct);
        var classified = await service.ReviewAsync(parent.Id, actor, RetentionProducerKind.Card, card, ct);
        Assert.False(classified.Succeeded); Assert.Null(classified.Value);
        Assert.Equal("retention_source_unavailable", classified.ErrorCode);
        Assert.Single(((InMemoryWorkManagementStore)work).AuditSnapshot(parent.Id), a => a.EventType == "RECORD_RETENTION_SOURCE_REGISTERED");
    }

    // Explicit late-source race control after the real first Board admission.
    private sealed class LateClassificationReviewProducer(IRetentionProducerReferences inner) : IRetentionProducerReferences
    {
        private int reads;
        public async Task<RetentionProducerReference?> ReadAsync(Guid organization, RetentionProducerKind kind, Guid record, CancellationToken ct)
        {
            var value = await inner.ReadAsync(organization, kind, record, ct);
            return ++reads >= 4 && value is not null ? value with { ListVersion = value.ListVersion + 1 } : value;
        }
    }
}
