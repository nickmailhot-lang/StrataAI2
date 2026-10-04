using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task PRD_15_CardGroupsUseAssigneesAndOriginalBoardGroupReceiptsRequireCurrentAdministration()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var member = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct);
        var work = app.Services.GetRequiredService<IWorkManagementStore>(); var commands = app.Services.GetRequiredService<IWorkManagementService>();
        var card = await work.CreateCardAsync(f.List, Guid.NewGuid(), "Group receipts", null, null, DateTimeOffset.UtcNow, ct);
        Assert.True((await commands.SetCardMemberAsync(card.Id, f.Owner, f.Owner, true, 1, "group-assignment", ct)).Succeeded);
        Assert.True((await commands.SetCardMemberAsync(card.Id, f.Recipient, f.Owner, true, 2, "group-assignment", ct)).Succeeded);
        var path = $"/cards/{card.Id}/comments"; var cardKey = Guid.NewGuid().ToString();
        var cardInput = new CreateCardCommentInput("Current assignees @card; literal @board", 3, MassMentionConfirmation: new(true, false));
        using var cardCreated = await Mutate(member, HttpMethod.Post, path, cardInput, cardKey);
        Assert.Equal(HttpStatusCode.OK, cardCreated.StatusCode); var first = (await cardCreated.Content.ReadFromJsonAsync<CardCommentChange>(ct))!;
        var inbox = app.Services.GetRequiredService<IWorkNotificationStore>();
        Assert.Single(await inbox.ListCardNotificationsAsync(f.Organization, f.Owner, cancellationToken: ct), row => row.NotificationType == "MENTION_CREATED");
        Assert.DoesNotContain(await inbox.ListCardNotificationsAsync(f.Organization, f.Recipient, cancellationToken: ct), row => row.NotificationType == "MENTION_CREATED");
        var boardKey = Guid.NewGuid().ToString(); var boardInput = new CreateCardCommentInput("Admin notification @board", 4, MassMentionConfirmation: new(false, true));
        using var denied = await Mutate(member, HttpMethod.Post, path, boardInput, boardKey);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        using var promote = await Mutate(owner, HttpMethod.Patch, $"/boards/{f.Board}/members/{f.Recipient}", new { role = "ADMIN" });
        Assert.Equal(HttpStatusCode.OK, promote.StatusCode);
        using var boardCreated = await Mutate(member, HttpMethod.Post, path, boardInput, boardKey);
        Assert.Equal(HttpStatusCode.OK, boardCreated.StatusCode); var boardChange = (await boardCreated.Content.ReadFromJsonAsync<CardCommentChange>(ct))!;
        using var boardRetry = await Mutate(member, HttpMethod.Post, path, boardInput, boardKey);
        Assert.Equal(boardChange, await boardRetry.Content.ReadFromJsonAsync<CardCommentChange>(ct));
        using var demote = await Mutate(owner, HttpMethod.Patch, $"/boards/{f.Board}/members/{f.Recipient}", new { role = "MEMBER" });
        Assert.Equal(HttpStatusCode.OK, demote.StatusCode);
        using var revokedReceipt = await Mutate(member, HttpMethod.Post, path, boardInput, boardKey);
        Assert.Equal(HttpStatusCode.NotFound, revokedReceipt.StatusCode);
        Assert.DoesNotContain("Admin notification", await revokedReceipt.Content.ReadAsStringAsync(ct));
        Assert.Equal(5, (await work.FindCardAsync(card.Id, ct))!.Version);
        Assert.Equal(2, (await inbox.ListCardNotificationsAsync(f.Organization, f.Owner, cancellationToken: ct)).Count(row => row.NotificationType == "MENTION_CREATED"));
        using var retainedCardReceipt = await Mutate(member, HttpMethod.Post, path, cardInput, cardKey);
        Assert.Equal(HttpStatusCode.OK, retainedCardReceipt.StatusCode);
        Assert.Equal(first, await retainedCardReceipt.Content.ReadFromJsonAsync<CardCommentChange>(ct));
    }
    [Fact]
    public async Task PRD_15_ConfirmedBoardMentionsRequireAdministrationAndRateLimitAtomicallyAcrossComments()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var member = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct); var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var card = await work.CreateCardAsync(f.List, Guid.NewGuid(), "Groups", null, null, DateTimeOffset.UtcNow, ct);
        var path = $"/cards/{card.Id}/comments";
        using var plain = await Mutate(owner, HttpMethod.Post, path, new CreateCardCommentInput("Literal @board", 1));
        Assert.Equal(HttpStatusCode.OK, plain.StatusCode);
        var inbox = app.Services.GetRequiredService<IWorkNotificationStore>();
        Assert.Empty(await inbox.ListCardNotificationsAsync(f.Organization, f.Recipient, cancellationToken: ct));
        using var invalid = await Mutate(owner, HttpMethod.Post, path,
            new CreateCardCommentInput("Not a declaration https://example.test/@board", 2, MassMentionConfirmation: new(false, true)));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var denied = await Mutate(member, HttpMethod.Post, path,
            new CreateCardCommentInput("@board", 2, MassMentionConfirmation: new(false, true)));
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        var key = Guid.NewGuid().ToString(); var input = new CreateCardCommentInput("First confirmed @board @board", 2, MassMentionConfirmation: new(false, true));
        using var created = await Mutate(owner, HttpMethod.Post, path, input, key);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode); var first = (await created.Content.ReadFromJsonAsync<CardCommentChange>(ct))!;
        using var replay = await Mutate(owner, HttpMethod.Post, path, input, key);
        Assert.Equal(first, await replay.Content.ReadFromJsonAsync<CardCommentChange>(ct));
        using var noop = await Mutate(owner, HttpMethod.Patch, $"{path}/{first.Comment.Id}",
            new EditCardCommentInput(input.Content, 3, 1, MassMentionConfirmation: new(false, true)));
        Assert.Equal(HttpStatusCode.OK, noop.StatusCode); Assert.False((await noop.Content.ReadFromJsonAsync<CardCommentChange>(ct))!.Changed);
        using var edited = await Mutate(owner, HttpMethod.Patch, $"{path}/{first.Comment.Id}",
            new EditCardCommentInput("First confirmed @board edited", 3, 1, MassMentionConfirmation: new(false, true)));
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        for (long version = 4; version <= 5; version++)
        {
            using var next = await Mutate(owner, HttpMethod.Post, path,
                new CreateCardCommentInput($"Confirmed {version} @board", version, MassMentionConfirmation: new(false, true)));
            Assert.Equal(HttpStatusCode.OK, next.StatusCode);
        }
        var refusedKey = Guid.NewGuid().ToString(); var refusedInput = new CreateCardCommentInput("Fourth @board", 6, MassMentionConfirmation: new(false, true));
        using var limited = await Mutate(owner, HttpMethod.Post, path, refusedInput, refusedKey);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(6, (await work.FindCardAsync(card.Id, ct))!.Version);
        var page = (await owner.GetFromJsonAsync<CardCommentPage>(path, ct))!;
        Assert.Equal(4, page.Items.Count); Assert.DoesNotContain(page.Items, row => row.Content == refusedInput.Content);
        Assert.Equal(3, (await inbox.ListCardNotificationsAsync(f.Organization, f.Recipient, cancellationToken: ct)).Count);
        Assert.Empty(await inbox.ListCardNotificationsAsync(f.Organization, f.Owner, cancellationToken: ct));
        using var limitedRetry = await Mutate(owner, HttpMethod.Post, path, refusedInput, refusedKey);
        Assert.Equal(HttpStatusCode.TooManyRequests, limitedRetry.StatusCode);
        Assert.DoesNotContain("MassMentionConfirmation", JsonSerializer.Serialize(new CreateCardCommentInput("Old request", 1)));
    }
    [Fact]
    public async Task PRD_15_SelectedMentionIdentityAndRevisionAreValidatedBeforeWritesAndNoOps()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var recipient = app.CreateClient();
        var f = await NotificationFixture(app, owner, recipient, ct); var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var card = await work.CreateCardAsync(f.List, Guid.NewGuid(), "Selected mention", null, null, DateTimeOffset.UtcNow, ct);
        var path = $"/cards/{card.Id}/comments"; var handle = $"u_{f.Recipient:N}"; var text = $"Hello @{handle}";
        var key = Guid.NewGuid().ToString();
        var selection = new CardCommentMentionSelection(f.Recipient, handle, 1);
        using var stale = await Mutate(owner, HttpMethod.Post, path, new CreateCardCommentInput(text, 1, [selection with { HandleVersion = 2 }]), key);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var substituted = await Mutate(owner, HttpMethod.Post, path, new CreateCardCommentInput(text, 1, [selection with { UserId = f.Owner }]), key);
        Assert.Equal(HttpStatusCode.Conflict, substituted.StatusCode);
        using var notDeclared = await Mutate(owner, HttpMethod.Post, path, new CreateCardCommentInput("Literal", 1, [selection]), key);
        Assert.Equal(HttpStatusCode.BadRequest, notDeclared.StatusCode);
        using var duplicate = await Mutate(owner, HttpMethod.Post, path, new CreateCardCommentInput(text, 1, [selection, selection]), key);
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Equal(1, (await work.FindCardAsync(card.Id, ct))!.Version);
        var inbox = app.Services.GetRequiredService<IWorkNotificationStore>();
        Assert.Empty(await inbox.ListCardNotificationsAsync(f.Organization, f.Recipient, cancellationToken: ct));
        var input = new CreateCardCommentInput(text, 1, [selection]);
        using var created = await Mutate(owner, HttpMethod.Post, path, input, key);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode); var change = (await created.Content.ReadFromJsonAsync<CardCommentChange>(ct))!;
        using var retry = await Mutate(owner, HttpMethod.Post, path, input, key);
        Assert.Equal(change, await retry.Content.ReadFromJsonAsync<CardCommentChange>(ct));
        using var staleNoop = await Mutate(owner, HttpMethod.Patch, $"{path}/{change.Comment.Id}", new EditCardCommentInput(text, 2, 1, [selection with { HandleVersion = 2 }]));
        Assert.Equal(HttpStatusCode.Conflict, staleNoop.StatusCode);
        using var noop = await Mutate(owner, HttpMethod.Patch, $"{path}/{change.Comment.Id}", new EditCardCommentInput(text, 2, 1, [selection]));
        Assert.Equal(HttpStatusCode.OK, noop.StatusCode); Assert.False((await noop.Content.ReadFromJsonAsync<CardCommentChange>(ct))!.Changed);
        Assert.Single(await inbox.ListCardNotificationsAsync(f.Organization, f.Recipient, cancellationToken: ct));
        Assert.DoesNotContain("MentionSelections", JsonSerializer.Serialize(new CreateCardCommentInput("Old request", 1)));
        Assert.DoesNotContain("MentionSelections", JsonSerializer.Serialize(new EditCardCommentInput("Old request", 1, 1)));
        var oldFingerprint = WorkCommand.Create(f.Owner, Guid.Parse(key), "COMMENT_ADDED", card.Id,
            new { cardId = card.Id, input = new { Content = "Old request", CardVersion = 1L } }, "comment_not_found").Fingerprint;
        Assert.Equal(oldFingerprint, WorkCommand.Create(f.Owner, Guid.Parse(key), "COMMENT_ADDED", card.Id,
            new { cardId = card.Id, input = new CreateCardCommentInput("Old request", 1) }, "comment_not_found").Fingerprint);
    }
    [Fact]
    public async Task PRD_15_HTTPUsernameMentionRetryPublishesPrivateInboxOnceAndRevokedBoardHidesIt()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var recipient = app.CreateClient();
        var f = await NotificationFixture(app, owner, recipient, ct); var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var card = await work.CreateCardAsync(f.List, Guid.NewGuid(), "Mention Card", null, null, DateTimeOffset.UtcNow, ct);
        var path = $"/cards/{card.Id}/comments"; var key = Guid.NewGuid().ToString();
        var input = new CreateCardCommentInput($"Private words @u_{f.Recipient:N} @u_{f.Owner:N}", 1);
        using var created = await Mutate(owner, HttpMethod.Post, path, input, key);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var change = (await created.Content.ReadFromJsonAsync<CardCommentChange>(ct))!;
        using var retry = await Mutate(owner, HttpMethod.Post, path, input, key);
        Assert.Equal(change, await retry.Content.ReadFromJsonAsync<CardCommentChange>(ct));
        var inbox = $"/organizations/{f.Organization}/notifications";
        using var response = await recipient.GetAsync(inbox, ct);
        Assert.NotNull(response.Headers.CacheControl);
        Assert.True(response.Headers.CacheControl.NoStore);
        Assert.True(response.Headers.CacheControl.Private);
        var page = (await response.Content.ReadFromJsonAsync<JsonElement>(ct));
        var item = Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal("MENTION_CREATED", item.GetProperty("type").GetString());
        Assert.Equal(f.Recipient, item.GetProperty("recipientId").GetGuid());
        Assert.Equal(f.Owner, item.GetProperty("actorId").GetGuid());
        Assert.Equal($"/app/{f.Organization}/boards/{f.Board}/cards/{card.Id}", item.GetProperty("entityLink").GetString());
        Assert.DoesNotContain("Private words", page.GetRawText());
        Assert.Empty((await owner.GetFromJsonAsync<JsonElement>(inbox, ct)).GetProperty("items").EnumerateArray());
        await work.RemoveBoardMemberAsync(f.Board, f.Recipient, DateTimeOffset.UtcNow, ct);
        Assert.Empty((await recipient.GetFromJsonAsync<JsonElement>(inbox, ct)).GetProperty("items").EnumerateArray());
        using var refused = await Mutate(recipient, HttpMethod.Post, path, new CreateCardCommentInput("Rejected", 2));
        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
    }
    [Fact]
    public async Task PRD_15_TC_01_03_04_07_HTTPAuthorCommandsReconcileRevisionsAndRedactionWithoutBodyReplay()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var member = app.CreateClient(); using var anonymous = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct); var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var card = await work.CreateCardAsync(f.List, Guid.NewGuid(), "Comments", null, null, DateTimeOffset.UtcNow, ct);
        var path = $"/cards/{card.Id}/comments"; var key = Guid.NewGuid().ToString(); var input = new CreateCardCommentInput("  Literal <script>\r\n🙂  ", 1);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path, ct)).StatusCode);
        using var invalid = await Mutate(owner, HttpMethod.Post, path, new CreateCardCommentInput(" ", 1), key);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var created = await Mutate(owner, HttpMethod.Post, path, input, key);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode); Assert.True(created.Headers.CacheControl!.NoStore);
        var change = (await created.Content.ReadFromJsonAsync<CardCommentChange>(ct))!;
        Assert.Equal(2, change.CardVersion); Assert.Equal("Literal <script>\n🙂", change.Comment.Content);
        using var replay = await Mutate(owner, HttpMethod.Post, path, input, key);
        Assert.Equal(change, await replay.Content.ReadFromJsonAsync<CardCommentChange>(ct));
        var childPath = $"{path}/{change.Comment.Id}";
        using var foreign = await Mutate(member, HttpMethod.Patch, childPath, new EditCardCommentInput("Foreign", 2, 1));
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode); Assert.DoesNotContain("Literal", await foreign.Content.ReadAsStringAsync(ct));
        using var stale = await Mutate(owner, HttpMethod.Patch, childPath, new EditCardCommentInput("Stale", 1, 1));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var edited = await Mutate(owner, HttpMethod.Patch, childPath, new EditCardCommentInput("Edited", 2, 1));
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode); var updated = (await edited.Content.ReadFromJsonAsync<CardCommentChange>(ct))!;
        Assert.Equal(3, updated.CardVersion); Assert.Equal(2, updated.Comment.Version); Assert.NotNull(updated.Comment.EditedAt);
        using var refusedDelete = await Mutate(owner, HttpMethod.Delete, childPath, new DeleteCardCommentInput(3, 2, false));
        Assert.Equal(HttpStatusCode.BadRequest, refusedDelete.StatusCode);
        var deleteKey = Guid.NewGuid().ToString(); var deleteInput = new DeleteCardCommentInput(3, 2, true);
        using var deleted = await Mutate(owner, HttpMethod.Delete, childPath, deleteInput, deleteKey);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode); var tombstone = (await deleted.Content.ReadFromJsonAsync<CardCommentChange>(ct))!;
        Assert.Null(tombstone.Comment.Content); Assert.Equal(4, tombstone.CardVersion); Assert.Equal(3, tombstone.Comment.Version);
        using var deleteReplay = await Mutate(owner, HttpMethod.Delete, childPath, deleteInput, deleteKey);
        Assert.Equal(tombstone, await deleteReplay.Content.ReadFromJsonAsync<CardCommentChange>(ct));
        using var hiddenOldReceipt = await Mutate(owner, HttpMethod.Post, path, input, key);
        Assert.Equal(HttpStatusCode.NotFound, hiddenOldReceipt.StatusCode); Assert.DoesNotContain("Literal", await hiddenOldReceipt.Content.ReadAsStringAsync(ct));
        var page = (await member.GetFromJsonAsync<CardCommentPage>(path, ct))!;
        Assert.Null(Assert.Single(page.Items).Content); Assert.Equal(f.Owner, page.Items[0].AuthorId); Assert.Equal(updated.Comment.EditedAt, page.Items[0].EditedAt);
        var events = (await app.Services.GetRequiredService<IWorkEventReader>().ReadAsync(f.Organization, f.Board, 0, 100, ct)).Events;
        Assert.Equal(3, events.Count(x => x.Event.EventType.StartsWith("COMMENT_", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task PRD_15_TC_05_10_HTTPArchiveAndMembershipRevocationRefuseOriginalCommandRecovery()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var member = app.CreateClient(); var f = await NotificationFixture(app, owner, member, ct);
        var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var card = await work.CreateCardAsync(f.List, Guid.NewGuid(), "Comments", null, null, DateTimeOffset.UtcNow, ct);
        var path = $"/cards/{card.Id}/comments"; var key = Guid.NewGuid().ToString(); var input = new CreateCardCommentInput("Member owned", 1);
        using var created = await Mutate(member, HttpMethod.Post, path, input, key); Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        using var archived = await Mutate(owner, HttpMethod.Post, $"/cards/{card.Id}/archive", new { version = 2 }); Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        using var deniedArchived = await Mutate(member, HttpMethod.Post, path, input, key); Assert.Equal(HttpStatusCode.NotFound, deniedArchived.StatusCode);
        Assert.False((await member.GetFromJsonAsync<CardCommentPage>(path, ct))!.CanComment);
        using var restored = await Mutate(owner, HttpMethod.Post, $"/cards/{card.Id}/restore", new { version = 3 }); Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        using var recovered = await Mutate(member, HttpMethod.Post, path, input, key); Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        await work.RemoveBoardMemberAsync(f.Board, f.Recipient, DateTimeOffset.UtcNow, ct);
        using var deniedReceipt = await Mutate(member, HttpMethod.Post, path, input, key); Assert.Equal(HttpStatusCode.NotFound, deniedReceipt.StatusCode);
        using var deniedRead = await member.GetAsync(path, ct); Assert.Equal(HttpStatusCode.NotFound, deniedRead.StatusCode);
        Assert.DoesNotContain("Member owned", await deniedReceipt.Content.ReadAsStringAsync(ct));
        Assert.Single((await owner.GetFromJsonAsync<CardCommentPage>(path, ct))!.Items);
    }
}
