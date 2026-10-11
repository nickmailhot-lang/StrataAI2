using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Common;
using StrataAI.Application.Organizations;
using StrataAI.Application.Records;
using StrataAI.Application.WorkManagement;
using StrataAI.Domain.Records;
using StrataAI.Infrastructure.Records;
using StrataAI.Infrastructure.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    private sealed record DispositionFixture(Guid Actor, Guid Organization, Guid Board, Guid List, Guid Card, Guid Comment, Guid Attachment);
    private static async Task<DispositionFixture> WorkDispositionFixture(ApiFactory app, HttpClient owner, CancellationToken ct)
    {
        await RegisterAndLogin(owner);
        var (actor, parent) = await ConfigurationParent(owner, "Held work", ct);
        var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var board = Guid.NewGuid(); var list = Guid.NewGuid(); var card = Guid.NewGuid(); var comment = Guid.NewGuid(); var attachment = Guid.NewGuid();
        var at = app.Services.GetRequiredService<IClock>().UtcNow; at = at.AddTicks(-(at.Ticks % 10));
        await ConfigurationOwned(app.Services.GetRequiredService<IOrganizationUnitOfWork>(), parent.Id, actor, null, false, async () =>
        {
            await work.CreateBoardAsync(parent.Id, actor, board, "Private held Board", null, BoardVisibility.Private, "COLOR", null, at, ct);
            await work.CreateListAsync(board, list, "Private held List", null, at, ct);
            await work.CreateCardAsync(list, card, "Private held Card", null, null, at, ct);
            await ((ICardCommentStore)work).CreateAsync(comment, parent.Id, card, actor, "Private retained comment", at, ct);
            await ((IAttachmentMetadataStore)work).CreateUrlAttachmentAsync(attachment, parent.Id, card, actor, "Private retained file", "https://example.test/private-held", at, ct);
            return OrganizationOperation<bool>.Success(true);
        }, ct);
        return new(actor, parent.Id, board, list, card, comment, attachment);
    }
    private static async Task ClassifyDispositionSource(HttpClient owner, DispositionFixture f, bool comment, CancellationToken ct)
    {
        var path = $"/organizations/{f.Organization}/retention-sources/{(comment ? "cardComment" : "attachment")}/{(comment ? f.Comment : f.Attachment)}";
        using var review = await owner.GetAsync(path + "/review", ct); Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        var evidence = await review.Content.ReadFromJsonAsync<JsonElement>(ct);
        using var classified = await Mutate(owner, HttpMethod.Post, path + "/classification",
            new { review = evidence, category = "reviewed.held", jurisdiction = (string?)null, confirmed = true }, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.OK, classified.StatusCode);
    }
    [Theory]
    [InlineData(RetentionWorkDispositionKind.Board)]
    [InlineData(RetentionWorkDispositionKind.List)]
    [InlineData(RetentionWorkDispositionKind.Card)]
    [InlineData(RetentionWorkDispositionKind.CardComment)]
    [InlineData(RetentionWorkDispositionKind.Attachment)]
    public async Task PRD_70_real_HTTP_deletion_respects_held_source_or_descendant_and_release_reclaims_failed_key(RetentionWorkDispositionKind kind)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var foreign = app.CreateClient();
        var f = await WorkDispositionFixture(app, owner, ct); await RegisterAndLogin(foreign);
        var comment = kind == RetentionWorkDispositionKind.CardComment;
        await ClassifyDispositionSource(owner, f, comment, ct);
        var hold = Guid.NewGuid(); var service = ActivatorUtilities.CreateInstance<RetentionHoldService>(app.Services);
        // Actual typed source/Board authority and actual Demo persistence; no
        // permissive hold-scope fixture for these record-specific tests.
        Assert.True((await service.ApplyAsync(f.Organization, f.Actor, hold,
            new(f.Organization, comment ? RetentionRecordClass.OperationalWork : RetentionRecordClass.Document, null, comment ? f.Comment : f.Attachment),
            "Private legal reason", Guid.NewGuid(), "apply", ct)).Succeeded);
        var archivePath = kind switch
        {
            RetentionWorkDispositionKind.Board => $"/boards/{f.Board}/archive",
            RetentionWorkDispositionKind.List => $"/lists/{f.List}/archive",
            RetentionWorkDispositionKind.Card => $"/cards/{f.Card}/archive",
            RetentionWorkDispositionKind.Attachment => $"/cards/{f.Card}/attachments/{f.Attachment}/archive",
            _ => null
        };
        if (archivePath is not null)
        {
            object input = kind == RetentionWorkDispositionKind.Attachment ? new AttachmentLifecycleInput(1, 1) : new { version = 1 };
            using var archived = await Mutate(owner, HttpMethod.Post, archivePath, input);
            Assert.Equal(HttpStatusCode.OK, archived.StatusCode); // Archive is not disposition.
        }
        var deletePath = kind switch
        {
            RetentionWorkDispositionKind.Board => $"/boards/{f.Board}?version=2&confirmed=true",
            RetentionWorkDispositionKind.List => $"/lists/{f.List}?version=2&confirmed=true&containedCardCount=1",
            RetentionWorkDispositionKind.Card => $"/cards/{f.Card}?version=2&confirmed=true",
            RetentionWorkDispositionKind.Attachment => $"/cards/{f.Card}/attachments/{f.Attachment}?cardVersion=2&version=2&confirmed=true",
            _ => $"/cards/{f.Card}/comments/{f.Comment}"
        };
        object deletionInput = comment ? new DeleteCardCommentInput(1, 1, true) : new { };
        var work = app.Services.GetRequiredService<IWorkManagementStore>(); var metadata = app.Services.GetRequiredService<IAttachmentMetadataStore>();
        async Task<string> Snapshot() => (await ConfigurationOwned(app.Services.GetRequiredService<IOrganizationUnitOfWork>(),
            f.Organization, f.Actor, null, false, async () => OrganizationOperation<string>.Success(JsonSerializer.Serialize(new
        {
            board = await work.FindBoardAsync(f.Board, ct), list = await work.FindListAsync(f.List, ct), card = await work.FindCardAsync(f.Card, ct),
            file = await metadata.FindLifecycleAttachmentAsync(f.Organization, f.Card, f.Attachment, ct),
            comment = await ((ICardCommentStore)work).FindAsync(f.Organization, f.Card, f.Comment, ct),
            audits = ((InMemoryWorkManagementStore)work).AuditSnapshot(f.Organization)
        })), ct)).Value!;
        var before = await Snapshot(); var key = Guid.NewGuid().ToString();
        using var hidden = await Mutate(foreign, HttpMethod.Delete, deletePath, deletionInput, key);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.DoesNotContain("hold", await hidden.Content.ReadAsStringAsync(ct), StringComparison.OrdinalIgnoreCase);
        using var refused = await Mutate(owner, HttpMethod.Delete, deletePath, deletionInput, key);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var body = await refused.Content.ReadAsStringAsync(ct); Assert.Contains("retention_hold_prevents_disposition", body);
        Assert.DoesNotContain("Private legal reason", body); Assert.DoesNotContain(hold.ToString(), body);
        Assert.Equal(before, await Snapshot());
        Assert.True((await service.ReleaseAsync(f.Organization, f.Actor, hold, 1, Guid.NewGuid(), "release", ct)).Succeeded);
        using var deleted = await Mutate(owner, HttpMethod.Delete, deletePath, deletionInput, key);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode); var original = await deleted.Content.ReadAsStringAsync(ct);
        var completed = await Snapshot();
        using var replay = await Mutate(owner, HttpMethod.Delete, deletePath, deletionInput, key);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode); Assert.Equal(original, await replay.Content.ReadAsStringAsync(ct));
        Assert.Equal(completed, await Snapshot());
    }

    [Fact]
    public async Task PRD_70_complete_Demo_disposition_scope_blocks_unknown_category_preserves_known_exclusion_and_denies_cross_tenant()
    {
        var ct = TestContext.Current.CancellationToken; var scopes = new HoldScopeProbe();
        await using var app = HoldDemoFactory(scopes); using var owner = app.CreateClient();
        var f = await WorkDispositionFixture(app, owner, ct);
        var service = ActivatorUtilities.CreateInstance<RetentionHoldService>(app.Services);
        var probe = app.Services.GetRequiredService<IRetentionWorkDisposition>();
        var hold = Guid.NewGuid();
        // Explicit broad-scope authorization fixture; deployed broad apply/release
        // remains unexposed. The persisted hold and disposition provider are real.
        Assert.True((await service.ApplyAsync(f.Organization, f.Actor, hold,
            new(f.Organization, RetentionRecordClass.Document, "OTHER.CATEGORY", null), "Private broad reason", Guid.NewGuid(), "apply", ct)).Succeeded);
        async Task<RetentionDispositionAdmission> Check(RetentionWorkDispositionKind kind, Guid id) =>
            (await ConfigurationOwned(app.Services.GetRequiredService<IOrganizationUnitOfWork>(), f.Organization, f.Actor, null, false,
                async () => OrganizationOperation<RetentionDispositionAdmission>.Success(await probe.CheckAsync(f.Organization, kind, id, ct)), ct)).Value;
        Assert.Equal(RetentionDispositionAdmission.Held, await Check(RetentionWorkDispositionKind.Card, f.Card));
        Assert.Equal(RetentionDispositionAdmission.Held, await Check(RetentionWorkDispositionKind.List, f.List));
        Assert.Equal(RetentionDispositionAdmission.Held, await Check(RetentionWorkDispositionKind.Board, f.Board));
        await ClassifyDispositionSource(owner, f, false, ct);
        Assert.Equal(RetentionDispositionAdmission.Clear, await Check(RetentionWorkDispositionKind.Card, f.Card));
        Assert.Equal(RetentionDispositionAdmission.Clear, await Check(RetentionWorkDispositionKind.Attachment, f.Attachment));
        Assert.Equal(RetentionDispositionAdmission.Unavailable, await Check(RetentionWorkDispositionKind.Attachment, Guid.NewGuid()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => probe.CheckAsync(f.Organization, RetentionWorkDispositionKind.Card, f.Card, ct));
        var other = (await ConfigurationParent(owner, "Foreign disposition", ct)).Parent;
        var foreign = await ConfigurationOwned(app.Services.GetRequiredService<IOrganizationUnitOfWork>(), other.Id, f.Actor, null, false,
            async () => OrganizationOperation<RetentionDispositionAdmission>.Success(await probe.CheckAsync(other.Id, RetentionWorkDispositionKind.Card, f.Card, ct)), ct);
        Assert.Equal(RetentionDispositionAdmission.Unavailable, foreign.Value);
        Assert.True((await service.ReleaseAsync(f.Organization, f.Actor, hold, 1, Guid.NewGuid(), "release", ct)).Succeeded);
        Assert.Equal(RetentionDispositionAdmission.Clear, await Check(RetentionWorkDispositionKind.Card, f.Card));
    }
    [Fact]
    public async Task PRD_70_completed_deletion_recovery_acknowledges_original_after_later_hold_without_new_disposition()
    {
        var ct = TestContext.Current.CancellationToken; var scopes = new HoldScopeProbe();
        await using var app = HoldDemoFactory(scopes); using var owner = app.CreateClient();
        var f = await WorkDispositionFixture(app, owner, ct);
        var path = $"/cards/{f.Card}/comments/{f.Comment}"; var input = new DeleteCardCommentInput(1, 1, true); var key = Guid.NewGuid().ToString();
        using var deleted = await Mutate(owner, HttpMethod.Delete, path, input, key);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode); var original = await deleted.Content.ReadAsStringAsync(ct);
        var service = ActivatorUtilities.CreateInstance<RetentionHoldService>(app.Services);
        // Broad administration is an explicit fixture, not an activated HTTP grant.
        Assert.True((await service.ApplyAsync(f.Organization, f.Actor, Guid.NewGuid(),
            new(f.Organization, null, null, null), "Later private legal reason", Guid.NewGuid(), "later-hold", ct)).Succeeded);
        var work = (InMemoryWorkManagementStore)app.Services.GetRequiredService<IWorkManagementStore>();
        var before = JsonSerializer.Serialize(work.AuditSnapshot(f.Organization));
        var card = await work.FindCardAsync(f.Card, ct);
        using var replay = await Mutate(owner, HttpMethod.Delete, path, input, key);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode); Assert.Equal(original, await replay.Content.ReadAsStringAsync(ct));
        Assert.Equal(card, await work.FindCardAsync(f.Card, ct));
        Assert.Equal(before, JsonSerializer.Serialize(work.AuditSnapshot(f.Organization)));
        // A new deletion of a still-live child must respect that same current hold.
        using var archive = await Mutate(owner, HttpMethod.Post, $"/cards/{f.Card}/attachments/{f.Attachment}/archive", new AttachmentLifecycleInput(2, 1));
        Assert.Equal(HttpStatusCode.OK, archive.StatusCode);
        using var blocked = await Mutate(owner, HttpMethod.Delete, $"/cards/{f.Card}/attachments/{f.Attachment}?cardVersion=3&version=2&confirmed=true", new { });
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Contains("retention_hold_prevents_disposition", await blocked.Content.ReadAsStringAsync(ct));
    }

}
