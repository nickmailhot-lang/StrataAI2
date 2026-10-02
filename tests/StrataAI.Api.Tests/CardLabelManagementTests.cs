using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task Card_label_face_previews_are_bounded_and_follow_Board_visibility_and_parent_lifecycle()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var board = await TelemetryBoard(owner, ct);
        using var createdList = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/lists", new { name = "Label previews" });
        var list = (await createdList.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var createdCard = await Mutate(owner, HttpMethod.Post, $"/lists/{list}/cards", new { title = "Preview Card" });
        var card = (await createdCard.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        for (var i = 0; i < 8; i++)
        {
            using var labelResponse = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/labels", new { name = $"Label {i}", color = "blue" });
            var label = (await labelResponse.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
            using var assigned = await Mutate(owner, HttpMethod.Put, $"/cards/{card}/labels/{label}?version={i + 1}", new { });
            Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);
        }
        using var guest = app.CreateClient(); using var denied = await guest.GetAsync($"/boards/{board}", ct);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        using var publish = await Mutate(owner, HttpMethod.Patch, $"/boards/{board}/visibility", new { visibility = "PUBLIC", version = 1 });
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);
        var snapshot = await guest.GetFromJsonAsync<JsonElement>($"/boards/{board}", ct);
        var preview = snapshot.GetProperty("cardLabels").GetProperty(card.ToString());
        Assert.Equal(8, preview.GetProperty("total").GetInt64()); Assert.Equal(6, preview.GetProperty("items").GetArrayLength());
        Assert.Equal("Label 0", preview.GetProperty("items")[0].GetProperty("name").GetString());
        Assert.False(snapshot.GetProperty("access").GetProperty("canEdit").GetBoolean());
        using var archive = await Mutate(owner, HttpMethod.Post, $"/lists/{list}/archive", new { version = 1 });
        Assert.Equal(HttpStatusCode.OK, archive.StatusCode);
        var archivedSnapshot = await guest.GetFromJsonAsync<JsonElement>($"/boards/{board}", ct);
        Assert.False(archivedSnapshot.GetProperty("cardLabels").TryGetProperty(card.ToString(), out _));
    }
    [Fact]
    public async Task Card_label_assignment_is_versioned_retry_safe_and_definition_deletion_advances_Card_revision()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var board = await TelemetryBoard(owner, ct);
        using var createdList = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/lists", new { name = "Labels" });
        var list = (await createdList.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var createdCard = await Mutate(owner, HttpMethod.Post, $"/lists/{list}/cards", new { title = "Retained Card", description = "Retained description" });
        var original = await createdCard.Content.ReadFromJsonAsync<JsonElement>(ct); var card = original.GetProperty("id").GetGuid();
        using var createdLabel = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/labels", new { name = "Priority", color = "red" });
        var label = (await createdLabel.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        var key = Guid.NewGuid().ToString(); var path = $"/cards/{card}/labels/{label}";
        using var assigned = await Mutate(owner, HttpMethod.Put, path + "?version=1", new { }, key);
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode); var receipt = await assigned.Content.ReadAsStringAsync(ct);
        var changed = JsonSerializer.Deserialize<JsonElement>(receipt);
        Assert.True(changed.GetProperty("assigned").GetBoolean()); Assert.True(changed.GetProperty("changed").GetBoolean());
        Assert.Equal(2, changed.GetProperty("card").GetProperty("version").GetInt64());
        var assignedPage = await owner.GetFromJsonAsync<JsonElement>($"/cards/{card}/labels", ct);
        Assert.Equal(card, assignedPage.GetProperty("cardId").GetGuid()); Assert.Equal(2, assignedPage.GetProperty("cardVersion").GetInt64());
        Assert.Equal(label, Assert.Single(assignedPage.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Equal(JsonValueKind.Null, assignedPage.GetProperty("nextCursor").ValueKind);
        using var replay = await Mutate(owner, HttpMethod.Put, path + "?version=1", new { }, key);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode); Assert.Equal(receipt, await replay.Content.ReadAsStringAsync(ct));
        using var stale = await Mutate(owner, HttpMethod.Delete, path + "?version=1", new { });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var duplicate = await Mutate(owner, HttpMethod.Put, path + "?version=2", new { });
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        var unchanged = await duplicate.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.False(unchanged.GetProperty("changed").GetBoolean()); Assert.Equal(2, unchanged.GetProperty("card").GetProperty("version").GetInt64());
        using var removed = await Mutate(owner, HttpMethod.Delete, path + "?version=2", new { });
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.Equal(3, (await removed.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("card").GetProperty("version").GetInt64());
        using var reassigned = await Mutate(owner, HttpMethod.Put, path + "?version=3", new { });
        Assert.Equal(HttpStatusCode.OK, reassigned.StatusCode);
        using var deletedLabel = await Mutate(owner, HttpMethod.Delete, $"/labels/{label}?version=1&confirmed=true", new { });
        Assert.Equal(HttpStatusCode.OK, deletedLabel.StatusCode);
        var snapshot = await owner.GetFromJsonAsync<JsonElement>($"/boards/{board}", ct);
        var retained = Assert.Single(Assert.Single(snapshot.GetProperty("lists").EnumerateArray()).GetProperty("cards").EnumerateArray());
        Assert.Equal(card, retained.GetProperty("id").GetGuid()); Assert.Equal(5, retained.GetProperty("version").GetInt64());
        Assert.Equal(original.GetProperty("title").GetString(), retained.GetProperty("title").GetString());
        Assert.Equal(original.GetProperty("description").GetString(), retained.GetProperty("description").GetString());
        Assert.Equal(original.GetProperty("rank").GetString(), retained.GetProperty("rank").GetString());
        var emptyPage = await owner.GetFromJsonAsync<JsonElement>($"/cards/{card}/labels", ct);
        Assert.Empty(emptyPage.GetProperty("items").EnumerateArray()); Assert.Equal(5, emptyPage.GetProperty("cardVersion").GetInt64());
        using var deletedLabelReplay = await Mutate(owner, HttpMethod.Put, path + "?version=1", new { }, key);
        Assert.Equal(HttpStatusCode.NotFound, deletedLabelReplay.StatusCode);
    }

    [Fact]
    public async Task Card_label_commands_reject_outsiders_cross_Board_labels_and_archived_parent_replays()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var board = await TelemetryBoard(owner, ct); var otherBoard = await TelemetryBoard(owner, ct);
        using var createdList = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/lists", new { name = "Private parent" });
        var list = (await createdList.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var createdCard = await Mutate(owner, HttpMethod.Post, $"/lists/{list}/cards", new { title = "Private Card" });
        var card = (await createdCard.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var localLabel = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/labels", new { name = "Private label", color = "blue" });
        var label = (await localLabel.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var otherLabel = await Mutate(owner, HttpMethod.Post, $"/boards/{otherBoard}/labels", new { name = "Other label", color = "red" });
        var other = (await otherLabel.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var cross = await Mutate(owner, HttpMethod.Put, $"/cards/{card}/labels/{other}?version=1", new { });
        Assert.Equal(HttpStatusCode.NotFound, cross.StatusCode);
        using var outsider = app.CreateClient(); await RegisterAndLogin(outsider);
        using var outsiderRead = await outsider.GetAsync($"/cards/{card}/labels", ct);
        Assert.Equal(HttpStatusCode.NotFound, outsiderRead.StatusCode);
        using var invalidCursor = await owner.GetAsync($"/cards/{card}/labels?after=invalid", ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalidCursor.StatusCode);
        using var denied = await Mutate(outsider, HttpMethod.Put, $"/cards/{card}/labels/{label}?version=1", new { });
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode); Assert.DoesNotContain("Private", await denied.Content.ReadAsStringAsync(ct));
        var key = Guid.NewGuid().ToString(); var path = $"/cards/{card}/labels/{label}?version=1";
        using var assigned = await Mutate(owner, HttpMethod.Put, path, new { }, key);
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);
        using var archived = await Mutate(owner, HttpMethod.Post, $"/lists/{list}/archive", new { version = 1 });
        Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        using var unavailable = await Mutate(owner, HttpMethod.Put, path, new { }, key);
        Assert.Equal(HttpStatusCode.NotFound, unavailable.StatusCode);
        using var archivedRead = await owner.GetAsync($"/cards/{card}/labels", ct);
        Assert.Equal(HttpStatusCode.NotFound, archivedRead.StatusCode);
    }
}
