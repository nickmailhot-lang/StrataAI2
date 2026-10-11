using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.Records;
using StrataAI.Application.WorkManagement;
using StrataAI.Domain.Records;
using StrataAI.Infrastructure.Organizations;
using StrataAI.Infrastructure.Records;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    private sealed class ReferenceWithdrawal(IRetentionHoldScopeAuthorization inner) : IRetentionHoldScopeAuthorization
    {
        public bool Denied { get; set; }
        public Task<bool> VerifyAsync(RetentionHoldScope scope, Guid actor, CancellationToken ct) => inner.VerifyAsync(scope, actor, ct);
        public Task<bool> VerifyAsync(RetentionHoldReference reference, Guid actor, CancellationToken ct) => Denied ? Task.FromResult(false) : inner.VerifyAsync(reference, actor, ct);
    }

    [Fact]
    public async Task PRD_70_Demo_real_admission_retains_application_context_across_moves_archive_release_retry_and_rollback()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(configureServices: services =>
        {
            // Actual Demo producers, Board admission, source and hold storage.
            // These registrations are fixture-local, not deployed activation.
            services.AddSingleton<InMemoryRetentionSourceStore>();
            services.AddSingleton<IRetentionSourceReferenceReader>(p => p.GetRequiredService<InMemoryRetentionSourceStore>());
            services.AddSingleton<IRetentionWorkSourceReferences>(p => p.GetRequiredService<InMemoryRetentionSourceStore>());
            services.AddSingleton<IRetentionHoldScopeAuthorization, RetentionWorkAuthorization>();
            services.AddSingleton<InMemoryRetentionHoldStore>();
            services.AddSingleton<IRetentionHoldStore>(p => p.GetRequiredService<InMemoryRetentionHoldStore>());
            services.AddSingleton<IDemoOrganizationTransactionParticipant>(p => p.GetRequiredService<InMemoryRetentionSourceStore>());
            services.AddSingleton<IDemoOrganizationTransactionParticipant>(p => p.GetRequiredService<InMemoryRetentionHoldStore>());
        });
        using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var (actor, parent) = await ConfigurationParent(owner, "Retained Demo application", ct);
        var work = app.Services.GetRequiredService<IWorkManagementStore>(); var sources = app.Services.GetRequiredService<InMemoryRetentionSourceStore>();
        var store = app.Services.GetRequiredService<InMemoryRetentionHoldStore>(); var unit = app.Services.GetRequiredService<IOrganizationUnitOfWork>();
        var organizations = app.Services.GetRequiredService<IOrganizationStore>();
        var service = ActivatorUtilities.CreateInstance<RetentionHoldService>(app.Services);
        var boards = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() }; var lists = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var card = Guid.NewGuid(); var comment = Guid.NewGuid(); var file = Guid.NewGuid();
        var records = new[] { (card, RetentionProducerKind.Card, RetentionRecordClass.OperationalWork),
            (comment, RetentionProducerKind.CardComment, RetentionRecordClass.OperationalWork), (file, RetentionProducerKind.Attachment, RetentionRecordClass.Document) };
        DateTimeOffset Now() => AttachmentMetadataMapping.DatabaseTimestamp(DateTimeOffset.UtcNow);
        await ConfigurationOwned(unit, parent.Id, actor, null, false, async () =>
        {
            for (var index = 0; index < 3; index++)
            {
                await work.CreateBoardAsync(parent.Id, actor, boards[index], "Retained Board", null, BoardVisibility.Private, "COLOR", null, Now(), ct);
                await work.CreateListAsync(boards[index], lists[index], "Retained List", null, Now(), ct);
            }
            await work.CreateCardAsync(lists[0], card, "Private source", null, null, Now(), ct);
            await ((ICardCommentStore)work).CreateAsync(comment, parent.Id, card, actor, "Private comment", Now(), ct);
            await ((IAttachmentMetadataStore)work).CreateUrlAttachmentAsync(file, parent.Id, card, actor, "Private file", "https://example.test/private", Now(), ct);
            var member = (await organizations.FindMembershipAsync(parent.Id, actor, ct))!;
            foreach (var record in records)
                Assert.NotNull(await sources.CaptureAsync(new(parent.Id, record.Item3, record.Item1, record.Item2, "explicit.classification", null,
                    1, 1, actor, member.Id, member.Version, member.Role, Guid.NewGuid(), "classify", Now(),
                    (await sources.ReadAsync(parent.Id, record.Item2, record.Item1, ct))!), ct));
            Assert.NotNull(await work.MoveCardAsync(card, lists[1], null, 1, Now(), ct));
            return OrganizationOperation<bool>.Success(true);
        }, ct);
        var ids = records.Select(_ => Guid.NewGuid()).ToArray(); var keys = records.Select(_ => Guid.NewGuid()).ToArray();
        var applied = new List<RetentionHold>();
        for (var index = 0; index < records.Length; index++)
        {
            var record = records[index];
            var result = await service.ApplyAsync(parent.Id, actor, ids[index], new(parent.Id, record.Item3, null, record.Item1), "Private reason", keys[index], "apply", ct);
            Assert.True(result.Succeeded); applied.Add(result.Value!);
        }
        Assert.Equal("retention_hold_source_unavailable", (await service.ApplyAsync(parent.Id, actor, Guid.NewGuid(),
            new(parent.Id, RetentionRecordClass.OperationalWork, "wrong.classification", card), "Private reason", Guid.NewGuid(), "wrong-category", ct)).ErrorCode);
        await ConfigurationOwned(unit, parent.Id, actor, null, false, async () =>
        {
            Assert.NotNull(await work.MoveCardAsync(card, lists[2], null, 2, Now(), ct));
            Assert.NotNull(await work.SetCardLifecycleAsync(card, WorkItemLifecycleState.Active, WorkItemLifecycleState.Archived, 3, Now(), ct, actor));
            foreach (var board in boards.Take(2))
                Assert.NotNull(await work.SetBoardLifecycleAsync(board, BoardLifecycleState.Active, BoardLifecycleState.Archived, 1, Now(), ct, actor));
            return OrganizationOperation<bool>.Success(true);
        }, ct);
        for (var index = 0; index < records.Length; index++)
        {
            var record = records[index];
            var released = await service.ReleaseAsync(parent.Id, actor, ids[index], 1, Guid.NewGuid(), "release", ct);
            Assert.True(released.Succeeded);
            Assert.Equal(applied[index], (await service.ApplyAsync(parent.Id, actor, ids[index], new(parent.Id, record.Item3, null, record.Item1),
                "Private reason", keys[index], "original-recovery", ct)).Value);
            Assert.Equal(released.Value, (await service.ReadAsync(parent.Id, actor, ids[index], ct)).Value);
            var capturedIndex = index;
            await ConfigurationOwned(unit, parent.Id, actor, null, false, async () =>
            {
                var current = await store.ReadReferenceAsync(parent.Id, ids[capturedIndex], ct);
                var original = await store.ReadReceiptReferenceAsync(parent.Id, actor, keys[capturedIndex], ct);
                Assert.NotNull(current); Assert.Equal(2, current.Version); Assert.NotNull(original); Assert.Equal(1, original.Version);
                Assert.All(store.ReadHistory(parent.Id, ids[capturedIndex], ct), revision =>
                {
                    Assert.Equal((short?)1, revision.Reference.ContextVersion); Assert.Equal(boards[0], revision.Reference.ClassificationBoardId);
                    Assert.Equal(boards[1], revision.Reference.ApplicationBoardId);
                    Assert.Equal(record.Item2 == RetentionProducerKind.Card ? 2 : 1, revision.Reference.ApplicationSourceVersion);
                });
                Assert.Equal(original with { Version = 2 }, current);
                return OrganizationOperation<bool>.Success(true);
            }, ct);
        }
        var withdrawal = new ReferenceWithdrawal(app.Services.GetRequiredService<IRetentionHoldScopeAuthorization>());
        var rollbackId = Guid.NewGuid(); var rollbackKey = Guid.NewGuid();
        var failedService = ActivatorUtilities.CreateInstance<RetentionHoldService>(app.Services,
            new HoldAfterWrite(store, () => withdrawal.Denied = true), withdrawal);
        Assert.Equal("retention_hold_not_found", (await failedService.ApplyAsync(parent.Id, actor, rollbackId,
            new(parent.Id, RetentionRecordClass.OperationalWork, null, card), "Private rollback", rollbackKey, "rollback", ct)).ErrorCode);
        withdrawal.Denied = false;
        await ConfigurationOwned(unit, parent.Id, actor, null, false, async () =>
        {
            Assert.Null(await store.ReadReferenceAsync(parent.Id, rollbackId, ct));
            Assert.Null(await store.ReadReceiptReferenceAsync(parent.Id, actor, rollbackKey, ct)); Assert.Empty(store.ReadHistory(parent.Id, rollbackId, ct));
            return OrganizationOperation<bool>.Success(true);
        }, ct);
        Assert.True((await service.ApplyAsync(parent.Id, actor, rollbackId, new(parent.Id, RetentionRecordClass.OperationalWork, null, card),
            "Private rollback", rollbackKey, "reclaimed", ct)).Succeeded);
    }
}
