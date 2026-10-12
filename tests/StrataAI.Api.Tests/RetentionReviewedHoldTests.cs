using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Common;
using StrataAI.Application.Organizations;
using StrataAI.Application.Records;
using StrataAI.Application.WorkManagement;
using StrataAI.Domain.Records;
using StrataAI.Infrastructure.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Theory]
    [InlineData(RetentionProducerKind.Card)]
    [InlineData(RetentionProducerKind.CardComment)]
    [InlineData(RetentionProducerKind.Attachment)]
    public async Task PRD_70_reviewed_hold_requires_current_source_before_apply_and_recovers_original_review_after_a_move(RetentionProducerKind kind)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient();
        var f = await WorkDispositionFixture(app, owner, ct);
        var record = kind == RetentionProducerKind.Card ? f.Card : kind == RetentionProducerKind.CardComment ? f.Comment : f.Attachment;
        var segment = kind == RetentionProducerKind.Card ? "card" : kind == RetentionProducerKind.CardComment ? "cardComment" : "attachment";
        var root = $"/organizations/{f.Organization}/retention-sources/{segment}/{record}";
        using var reviewed = await owner.GetAsync(root + "/review", ct); Assert.Equal(HttpStatusCode.OK, reviewed.StatusCode);
        var evidence = await reviewed.Content.ReadFromJsonAsync<JsonElement>(ct);
        using var classified = await Mutate(owner, HttpMethod.Post, root + "/classification",
            new { review = evidence, category = "human.hold", jurisdiction = (string?)null, confirmed = true }, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.OK, classified.StatusCode);
        var review = await ReviewedHoldSource(app.Services, f.Organization, f.Actor, kind, record, ct);
        var scope = new RetentionHoldScope(f.Organization,
            kind == RetentionProducerKind.Attachment ? RetentionRecordClass.Document : RetentionRecordClass.OperationalWork, "human.hold", record);
        var service = ActivatorUtilities.CreateInstance<RetentionHoldService>(app.Services);
        var reviewer = app.Services.GetRequiredService<RetentionProducerAuthorization>();
        var hold = Guid.NewGuid(); var key = Guid.NewGuid();
        var work = app.Services.GetRequiredService<InMemoryWorkManagementStore>(); var before = work.AuditSnapshot(f.Organization).ToArray();
        var stale = await service.ApplyReviewedAsync(f.Organization, f.Actor, hold, scope, "Private reviewed hold reason",
            review with { SourceVersion = review.SourceVersion + 1 }, reviewer, key, "stale-held-review", ct);
        Assert.Equal("retention_hold_review_changed", stale.ErrorCode); Assert.True(before.SequenceEqual(work.AuditSnapshot(f.Organization)));
        Assert.False((await service.ReadAsync(f.Organization, f.Actor, hold, ct)).Succeeded);
        var applied = await service.ApplyReviewedAsync(f.Organization, f.Actor, hold, scope, "Private reviewed hold reason",
            review, reviewer, key, "current-held-review", ct);
        Assert.True(applied.Succeeded);
        var mismatch = await service.ApplyReviewedAsync(f.Organization, f.Actor, hold, scope, "Private reviewed hold reason",
            review with { CardVersion = review.CardVersion + 1 }, reviewer, key, "altered-original-review", ct);
        Assert.Equal("idempotency_key_conflict", mismatch.ErrorCode);
        var destination = Guid.NewGuid(); var destinationList = Guid.NewGuid(); var unit = app.Services.GetRequiredService<IOrganizationUnitOfWork>();
        var store = app.Services.GetRequiredService<IWorkManagementStore>(); var clock = app.Services.GetRequiredService<IClock>();
        await ConfigurationOwned(unit, f.Organization, f.Actor, null, false, async () =>
        {
            await store.CreateBoardAsync(f.Organization, f.Actor, destination, "Reviewed hold destination", null,
                BoardVisibility.Private, "COLOR", null, clock.UtcNow, ct);
            await store.CreateListAsync(destination, destinationList, "Moved retained sources", null, clock.UtcNow, ct);
            Assert.NotNull(await store.MoveCardAsync(f.Card, destinationList, null, review.CardVersion, clock.UtcNow, ct));
            return OrganizationOperation<bool>.Success(true);
        }, ct);
        var afterMove = work.AuditSnapshot(f.Organization).ToArray();
        var replay = await service.ApplyReviewedAsync(f.Organization, f.Actor, hold, scope, "Private reviewed hold reason",
            review, reviewer, key, "original-reviewed-recovery", ct);
        Assert.Equal(applied.Value, replay.Value); Assert.True(afterMove.SequenceEqual(work.AuditSnapshot(f.Organization)));
        var fresh = await service.ApplyReviewedAsync(f.Organization, f.Actor, Guid.NewGuid(), scope, "Private reviewed hold reason",
            review, reviewer, Guid.NewGuid(), "new-held-stale-review", ct);
        Assert.Equal("retention_hold_review_changed", fresh.ErrorCode);
    }

    [Fact]
    public async Task PRD_70_reviewed_broad_scope_rejects_record_evidence_and_uses_real_administration_without_a_producer_snapshot()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient();
        var f = await WorkDispositionFixture(app, owner, ct);
        var review = await ReviewedHoldSource(app.Services, f.Organization, f.Actor, RetentionProducerKind.Card, f.Card, ct);
        var service = ActivatorUtilities.CreateInstance<RetentionHoldService>(app.Services);
        var reviewer = app.Services.GetRequiredService<RetentionProducerAuthorization>();
        var scope = new RetentionHoldScope(f.Organization, null, null, null); var id = Guid.NewGuid(); var key = Guid.NewGuid();
        Assert.Equal("invalid_retention_hold_review", (await service.ApplyReviewedAsync(f.Organization, f.Actor, id,
            scope, "Broad human hold", review, reviewer, key, "invalid-broad-review", ct)).ErrorCode);
        var applied = await service.ApplyReviewedAsync(f.Organization, f.Actor, id, scope, "Broad human hold", null, reviewer, key, "broad-human-review", ct);
        Assert.True(applied.Succeeded);
        Assert.Equal(applied.Value, (await service.ApplyReviewedAsync(f.Organization, f.Actor, id, scope,
            "Broad human hold", null, reviewer, key, "broad-original-review", ct)).Value);
    }

    private static async Task<RetentionProducerReference> ReviewedHoldSource(IServiceProvider services, Guid organization,
        Guid actor, RetentionProducerKind kind, Guid record, CancellationToken ct)
    {
        var result = await ConfigurationOwned(services.GetRequiredService<IOrganizationUnitOfWork>(), organization, actor, null, false, async () =>
        {
            var evidence = await services.GetRequiredService<IRetentionProducerReferences>().ReadAsync(organization, kind, record, ct);
            Assert.NotNull(evidence);
            Assert.True(await services.GetRequiredService<RetentionProducerAuthorization>().VerifyAsync(evidence, actor, ct));
            return OrganizationOperation<RetentionProducerReference>.Success(evidence);
        }, ct);
        return result.Value!;
    }
}
