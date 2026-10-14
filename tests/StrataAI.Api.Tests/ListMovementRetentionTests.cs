using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
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
    [InlineData(false)]
    [InlineData(true)]
    public async Task PRD_07_whole_List_HTTP_movement_preserves_record_hold_and_destination_disposition_after_source_archive(bool comment)
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var outsider = app.CreateClient();
        var f = await WorkDispositionFixture(app, owner, ct); await RegisterAndLogin(outsider);
        await ClassifyDispositionSource(owner, f, comment, ct);
        var service = ActivatorUtilities.CreateInstance<RetentionHoldService>(app.Services);
        var hold = Guid.NewGuid(); var applyKey = Guid.NewGuid();
        var scope = new RetentionHoldScope(f.Organization, comment ? RetentionRecordClass.OperationalWork : RetentionRecordClass.Document,
            null, comment ? f.Comment : f.Attachment);
        var applied = await service.ApplyAsync(f.Organization, f.Actor, hold, scope, "Private preserved hold reason", applyKey, "apply-before-list-move", ct);
        Assert.True(applied.Succeeded);
        var destination = await ListMoveDestination(owner, f.Organization, ct);
        var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var unit = app.Services.GetRequiredService<IOrganizationUnitOfWork>();
        var probe = app.Services.GetRequiredService<IRetentionWorkDisposition>();
        async Task<RetentionDispositionAdmission> Check(RetentionWorkDispositionKind kind, Guid id) =>
            (await ConfigurationOwned(unit, f.Organization, f.Actor, null, false,
                async () => OrganizationOperation<RetentionDispositionAdmission>.Success(await probe.CheckAsync(f.Organization, kind, id, ct)), ct)).Value;
        async Task<string> Snapshot() => (await ConfigurationOwned(unit, f.Organization, f.Actor, null, false,
            async () => OrganizationOperation<string>.Success(JsonSerializer.Serialize(new
            {
                list = await work.FindListAsync(f.List, ct), card = await work.FindCardAsync(f.Card, ct),
                comment = await ((ICardCommentStore)work).FindAsync(f.Organization, f.Card, f.Comment, ct),
                attachment = await ((IAttachmentMetadataStore)work).FindLifecycleAttachmentAsync(f.Organization, f.Card, f.Attachment, ct),
                audits = ((InMemoryWorkManagementStore)work).AuditSnapshot(f.Organization)
            })), ct)).Value!;
        var movementBody = new { sourceBoardId = f.Board, destinationBoardId = destination, expectedVersion = 1 };
        var movementKey = Guid.NewGuid().ToString();
        using var moved = await Mutate(owner, HttpMethod.Post, $"/lists/{f.List}/move", movementBody, movementKey);
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        Assert.Equal(destination, (await work.FindCardAsync(f.Card, ct))!.BoardId);
        Assert.Equal(applied.Value, (await service.ReadAsync(f.Organization, f.Actor, hold, ct)).Value);
        Assert.Equal(RetentionDispositionAdmission.Held, await Check(RetentionWorkDispositionKind.Card, f.Card));
        Assert.Equal(RetentionDispositionAdmission.Held, await Check(RetentionWorkDispositionKind.List, f.List));
        Assert.Equal(RetentionDispositionAdmission.Held, await Check(RetentionWorkDispositionKind.Board, destination));
        Assert.Equal(RetentionDispositionAdmission.Clear, await Check(RetentionWorkDispositionKind.Board, f.Board));
        using var archivedSource = await Mutate(owner, HttpMethod.Post, $"/boards/{f.Board}/archive", new { version = 1 });
        Assert.Equal(HttpStatusCode.OK, archivedSource.StatusCode);
        Assert.Equal(RetentionDispositionAdmission.Held, await Check(RetentionWorkDispositionKind.List, f.List));
        using var archive = await Mutate(owner, HttpMethod.Post, $"/lists/{f.List}/archive", new { version = 2 });
        Assert.Equal(HttpStatusCode.OK, archive.StatusCode);
        var before = await Snapshot(); var deletionKey = Guid.NewGuid().ToString();
        var deletionPath = $"/lists/{f.List}?version=3&confirmed=true&containedCardCount=1";
        using var hidden = await Mutate(outsider, HttpMethod.Delete, deletionPath, new { }, deletionKey);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.DoesNotContain("hold", await hidden.Content.ReadAsStringAsync(ct), StringComparison.OrdinalIgnoreCase);
        using var refused = await Mutate(owner, HttpMethod.Delete, deletionPath, new { }, deletionKey);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var body = await refused.Content.ReadAsStringAsync(ct);
        Assert.Contains("retention_hold_prevents_disposition", body);
        Assert.DoesNotContain("Private preserved hold reason", body); Assert.DoesNotContain(hold.ToString(), body);
        Assert.Equal(before, await Snapshot());
        Assert.True((await service.ReleaseAsync(f.Organization, f.Actor, hold, 1, Guid.NewGuid(), "release-after-list-move", ct)).Succeeded);
        // Original hold receipt recovers its historical applied state, without
        // reactivating the released hold or rewriting its original context.
        Assert.Equal(applied.Value, (await service.ApplyAsync(f.Organization, f.Actor, hold, scope,
            "Private preserved hold reason", applyKey, "recover-original-hold", ct)).Value);
        Assert.Equal(RetentionDispositionAdmission.Clear, await Check(RetentionWorkDispositionKind.List, f.List));
        using var deleted = await Mutate(owner, HttpMethod.Delete, deletionPath, new { }, deletionKey);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        var acknowledgment = await deleted.Content.ReadAsStringAsync(ct); var after = await Snapshot();
        using var replay = await Mutate(owner, HttpMethod.Delete, deletionPath, new { }, deletionKey);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode); Assert.Equal(acknowledgment, await replay.Content.ReadAsStringAsync(ct));
        Assert.Equal(after, await Snapshot());
    }
}
