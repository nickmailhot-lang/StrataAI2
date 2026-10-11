using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using StrataAI.Api.Records;
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
    public async Task PRD_70_Classification_command_recovers_original_results_and_rolls_back_receipt_source_and_audit()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(configureServices: services =>
        {
            services.AddSingleton<InMemoryRetentionSourceStore>();
            services.AddSingleton<IDemoOrganizationTransactionParticipant>(p => p.GetRequiredService<InMemoryRetentionSourceStore>());
        });
        using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (actor, parent) = await ConfigurationParent(owner, "Classification commands", ct);
        var catalog = app.Services.GetRequiredService<InMemoryRetentionSourceStore>();
        var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var organizations = app.Services.GetRequiredService<IOrganizationStore>();
        var unit = app.Services.GetRequiredService<IOrganizationUnitOfWork>();
        var actors = app.Services.GetRequiredService<ICommandActorAuthorization>();
        var boards = app.Services.GetRequiredService<IWorkBoardAuthorization>();
        var clock = new ClassificationClock(app.Services.GetRequiredService<IClock>().UtcNow);
        var producerAuth = new RetentionProducerAuthorization(catalog, work, boards, organizations, actors);
        var reviewService = new RetentionSourceReviewService(catalog, catalog, producerAuth, organizations, actors, unit);
        var classifiedAuth = new RetentionWorkAuthorization(catalog, catalog, work, boards, organizations, actors);
        var probe = new ClassificationStoreProbe(catalog);
        RetentionSourceClassificationService Service(IWorkBoardAuthorization? gate = null) => new(probe, producerAuth,
            gate is null ? classifiedAuth : new(catalog, catalog, work, gate, organizations, actors), organizations, actors, unit, clock);
        var service = Service();
        var board = Guid.NewGuid(); var destination = Guid.NewGuid(); var list = Guid.NewGuid(); var destinationList = Guid.NewGuid();
        var card = Guid.NewGuid(); var comment = Guid.NewGuid(); var attachment = Guid.NewGuid(); var rollbackCard = Guid.NewGuid();
        DateTimeOffset Now() => AttachmentMetadataMapping.DatabaseTimestamp(app.Services.GetRequiredService<IClock>().UtcNow);
        await ConfigurationOwned(unit, parent.Id, actor, null, false, async () =>
        {
            await work.CreateBoardAsync(parent.Id, actor, board, "Original", null, BoardVisibility.Private, "COLOR", null, Now(), ct);
            await work.CreateBoardAsync(parent.Id, actor, destination, "Destination", null, BoardVisibility.Private, "COLOR", null, Now(), ct);
            await work.CreateListAsync(board, list, "Original list", null, Now(), ct);
            await work.CreateListAsync(destination, destinationList, "Destination list", null, Now(), ct);
            await work.CreateCardAsync(list, card, "Private source", null, null, Now(), ct);
            await work.CreateCardAsync(list, rollbackCard, "Rollback source", null, null, Now(), ct);
            await ((ICardCommentStore)work).CreateAsync(comment, parent.Id, card, actor, "Private comment", Now(), ct);
            await ((IAttachmentMetadataStore)work).CreateUrlAttachmentAsync(attachment, parent.Id, card, actor, "Private file", "https://example.test/private", Now(), ct);
            return OrganizationOperation<bool>.Success(true);
        }, ct);
        clock.Value = Now();
        var captures = new List<(RetentionProducerReference Review, Guid Key, RetentionSourceClassification Result)>();
        foreach (var (id, kind) in new[] { (card, RetentionProducerKind.Card), (comment, RetentionProducerKind.CardComment), (attachment, RetentionProducerKind.Attachment) })
        {
            var review = (await reviewService.ReviewAsync(parent.Id, actor, kind, id, ct)).Value!; var key = Guid.NewGuid();
            var routeKind = kind == RetentionProducerKind.CardComment ? "cardComment" : kind == RetentionProducerKind.Attachment ? "attachment" : "card";
            var projectionContext = Request("{}", key);
            var projection = await RetentionSourceEndpoints.ReviewAsync(parent.Id, routeKind, id, projectionContext, reviewService, ct);
            Assert.Equal(RetentionSourceReviewRequest.From(review), Assert.IsAssignableFrom<IValueHttpResult>(projection).Value);
            Assert.Equal("private, no-store", projectionContext.Response.Headers.CacheControl);
            var json = Payload(review);
            foreach (var malformed in new[] { json.Replace("\"confirmed\":true", "\"confirmed\":false"),
                json.Replace("\"confirmed\":true", "\"confirmed\":true,\"confirmed\":true"),
                json.Replace("\"confirmed\":true", "\"confirmed\":true,\"actorId\":\"" + actor + "\""),
                json.Replace("\"kind\":\"" + routeKind + "\"", "\"kind\":0"),
                json.Replace("\"jurisdiction\":\"BC\",", ""),
                json.Replace("\"sourceVersion\":1,", ""), new string(' ', 32_769) })
            {
                var rejected = await RetentionSourceEndpoints.ClassifyAsync(parent.Id, routeKind, id, Request(malformed, key), reviewService, service, ct);
                Assert.Equal(400, Assert.IsAssignableFrom<IStatusCodeHttpResult>(rejected).StatusCode);
            }
            var wrongActor = Request(json, key); wrongActor.Request.Headers["X-StrataAI-Expected-Actor"] = Guid.NewGuid().ToString("D");
            Assert.Equal(401, Assert.IsAssignableFrom<IStatusCodeHttpResult>(await RetentionSourceEndpoints.ClassifyAsync(parent.Id, routeKind, id,
                wrongActor, reviewService, service, ct)).StatusCode);
            var denied = Request("not json", key);
            Assert.Equal(404, Assert.IsAssignableFrom<IStatusCodeHttpResult>(await RetentionSourceEndpoints.ClassifyAsync(Guid.NewGuid(), routeKind,
                id, denied, reviewService, service, ct)).StatusCode);
            Assert.Equal(0, denied.Request.Body.Position);
            var substituted = JsonNode.Parse(json)!.AsObject(); substituted["review"]!["recordId"] = Guid.NewGuid();
            Assert.Equal(400, Assert.IsAssignableFrom<IStatusCodeHttpResult>(await RetentionSourceEndpoints.ClassifyAsync(parent.Id, routeKind, id,
                Request(substituted.ToJsonString(), key), reviewService, service, ct)).StatusCode);
            Assert.False((await service.ClassifyAsync(parent.Id, actor, review with { ListVersion = 99 }, "private.explicit", "BC", key, "stale", ct)).Succeeded);
            var result = await service.ClassifyAsync(parent.Id, actor, review, "private.explicit", "BC", key, "first", ct);
            Assert.True(result.Succeeded, result.ErrorCode); Assert.NotNull(result.Value);
            Assert.Equal(board, result.Value.Reference.OriginalBoardId); Assert.Equal(1, result.Value.Reference.SourceVersion);
            Assert.Equal(result.Value, (await service.ClassifyAsync(parent.Id, actor, review, "private.explicit", "BC", key, "new-correlation", ct)).Value);
            Assert.Equal(result.Value, Assert.IsAssignableFrom<IValueHttpResult>(await RetentionSourceEndpoints.ClassifyAsync(parent.Id, routeKind, id,
                Request(json, key), reviewService, service, ct)).Value);
            Assert.Equal("idempotency_key_conflict", (await service.ClassifyAsync(parent.Id, actor, review, "other", "BC", key, "conflict", ct)).ErrorCode);
            Assert.Equal("idempotency_key_conflict", (await service.ClassifyAsync(parent.Id, actor, review with { ListVersion = 2 }, "private.explicit", "BC", key, "conflict-review", ct)).ErrorCode);
            Assert.Equal("retention_source_unavailable", (await service.ClassifyAsync(parent.Id, actor, review, "other", null, Guid.NewGuid(), "new-key", ct)).ErrorCode);
            captures.Add((review, key, result.Value));
        }
        var rollbackReview = (await reviewService.ReviewAsync(parent.Id, actor, RetentionProducerKind.Card, rollbackCard, ct)).Value!;
        var rollbackKey = Guid.NewGuid(); probe.AfterCapture = () => probe.Withdraw = true;
        Assert.False((await service.ClassifyAsync(parent.Id, actor, rollbackReview, "private.rollback", null, rollbackKey, "late-fence", ct)).Succeeded);
        probe.AfterCapture = null; probe.Withdraw = false;
        await ConfigurationOwned(unit, parent.Id, actor, null, false, async () =>
        {
            Assert.Null(await catalog.ReadSourceAsync(parent.Id, RetentionRecordClass.OperationalWork, rollbackCard, ct));
            Assert.Null(await catalog.ReadReceiptReferenceAsync(parent.Id, actor, rollbackKey, ct));
            Assert.Null(await catalog.ReadReceiptAsync(parent.Id, actor, rollbackKey, ct));
            await work.MoveCardAsync(card, destinationList, null, 1, Now(), ct);
            return OrganizationOperation<bool>.Success(true);
        }, ct);
        Assert.Equal(3, ((InMemoryWorkManagementStore)work).AuditSnapshot(parent.Id).Count(a => a.EventType == "RECORD_RETENTION_SOURCE_REGISTERED"));
        foreach (var capture in captures)
        {
            Assert.Equal(capture.Result, (await service.ClassifyAsync(parent.Id, actor, capture.Review, "private.explicit", "BC", capture.Key, "moved-retry", ct)).Value);
            var routeKind = capture.Review.Kind == RetentionProducerKind.CardComment ? "cardComment" : capture.Review.Kind == RetentionProducerKind.Attachment ? "attachment" : "card";
            Assert.Equal(capture.Result, Assert.IsAssignableFrom<IValueHttpResult>(await RetentionSourceEndpoints.ClassifyAsync(parent.Id, routeKind,
                capture.Review.RecordId, Request(Payload(capture.Review), capture.Key), reviewService, service, ct)).Value);
            foreach (var deniedBoard in new[] { board, destination })
            {
                var denied = Service(new ClassificationBoardGate(boards, deniedBoard)); var before = probe.BodyReads;
                var result = await denied.ClassifyAsync(parent.Id, actor, capture.Review, "private.explicit", "BC", capture.Key, "denied", ct);
                Assert.False(result.Succeeded); Assert.Null(result.Value); Assert.Equal(before, probe.BodyReads);
            }
        }
        clock.Value = clock.Value.AddHours(24);
        Assert.Equal("idempotency_key_expired", (await service.ClassifyAsync(parent.Id, actor, captures[0].Review, "private.explicit", "BC", captures[0].Key, "expired", ct)).ErrorCode);
        clock.Value = Now();
        Assert.True((await service.ClassifyAsync(parent.Id, actor, rollbackReview, "private.rollback", null, rollbackKey, "retry-rollback", ct)).Succeeded);
        Assert.Equal(4, ((InMemoryWorkManagementStore)work).AuditSnapshot(parent.Id).Count(a => a.EventType == "RECORD_RETENTION_SOURCE_REGISTERED"));

        DefaultHttpContext Request(string body, Guid key)
        {
            var context = new DefaultHttpContext(); context.TraceIdentifier = "classification-http-boundary";
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, actor.ToString("D"))], "test"));
            context.Request.ContentType = "application/json"; context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
            context.Request.Headers["Idempotency-Key"] = key.ToString("D"); return context;
        }
        static string Payload(RetentionProducerReference review)
        {
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            return JsonSerializer.Serialize(new ClassifyRetentionSourceRequest(RetentionSourceReviewRequest.From(review), "private.explicit", "BC", true), options);
        }
    }

    private sealed class ClassificationClock(DateTimeOffset at) : IClock
    { public DateTimeOffset Value { get; set; } = at; public DateTimeOffset UtcNow => Value; }
    private sealed class ClassificationBoardGate(IWorkBoardAuthorization inner, Guid denied) : IWorkBoardAuthorization
    {
        public Task<WorkOperation<BoardSyncScope>> GetSyncScopeAsync(Guid board, Guid? actor, CancellationToken ct = default) =>
            board == denied ? Task.FromResult(WorkOperation<BoardSyncScope>.Failure("board_not_found")) : inner.GetSyncScopeAsync(board, actor, ct);
    }
    // Explicit late receipt read withdrawal; actual storage and rollback remain real.
    private sealed class ClassificationStoreProbe(IRetentionSourceCommandStore inner) : IRetentionSourceCommandStore
    {
        public Action? AfterCapture { get; set; }
        public bool Withdraw { get; set; }
        public int BodyReads { get; private set; }
        public Task<RetentionSourceReference?> ReadSourceAsync(Guid organization, RetentionRecordClass recordClass, Guid record, CancellationToken ct) => inner.ReadSourceAsync(organization, recordClass, record, ct);
        public Task<RetentionSourceReference?> ReadReceiptReferenceAsync(Guid organization, Guid actor, Guid key, CancellationToken ct) => inner.ReadReceiptReferenceAsync(organization, actor, key, ct);
        public Task<RetentionSourceReceipt?> ReadReceiptAsync(Guid organization, Guid actor, Guid key, CancellationToken ct)
        { BodyReads++; return Withdraw ? Task.FromResult<RetentionSourceReceipt?>(null) : inner.ReadReceiptAsync(organization, actor, key, ct); }
        public async Task<RetentionSourceReference?> CaptureAsync(RetentionSourceWrite write, CancellationToken ct)
        { var result = await inner.CaptureAsync(write, ct); if (result is not null) AfterCapture?.Invoke(); return result; }
    }
}
