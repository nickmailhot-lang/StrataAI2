using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Api.Tests;
public sealed partial class ApiHostTests
{
    [Fact]
    public async Task PRD_08_Cross_Board_move_preserves_labels_eligible_assignments_history_and_original_source_receipt()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var member = app.CreateClient(); using var departing = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct); await RegisterAndLogin(departing);
        var departedId = (await departing.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        await app.Services.GetRequiredService<IOrganizationStore>().AddOrRestoreMemberAsync(f.Organization, departedId, OrganizationRole.Member, DateTimeOffset.UtcNow, ct);
        using var sourceGrant = await Mutate(owner, HttpMethod.Patch, $"/boards/{f.Board}/members/{departedId}", new { role = "MEMBER" });
        Assert.Equal(HttpStatusCode.OK, sourceGrant.StatusCode);
        using var cardResponse = await Mutate(owner, HttpMethod.Post, $"/lists/{f.List}/cards", new { title = "Stable moved Card" });
        var card = (await cardResponse.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var labelResponse = await Mutate(owner, HttpMethod.Post, $"/boards/{f.Board}/labels", new { name = "Preserved label", color = "blue" });
        var label = (await labelResponse.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var labeled = await Mutate(owner, HttpMethod.Put, $"/cards/{card}/labels/{label}?version=1", new { });
        Assert.Equal(HttpStatusCode.OK, labeled.StatusCode);
        using var assigned = await Mutate(owner, HttpMethod.Put, $"/cards/{card}/members/{f.Recipient}?version=2", new { });
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);
        using var assignedOther = await Mutate(owner, HttpMethod.Put, $"/cards/{card}/members/{departedId}?version=3", new { });
        Assert.Equal(HttpStatusCode.OK, assignedOther.StatusCode);
        using var dates = await Mutate(owner, HttpMethod.Patch, $"/cards/{card}/dates",
            new CardDatesInput(null, DateTimeOffset.UtcNow.AddDays(2).ToString("O"), "UTC", true, false, 4));
        Assert.Equal(HttpStatusCode.OK, dates.StatusCode);
        foreach (var client in new[] { member, departing })
        {
            using var reminder = await Mutate(client, HttpMethod.Post, $"/cards/{card}/reminders", new CardReminderInput("1_HOUR", true, 5, 0));
            Assert.Equal(HttpStatusCode.OK, reminder.StatusCode);
        }
        var reminderStore = app.Services.GetRequiredService<ICardReminderStore>();
        var retainedReminder = await reminderStore.FindAsync(f.Organization, f.Recipient, card, ct);
        var departingReminder = await reminderStore.FindAsync(f.Organization, departedId, card, ct);
        using var boardResponse = await Mutate(owner, HttpMethod.Post, "/boards", new { organizationId = f.Organization, name = "Move destination" });
        var board = (await boardResponse.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var grant = await Mutate(owner, HttpMethod.Patch, $"/boards/{board}/members/{f.Recipient}", new { role = "MEMBER" });
        Assert.Equal(HttpStatusCode.OK, grant.StatusCode);
        using var listResponse = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/lists", new { name = "Destination" });
        var list = (await listResponse.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        var inboxPath = $"/organizations/{f.Organization}/notifications";
        var originalInbox = (await member.GetFromJsonAsync<NotificationInboxPage>(inboxPath, ct))!;
        var originalNotice = Assert.Single(originalInbox.Items);
        Assert.Equal(f.Board, originalNotice.BoardId); Assert.Null(originalNotice.CurrentBoardId);
        var body = new { sourceBoardId = f.Board, destinationListId = list, expectedVersion = 5 }; var key = Guid.NewGuid().ToString();
        using var moved = await Mutate(member, HttpMethod.Post, $"/cards/{card}/move", body, key);
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        var ack = await moved.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(card, ack.GetProperty("id").GetGuid()); Assert.Equal(board, ack.GetProperty("boardId").GetGuid());
        Assert.Equal(6, ack.GetProperty("version").GetInt64());
        var movedNotice = Assert.Single((await member.GetFromJsonAsync<NotificationInboxPage>(inboxPath, ct))!.Items);
        Assert.Equal(originalNotice.Id, movedNotice.Id); Assert.Equal(f.Board, movedNotice.BoardId);
        Assert.Equal(board, movedNotice.CurrentBoardId); Assert.Equal(originalNotice.CreatedAt, movedNotice.CreatedAt);
        Assert.Equal($"/app/{f.Organization}/boards/{board}/cards/{card}", movedNotice.EntityLink);
        Assert.Empty((await departing.GetFromJsonAsync<NotificationInboxPage>(inboxPath, ct))!.Items);
        var readKey = Guid.NewGuid().ToString();
        using var marked = await Mutate(member, HttpMethod.Post, $"{inboxPath}/{movedNotice.Id}/read", new { }, readKey);
        Assert.Equal(HttpStatusCode.OK, marked.StatusCode);
        Assert.Equal(retainedReminder, await reminderStore.FindAsync(f.Organization, f.Recipient, card, ct));
        var suspendedReminder = await reminderStore.FindAsync(f.Organization, departedId, card, ct);
        Assert.NotNull(suspendedReminder); Assert.Equal(departingReminder!.Id, suspendedReminder.Id);
        Assert.Equal("SUSPENDED", suspendedReminder.Status); Assert.Null(suspendedReminder.TriggerAt);
        Assert.Equal(departingReminder.Generation + 1, suspendedReminder.Generation);
        var labels = await owner.GetFromJsonAsync<JsonElement>($"/cards/{card}/labels", ct);
        var copied = Assert.Single(labels.GetProperty("items").EnumerateArray());
        Assert.NotEqual(label, copied.GetProperty("id").GetGuid()); Assert.Equal(board, copied.GetProperty("boardId").GetGuid());
        Assert.Equal("Preserved label", copied.GetProperty("name").GetString()); Assert.Equal("blue", copied.GetProperty("color").GetString());
        var members = await owner.GetFromJsonAsync<JsonElement>($"/cards/{card}/members", ct);
        Assert.Equal(f.Recipient, Assert.Single(members.GetProperty("items").EnumerateArray()).GetProperty("userId").GetGuid());
        var source = await owner.GetFromJsonAsync<JsonElement>($"/boards/{f.Board}", ct);
        Assert.DoesNotContain(source.GetProperty("lists").EnumerateArray().SelectMany(c => c.GetProperty("cards").EnumerateArray()), c => c.GetProperty("id").GetGuid() == card);
        var history = await owner.GetFromJsonAsync<JsonElement>($"/cards/{card}/activity", ct);
        var transitions = history.GetProperty("items").EnumerateArray().Where(e => e.GetProperty("eventType").GetString() == "CARD_MOVED").ToArray();
        Assert.Equal(2, transitions.Length); Assert.Equal(2, transitions.Select(e => e.GetProperty("eventId").GetGuid()).Distinct().Count());
        Assert.All(transitions, e => { Assert.Equal(board, e.GetProperty("currentBoardId").GetGuid()); Assert.Empty(e.GetProperty("metadata").EnumerateObject()); });
        using var later = await Mutate(owner, HttpMethod.Patch, $"/cards/{card}", new { title = "Later moved revision", version = 6 });
        Assert.Equal(HttpStatusCode.OK, later.StatusCode);
        using var replay = await Mutate(member, HttpMethod.Post, $"/cards/{card}/move", body, key);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode); Assert.Equal(ack.ToString(), (await replay.Content.ReadFromJsonAsync<JsonElement>(ct)).ToString());
        using var withdrawn = await Mutate(owner, HttpMethod.Delete, $"/boards/{f.Board}/members/{f.Recipient}", new { });
        Assert.Equal(HttpStatusCode.NoContent, withdrawn.StatusCode);
        using var refused = await Mutate(member, HttpMethod.Post, $"/cards/{card}/move", body, key);
        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
        Assert.Empty((await member.GetFromJsonAsync<NotificationInboxPage>(inboxPath, ct))!.Items);
        using var refusedReadReceipt = await Mutate(member, HttpMethod.Post, $"{inboxPath}/{movedNotice.Id}/read", new { }, readKey);
        Assert.Equal(HttpStatusCode.NotFound, refusedReadReceipt.StatusCode);
        using var destinationView = await member.GetAsync($"/boards/{board}", ct); Assert.Equal(HttpStatusCode.OK, destinationView.StatusCode);
        members = await owner.GetFromJsonAsync<JsonElement>($"/cards/{card}/members", ct);
        Assert.Equal(7, members.GetProperty("cardVersion").GetInt64()); Assert.Single(members.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task PRD_08_Moved_Card_keeps_children_and_watch_identity_but_rechecks_current_destination_access()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var member = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct); var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var card = await work.CreateCardAsync(f.List, Guid.NewGuid(), "Moved children", "Stable body", null, DateTimeOffset.UtcNow, ct);
        var checklistPath = $"/cards/{card.Id}/checklists";
        using var created = await Mutate(owner, HttpMethod.Post, checklistPath, new CreateChecklistInput("Stable checklist", 1));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var checklist = (await created.Content.ReadFromJsonAsync<ChecklistChange>(ct))!.Checklist;
        var itemPath = $"{checklistPath}/{checklist.Id}/items";
        using var added = await Mutate(owner, HttpMethod.Post, itemPath, new CreateChecklistItemInput("Stable item", 2, 1));
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        var item = (await added.Content.ReadFromJsonAsync<ChecklistItemChange>(ct))!.Item;
        var attachmentPath = $"/cards/{card.Id}/attachments";
        using var attached = await Mutate(member, HttpMethod.Post, attachmentPath + "/url",
            new CreateUrlAttachmentInput("Stable URL", "https://example.test/moved", 3));
        Assert.Equal(HttpStatusCode.OK, attached.StatusCode);
        var attachment = (await attached.Content.ReadFromJsonAsync<AttachmentChange>(ct))!.Attachment;
        var commentPath = $"/cards/{card.Id}/comments";
        using var commented = await Mutate(member, HttpMethod.Post, commentPath, new CreateCardCommentInput("Stable comment", 4));
        Assert.Equal(HttpStatusCode.OK, commented.StatusCode);
        var comment = (await commented.Content.ReadFromJsonAsync<CardCommentChange>(ct))!.Comment;
        var watchPath = $"/watch/CARD/{card.Id}";
        using var watched = await Mutate(owner, HttpMethod.Put, watchPath + "?version=0", new { });
        Assert.Equal(HttpStatusCode.OK, watched.StatusCode); var watch = (await watched.Content.ReadFromJsonAsync<WatchState>(ct))!;
        using var memberWatched = await Mutate(member, HttpMethod.Put, watchPath + "?version=0", new { });
        Assert.Equal(HttpStatusCode.OK, memberWatched.StatusCode);
        using var destination = await Mutate(owner, HttpMethod.Post, "/boards", new { organizationId = f.Organization, name = "Private destination", visibility = "PRIVATE" });
        Assert.Equal(HttpStatusCode.Created, destination.StatusCode);
        var board = (await destination.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var parent = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/lists", new { name = "Destination" });
        var list = (await parent.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var moved = await Mutate(owner, HttpMethod.Post, $"/cards/{card.Id}/move", new { sourceBoardId = f.Board, destinationListId = list, expectedVersion = 5 });
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        var current = (await work.FindCardAsync(card.Id, ct))!;
        Assert.Equal(board, current.BoardId); Assert.Equal(6, current.Version); Assert.Equal(card.Description, current.Description);
        var checklists = (await owner.GetFromJsonAsync<ChecklistPage>(checklistPath, ct))!;
        Assert.Equal(board, checklists.BoardId); Assert.Equal(6, checklists.CardVersion); Assert.Equal(checklist.Id, Assert.Single(checklists.Items).Checklist.Id);
        var items = (await owner.GetFromJsonAsync<ChecklistItemPage>(itemPath, ct))!;
        Assert.Equal(board, items.BoardId); Assert.Equal(item, Assert.Single(items.Items));
        var attachments = (await owner.GetFromJsonAsync<AttachmentPage>(attachmentPath, ct))!;
        Assert.Equal(board, attachments.BoardId); Assert.Equal(attachment, Assert.Single(attachments.Items));
        var comments = (await owner.GetFromJsonAsync<CardCommentPage>(commentPath, ct))!;
        Assert.Equal(board, comments.BoardId); Assert.Equal(comment, Assert.Single(comments.Items));
        var currentWatch = (await owner.GetFromJsonAsync<WatchState>(watchPath, ct))!;
        Assert.Equal(board, currentWatch.BoardId); Assert.Equal(watch.SubscriptionId, currentWatch.SubscriptionId);
        Assert.Equal(watch.Version, currentWatch.Version); Assert.True(currentWatch.Watching);
        foreach (var path in new[] { checklistPath, itemPath, attachmentPath, commentPath, watchPath })
        {
            using var refused = await member.GetAsync(path, ct); Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
            Assert.DoesNotContain("Stable", await refused.Content.ReadAsStringAsync(ct));
        }
        using var oldSource = await member.GetAsync($"/boards/{f.Board}", ct); Assert.Equal(HttpStatusCode.OK, oldSource.StatusCode);
        using var refusedEdit = await Mutate(member, HttpMethod.Patch, $"{commentPath}/{comment.Id}", new EditCardCommentInput("Denied", 6, 1));
        Assert.Equal(HttpStatusCode.NotFound, refusedEdit.StatusCode);
        Assert.Equal(comment, Assert.Single((await owner.GetFromJsonAsync<CardCommentPage>(commentPath, ct))!.Items));
        Assert.Equal(current, await work.FindCardAsync(card.Id, ct));
    }

    // Publication metadata is synthetic, as in the existing private download
    // contracts. Real sessions, move admission, private byte staging and delivery
    // execute normally; this does not claim real Worker preview generation.
    [Fact(Skip = "Private staging requires Linux.", SkipUnless = nameof(LinuxPrivateStagingSupported))]
    public async Task PRD_08_Moved_file_and_preview_keep_private_bytes_and_recheck_current_destination()
    {
        var ct = TestContext.Current.CancellationToken; var objects = new UploadObjects(); var selection = new CoverSelectionFixture();
        await using var app = UploadFactory(objects, downloads: true, images: true, covers: selection);
        using var owner = app.CreateClient(); using var member = app.CreateClient(); using var anonymous = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct); var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var card = await work.CreateCardAsync(f.List, Guid.NewGuid(), "Moved private image", null, null, DateTimeOffset.UtcNow, ct);
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==");
        var original = png.Concat("PRIVATE ORIGINAL METADATA"u8.ToArray()).ToArray();
        using var upload = FileRequest($"/cards/{card.Id}/attachments", original, Guid.NewGuid(), "Moved image.png");
        using var uploaded = await member.SendAsync(upload, ct); Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);
        var file = (await uploaded.Content.ReadFromJsonAsync<AttachmentChange>(ct))!.Attachment;
        selection.Organization = f.Organization; selection.Card = card.Id; selection.Selected = file.Id;
        var metadata = Assert.IsType<DownloadMetadata>(app.Services.GetRequiredService<IAttachmentMetadataStore>()); metadata.Clean = true;
        var reference = AttachmentObjectReference.ForPreview(f.Organization, Guid.NewGuid());
        var stored = await objects.WritePrivateAsync(reference, new MemoryStream(png, false), png.Length, ct);
        metadata.PublishedPreview = new(new(reference, stored.SizeBytes, stored.Sha256), 1, 1);
        var downloadPath = $"/cards/{card.Id}/attachments/{file.Id}/download";
        var previewPath = $"/cards/{card.Id}/attachments/{file.Id}/preview";
        var oldOptions = (await member.GetFromJsonAsync<AttachmentDownloadOptions>(downloadPath + "-options", ct))!;
        Assert.Equal(f.Board, oldOptions.BoardId); Assert.Equal(2, oldOptions.CardVersion);
        using var destination = await Mutate(owner, HttpMethod.Post, "/boards", new { organizationId = f.Organization, name = "Private image destination", visibility = "PRIVATE" });
        var board = (await destination.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var parent = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/lists", new { name = "Destination" });
        var list = (await parent.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var moved = await Mutate(owner, HttpMethod.Post, $"/cards/{card.Id}/move", new { sourceBoardId = f.Board, destinationListId = list, expectedVersion = 2 });
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        foreach (var (path, expected) in new[] { (downloadPath, original), (previewPath, png) })
        {
            using var optionsResponse = await owner.GetAsync(path + "-options", ct); Assert.Equal(HttpStatusCode.OK, optionsResponse.StatusCode);
            var options = (await optionsResponse.Content.ReadFromJsonAsync<AttachmentDownloadOptions>(ct))!;
            Assert.Equal(board, options.BoardId); Assert.Equal(3, options.CardVersion); Assert.Equal(file.Id, options.AttachmentId);
            var json = await optionsResponse.Content.ReadAsStringAsync(ct);
            Assert.DoesNotContain(reference.ObjectKey, json); Assert.DoesNotContain(stored.Sha256, json);
            using var delivered = await owner.GetAsync(path, ct); Assert.Equal(HttpStatusCode.OK, delivered.StatusCode);
            Assert.Equal(expected, await delivered.Content.ReadAsByteArrayAsync(ct));
            Assert.True(delivered.Headers.CacheControl!.Private); Assert.True(delivered.Headers.CacheControl.NoStore);
        }
        var reads = objects.Reads;
        foreach (var path in new[] { downloadPath, previewPath, downloadPath + "-options", previewPath + "-options",
            $"/attachments/{file.Id}/download?cardId={card.Id}", $"/attachments/{file.Id}/preview?cardId={card.Id}" })
        {
            using var refused = await member.GetAsync(path, ct); Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
            Assert.Null(refused.Content.Headers.ContentDisposition);
        }
        Assert.Equal(reads, objects.Reads);
        Assert.Equal(file, await app.Services.GetRequiredService<IAttachmentMetadataStore>().FindAttachmentAsync(f.Organization, card.Id, file.Id, ct));
        using var stillSource = await member.GetAsync($"/boards/{f.Board}", ct); Assert.Equal(HttpStatusCode.OK, stillSource.StatusCode);
        var coverPath = $"/cards/{card.Id}/cover/image";
        foreach (var client in new[] { member, anonymous })
        {
            using var refusedCover = await client.GetAsync(coverPath, ct); Assert.Equal(HttpStatusCode.NotFound, refusedCover.StatusCode);
            Assert.Null(refusedCover.Content.Headers.ContentDisposition);
        }
        using (var oldRevision = await owner.GetAsync(coverPath + "?cardVersion=2", ct)) Assert.Equal(HttpStatusCode.NotFound, oldRevision.StatusCode);
        Assert.Equal(reads, objects.Reads);
        using (var privateCover = await owner.GetAsync(coverPath + "?cardVersion=3", ct))
        { Assert.Equal(HttpStatusCode.OK, privateCover.StatusCode); Assert.Equal(png, await privateCover.Content.ReadAsByteArrayAsync(ct)); }
        using (var publish = await Mutate(owner, HttpMethod.Patch, $"/boards/{board}/visibility", new { visibility = "PUBLIC", version = 1 }))
            Assert.Equal(HttpStatusCode.OK, publish.StatusCode);
        using (var publicCover = await anonymous.GetAsync(coverPath, ct))
        {
            Assert.Equal(HttpStatusCode.OK, publicCover.StatusCode); Assert.Equal(png, await publicCover.Content.ReadAsByteArrayAsync(ct));
            Assert.Equal("cover.png", publicCover.Content.Headers.ContentDisposition!.FileName);
            Assert.True(publicCover.Headers.CacheControl!.Private); Assert.True(publicCover.Headers.CacheControl.NoStore);
        }
        reads = objects.Reads;
        foreach (var path in new[] { downloadPath, previewPath })
        {
            using var privateOriginal = await anonymous.GetAsync(path, ct); Assert.Equal(HttpStatusCode.Unauthorized, privateOriginal.StatusCode);
        }
        Assert.Equal(reads, objects.Reads);
        using (var hide = await Mutate(owner, HttpMethod.Patch, $"/boards/{board}/visibility", new { visibility = "PRIVATE", version = 2 }))
            Assert.Equal(HttpStatusCode.OK, hide.StatusCode);
        using (var hiddenCover = await anonymous.GetAsync(coverPath, ct)) Assert.Equal(HttpStatusCode.NotFound, hiddenCover.StatusCode);
        Assert.Equal(reads, objects.Reads);
        Assert.Equal(3, (await work.FindCardAsync(card.Id, ct))!.Version);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PRD_08_Move_preserves_legacy_and_explicit_source_receipts_and_refuses_revoked_actor(bool explicitSource)
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var member = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct);
        using var listResponse = await Mutate(owner, HttpMethod.Post, $"/boards/{f.Board}/lists", new { name = "Destination" });
        Assert.Equal(HttpStatusCode.Created, listResponse.StatusCode);
        var destination = (await listResponse.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var cardResponse = await Mutate(owner, HttpMethod.Post, $"/lists/{f.List}/cards", new { title = "Source receipt subject" });
        Assert.Equal(HttpStatusCode.Created, cardResponse.StatusCode);
        var card = (await cardResponse.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        object body = explicitSource ? new { destinationListId = destination, expectedVersion = 1, sourceBoardId = f.Board }
            : new { destinationListId = destination, expectedVersion = 1 };
        var key = Guid.NewGuid().ToString();
        using var moved = await Mutate(member, HttpMethod.Post, $"/cards/{card}/move", body, key);
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        var acknowledgment = await moved.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(2, acknowledgment.GetProperty("version").GetInt64());
        using var later = await Mutate(owner, HttpMethod.Patch, $"/cards/{card}", new { title = "Later revision", version = 2 });
        Assert.Equal(HttpStatusCode.OK, later.StatusCode);
        using var replay = await Mutate(member, HttpMethod.Post, $"/cards/{card}/move", body, key);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(acknowledgment.ToString(), (await replay.Content.ReadFromJsonAsync<JsonElement>(ct)).ToString());
        var history = await owner.GetFromJsonAsync<JsonElement>($"/cards/{card}/activity", ct);
        Assert.Single(history.GetProperty("items").EnumerateArray(), row => row.GetProperty("eventType").GetString() == "CARD_MOVED");
        using var forged = await Mutate(member, HttpMethod.Post, $"/cards/{card}/move",
            new { destinationListId = destination, expectedVersion = 1, sourceBoardId = Guid.NewGuid() }, key);
        Assert.Equal(HttpStatusCode.NotFound, forged.StatusCode);
        Assert.DoesNotContain("Source receipt subject", await forged.Content.ReadAsStringAsync(ct));
        using var removed = await Mutate(owner, HttpMethod.Delete, $"/boards/{f.Board}/members/{f.Recipient}", new { });
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        using var revoked = await Mutate(member, HttpMethod.Post, $"/cards/{card}/move", body, key);
        Assert.Equal(HttpStatusCode.NotFound, revoked.StatusCode);
        var current = await owner.GetFromJsonAsync<JsonElement>($"/boards/{f.Board}", ct);
        var persisted = Assert.Single(current.GetProperty("lists").EnumerateArray()
            .SelectMany(column => column.GetProperty("cards").EnumerateArray()), row => row.GetProperty("id").GetGuid() == card);
        Assert.Equal(3, persisted.GetProperty("version").GetInt64());
        Assert.Equal("Later revision", persisted.GetProperty("title").GetString());
    }

    [Fact]
    public async Task PRD_08_Move_source_scope_does_not_admit_foreign_Organization_or_ungranted_destination()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var member = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct);
        using var cardResponse = await Mutate(owner, HttpMethod.Post, $"/lists/{f.List}/cards", new { title = "Unchanged move subject" });
        var card = (await cardResponse.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var boardResponse = await Mutate(owner, HttpMethod.Post, "/boards", new { organizationId = f.Organization, name = "Private destination" });
        var board = (await boardResponse.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var listResponse = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/lists", new { name = "Hidden destination" });
        var destination = (await listResponse.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var denied = await Mutate(member, HttpMethod.Post, $"/cards/{card}/move",
            new { destinationListId = destination, expectedVersion = 1, sourceBoardId = f.Board });
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.DoesNotContain("Hidden destination", await denied.Content.ReadAsStringAsync(ct));
        using var otherOrgResponse = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Other Organization" });
        var otherOrg = (await otherOrgResponse.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        using var foreignBoardResponse = await Mutate(owner, HttpMethod.Post, "/boards", new { organizationId = otherOrg, name = "Foreign source" });
        var foreignBoard = (await foreignBoardResponse.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var foreign = await Mutate(owner, HttpMethod.Post, $"/cards/{card}/move",
            new { destinationListId = f.List, expectedVersion = 1, sourceBoardId = foreignBoard });
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        var history = await owner.GetFromJsonAsync<JsonElement>($"/cards/{card}/activity", ct);
        Assert.DoesNotContain(history.GetProperty("items").EnumerateArray(), row => row.GetProperty("eventType").GetString() == "CARD_MOVED");
    }
}
