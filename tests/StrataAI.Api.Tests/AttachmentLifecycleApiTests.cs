using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;
using StrataAI.Domain.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task PRD_14_18_Archive_restore_delete_preserve_history_require_consent_and_replay_only_with_current_authority()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var member = app.CreateClient(); using var outsider = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct); await RegisterAndLogin(outsider);
        var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var store = app.Services.GetRequiredService<IAttachmentMetadataStore>();
        var card = await work.CreateCardAsync(f.List, Guid.NewGuid(), "Lifecycle card", "Preserved description", null, DateTimeOffset.UtcNow, ct);
        var path = $"/cards/{card.Id}/attachments";
        using var created = await Mutate(member, HttpMethod.Post, path + "/url", new CreateUrlAttachmentInput("Private URL", "https://example.test/", 1));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode); var file = (await created.Content.ReadFromJsonAsync<AttachmentChange>(ct))!.Attachment;
        var childPath = path + $"/{file.Id}";
        using var activeDelete = await Mutate(owner, HttpMethod.Delete, childPath + "?cardVersion=2&version=1&confirmed=true", new { });
        Assert.Equal(HttpStatusCode.Conflict, activeDelete.StatusCode);
        using var hidden = await Mutate(outsider, HttpMethod.Post, childPath + "/archive", new AttachmentLifecycleInput(0, 0));
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        var archiveKey = Guid.NewGuid().ToString(); var original = new AttachmentLifecycleInput(2, 1);
        using var archived = await Mutate(member, HttpMethod.Post, childPath + "/archive", original, archiveKey);
        Assert.Equal(HttpStatusCode.OK, archived.StatusCode); var archiveText = await archived.Content.ReadAsStringAsync(ct);
        var archivedChange = (await archived.Content.ReadFromJsonAsync<AttachmentLifecycleChange>(ct))!;
        Assert.True(archivedChange.Changed); Assert.Equal(3, archivedChange.CardVersion);
        Assert.Equal(AttachmentLifecycleState.Archived, archivedChange.Attachment.LifecycleState); Assert.Equal(2, archivedChange.Attachment.Version);
        Assert.Equal(archivedChange.Attachment.UpdatedAt, archivedChange.Attachment.ArchivedAt); Assert.Null(archivedChange.Attachment.DeletedBy);
        using var replay = await Mutate(member, HttpMethod.Post, childPath + "/archive", original, archiveKey);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode); Assert.Equal(archiveText, await replay.Content.ReadAsStringAsync(ct));
        Assert.Empty((await member.GetFromJsonAsync<AttachmentPage>(path, ct))!.Items);
        Assert.Null(await store.FindAttachmentAsync(f.Organization, card.Id, file.Id, ct));
        var archive = (await member.GetFromJsonAsync<AttachmentArchivePage>(path + "/archive", ct))!;
        Assert.Equal(archivedChange.Attachment, Assert.Single(archive.Items)); Assert.True(archive.CanRestore); Assert.False(archive.CanDelete);
        using var nonAdmin = await Mutate(member, HttpMethod.Delete, childPath + "?cardVersion=3&version=2&confirmed=false", new { });
        Assert.Equal(HttpStatusCode.NotFound, nonAdmin.StatusCode);
        using var noConsent = await Mutate(owner, HttpMethod.Delete, childPath + "?cardVersion=3&version=2", new { });
        Assert.Equal(HttpStatusCode.BadRequest, noConsent.StatusCode);
        using var stale = await Mutate(owner, HttpMethod.Post, childPath + "/restore", original);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var restored = await Mutate(member, HttpMethod.Post, childPath + "/restore", new AttachmentLifecycleInput(3, 2));
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode); var restoredChange = (await restored.Content.ReadFromJsonAsync<AttachmentLifecycleChange>(ct))!;
        Assert.Equal(4, restoredChange.CardVersion); Assert.Equal(3, restoredChange.Attachment.Version);
        Assert.Equal(AttachmentLifecycleState.Active, restoredChange.Attachment.LifecycleState);
        Assert.Equal(archivedChange.Attachment.ArchivedAt, restoredChange.Attachment.ArchivedAt);
        using var oldArchive = await Mutate(member, HttpMethod.Post, childPath + "/archive", original, archiveKey);
        Assert.Equal(HttpStatusCode.NotFound, oldArchive.StatusCode);
        using var noOp = await Mutate(member, HttpMethod.Post, childPath + "/restore", new AttachmentLifecycleInput(4, 3));
        Assert.Equal(HttpStatusCode.OK, noOp.StatusCode); Assert.False((await noOp.Content.ReadFromJsonAsync<AttachmentLifecycleChange>(ct))!.Changed);
        Assert.Equal(4, (await work.FindCardAsync(card.Id, ct))!.Version);
        using var rearchived = await Mutate(member, HttpMethod.Post, childPath + "/archive", new AttachmentLifecycleInput(4, 3));
        Assert.Equal(HttpStatusCode.OK, rearchived.StatusCode);
        var deletePath = $"/attachments/{file.Id}?cardId={card.Id}&cardVersion=5&version=4&confirmed=true";
        var deleteKey = Guid.NewGuid().ToString();
        using var deleted = await Mutate(owner, HttpMethod.Delete, deletePath, new { }, deleteKey);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode); var deleteText = await deleted.Content.ReadAsStringAsync(ct);
        var deletion = (await deleted.Content.ReadFromJsonAsync<AttachmentLifecycleChange>(ct))!;
        Assert.Equal(6, deletion.CardVersion); Assert.Equal(5, deletion.Attachment.Version); Assert.Equal(f.Owner, deletion.Attachment.DeletedBy);
        Assert.Equal(deletion.Attachment.UpdatedAt, deletion.Attachment.DeletedAt); Assert.Equal(AttachmentLifecycleState.Deleted, deletion.Attachment.LifecycleState);
        using var deleteReplay = await Mutate(owner, HttpMethod.Delete, deletePath, new { }, deleteKey);
        Assert.Equal(HttpStatusCode.OK, deleteReplay.StatusCode); Assert.Equal(deleteText, await deleteReplay.Content.ReadAsStringAsync(ct));
        using var keyReuse = await Mutate(owner, HttpMethod.Delete, deletePath.Replace("confirmed=true", "confirmed=false", StringComparison.Ordinal), new { }, deleteKey);
        Assert.Equal(HttpStatusCode.Conflict, keyReuse.StatusCode);
        using var forbiddenRestore = await Mutate(owner, HttpMethod.Post, childPath + "/restore", new AttachmentLifecycleInput(6, 5));
        Assert.Equal(HttpStatusCode.NotFound, forbiddenRestore.StatusCode);
        Assert.Empty((await owner.GetFromJsonAsync<AttachmentArchivePage>(path + "/archive", ct))!.Items);
        Assert.Empty((await owner.GetFromJsonAsync<AttachmentPage>(path, ct))!.Items);
        var current = (await work.FindCardAsync(card.Id, ct))!;
        Assert.Equal(card.Title, current.Title); Assert.Equal(card.Description, current.Description); Assert.Equal(card.Rank, current.Rank); Assert.Equal(6, current.Version);
        using var archivedCard = await Mutate(owner, HttpMethod.Post, $"/cards/{card.Id}/archive", new { version = 6 });
        Assert.Equal(HttpStatusCode.OK, archivedCard.StatusCode);
        using var inactiveReplay = await Mutate(owner, HttpMethod.Delete, deletePath, new { }, deleteKey);
        Assert.Equal(HttpStatusCode.NotFound, inactiveReplay.StatusCode);
    }
    [Fact]
    public async Task PRD_14_18_Archive_pages_are_bounded_card_bound_and_refuse_public_nonmembers_and_revoked_receipts()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var member = app.CreateClient(); using var outsider = app.CreateClient(); using var anonymous = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct); await RegisterAndLogin(outsider);
        var work = app.Services.GetRequiredService<IWorkManagementStore>(); var store = app.Services.GetRequiredService<IAttachmentMetadataStore>();
        var at = AttachmentMetadataMapping.DatabaseTimestamp(DateTimeOffset.UtcNow); var card = await work.CreateCardAsync(f.List, Guid.NewGuid(), "Archive pages", null, null, at, ct);
        var files = new List<AttachmentMetadata>();
        for (var i = 0; i < 63; i++)
        {
            var value = await store.CreateUrlAttachmentAsync(Guid.NewGuid(), f.Organization, card.Id, f.Owner, $"Archive {i}", $"https://example.test/{i}", at, ct);
            files.Add((await store.ChangeAttachmentLifecycleAsync(f.Organization, card.Id, value.Id, 1, AttachmentLifecycleState.Active, AttachmentLifecycleState.Archived, f.Owner, at, ct))!);
        }
        var path = $"/cards/{card.Id}/attachments/archive";
        var first = (await member.GetFromJsonAsync<AttachmentArchivePage>(path, ct))!; Assert.Equal(50, first.Items.Count); Assert.NotNull(first.NextCursor);
        var second = (await member.GetFromJsonAsync<AttachmentArchivePage>(path + "?after=" + Uri.EscapeDataString(first.NextCursor), ct))!;
        Assert.Equal(13, second.Items.Count); Assert.Null(second.NextCursor); Assert.Equal(63, first.Items.Concat(second.Items).Select(x => x.Id).Distinct().Count());
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync(path + "?after=" + Uri.EscapeDataString(first.NextCursor.Replace(card.Id.ToString("D"), Guid.NewGuid().ToString("D"), StringComparison.Ordinal)), ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync(path + "?after=" + Uri.EscapeDataString(first.NextCursor[8..]), ct)).StatusCode);
        var board = (await work.FindBoardAsync(f.Board, ct))!; await work.SetBoardVisibilityAsync(f.Board, BoardVisibility.Public, board.Version, at, ct);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync(path + "?after=bad", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path, ct)).StatusCode);
        var restore = $"/cards/{card.Id}/attachments/{files[0].Id}/restore"; var key = Guid.NewGuid().ToString(); var input = new AttachmentLifecycleInput(1, 2);
        using var restored = await Mutate(member, HttpMethod.Post, restore, input, key); Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        using var revoked = await Mutate(owner, HttpMethod.Delete, $"/boards/{f.Board}/members/{f.Recipient}", new { }); Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        // PUBLIC viewing remains available to an Internal Organization member
        // after its explicit Board edit grant is removed.
        var retained = (await member.GetFromJsonAsync<AttachmentArchivePage>(path, ct))!;
        Assert.False(retained.CanRestore); Assert.False(retained.CanDelete); Assert.Equal(50, retained.Items.Count); Assert.NotNull(retained.NextCursor);
        using var denied = await Mutate(member, HttpMethod.Post, restore, input, key); Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        await app.Services.GetRequiredService<IOrganizationStore>().RemoveMemberAsync(f.Organization, f.Recipient, DateTimeOffset.UtcNow, ct);
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync(path, ct)).StatusCode);
        Assert.Equal(2, (await work.FindCardAsync(card.Id, ct))!.Version);
    }
}
