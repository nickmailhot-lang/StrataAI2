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
        var body = new { sourceBoardId = f.Board, destinationListId = list, expectedVersion = 5 }; var key = Guid.NewGuid().ToString();
        using var moved = await Mutate(member, HttpMethod.Post, $"/cards/{card}/move", body, key);
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        var ack = await moved.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(card, ack.GetProperty("id").GetGuid()); Assert.Equal(board, ack.GetProperty("boardId").GetGuid());
        Assert.Equal(6, ack.GetProperty("version").GetInt64());
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
        using var destinationView = await member.GetAsync($"/boards/{board}", ct); Assert.Equal(HttpStatusCode.OK, destinationView.StatusCode);
        members = await owner.GetFromJsonAsync<JsonElement>($"/cards/{card}/members", ct);
        Assert.Equal(7, members.GetProperty("cardVersion").GetInt64()); Assert.Single(members.GetProperty("items").EnumerateArray());
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
