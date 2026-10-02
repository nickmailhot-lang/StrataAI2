using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task List_copy_preserves_Card_content_order_and_archive_state_with_new_identifiers_and_one_retry_result()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var board = await TelemetryBoard(owner, ct);
        using var sourceResponse = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/lists", new { name = "Source" });
        Assert.Equal(HttpStatusCode.Created, sourceResponse.StatusCode);
        var sourceValue = await sourceResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var source = sourceValue.GetProperty("id").GetGuid();
        var sourceCards = new List<JsonElement>();
        foreach (var title in new[] { "Active one", "Archived two", "Deleted three" })
        {
            using var created = await Mutate(owner, HttpMethod.Post, $"/lists/{source}/cards", new { title, description = "Private description" });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            sourceCards.Add(await created.Content.ReadFromJsonAsync<JsonElement>(ct));
        }
        var archivedId = sourceCards[1].GetProperty("id").GetGuid(); var deletedId = sourceCards[2].GetProperty("id").GetGuid();
        using var labelResponse = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/labels", new { name = "Copied label", color = "blue" });
        Assert.Equal(HttpStatusCode.Created, labelResponse.StatusCode);
        var labelId = (await labelResponse.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var assignment = await Mutate(owner, HttpMethod.Put, $"/cards/{sourceCards[0].GetProperty("id").GetGuid()}/labels/{labelId}?version=1", new { });
        Assert.Equal(HttpStatusCode.OK, assignment.StatusCode);
        using var archived = await Mutate(owner, HttpMethod.Post, $"/cards/{archivedId}/archive", new { version = 1 });
        Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        using var archivedDeleted = await Mutate(owner, HttpMethod.Post, $"/cards/{deletedId}/archive", new { version = 1 });
        Assert.Equal(HttpStatusCode.OK, archivedDeleted.StatusCode);
        using var deleted = await Mutate(owner, HttpMethod.Delete, $"/cards/{deletedId}?version=2&confirmed=true", new { });
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        var key = Guid.NewGuid().ToString(); var body = new { destinationBoardId = board, name = "  Copied  ", version = 1 };
        using var copiedResponse = await Mutate(owner, HttpMethod.Post, $"/lists/{source}/copy", body, key);
        Assert.Equal(HttpStatusCode.Created, copiedResponse.StatusCode);
        var receipt = await copiedResponse.Content.ReadAsStringAsync(ct); var copied = JsonSerializer.Deserialize<JsonElement>(receipt);
        var copyId = copied.GetProperty("id").GetGuid(); Assert.NotEqual(source, copyId);
        Assert.Equal("Copied", copied.GetProperty("name").GetString()); Assert.Equal(1, copied.GetProperty("version").GetInt64());
        Assert.NotEqual(sourceValue.GetProperty("rank").GetString(), copied.GetProperty("rank").GetString());
        using var renamed = await Mutate(owner, HttpMethod.Patch, $"/lists/{source}", new { name = "Changed source", version = 1 });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        using var replay = await Mutate(owner, HttpMethod.Post, $"/lists/{source}/copy", body, key);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode); Assert.Equal(receipt, await replay.Content.ReadAsStringAsync(ct));
        using var changed = await Mutate(owner, HttpMethod.Post, $"/lists/{source}/copy", new { destinationBoardId = board, name = "Different", version = 1 }, key);
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        using var stale = await Mutate(owner, HttpMethod.Post, $"/lists/{source}/copy", body);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var snapshot = await owner.GetFromJsonAsync<JsonElement>($"/boards/{board}", ct);
        Assert.Equal(2, snapshot.GetProperty("lists").GetArrayLength());
        var activeCopy = Assert.Single(snapshot.GetProperty("lists").EnumerateArray(), item => item.GetProperty("list").GetProperty("id").GetGuid() == copyId);
        var activeCard = Assert.Single(activeCopy.GetProperty("cards").EnumerateArray());
        Assert.Equal("Active one", activeCard.GetProperty("title").GetString()); Assert.Equal("Private description", activeCard.GetProperty("description").GetString());
        Assert.Equal(sourceCards[0].GetProperty("rank").GetString(), activeCard.GetProperty("rank").GetString());
        Assert.DoesNotContain(activeCard.GetProperty("id").GetGuid(), sourceCards.Select(item => item.GetProperty("id").GetGuid()));
        var copiedLabels = await owner.GetFromJsonAsync<JsonElement>($"/cards/{activeCard.GetProperty("id").GetGuid()}/labels", ct);
        Assert.Equal(labelId, Assert.Single(copiedLabels.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        var archivePage = await owner.GetFromJsonAsync<JsonElement>($"/boards/{board}/archived-cards", ct);
        var archivedCopy = Assert.Single(archivePage.GetProperty("items").EnumerateArray(), item => item.GetProperty("list").GetProperty("id").GetGuid() == copyId).GetProperty("card");
        Assert.Equal("Archived two", archivedCopy.GetProperty("title").GetString()); Assert.Equal(1, archivedCopy.GetProperty("version").GetInt64());
        Assert.Equal(sourceCards[1].GetProperty("rank").GetString(), archivedCopy.GetProperty("rank").GetString());
        using var outsider = app.CreateClient(); await RegisterAndLogin(outsider);
        using var denied = await Mutate(outsider, HttpMethod.Post, $"/lists/{source}/copy", body, key);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode); Assert.DoesNotContain("Copied", await denied.Content.ReadAsStringAsync(ct));
        using var archivedSource = await Mutate(owner, HttpMethod.Post, $"/lists/{source}/archive", new { version = 2 });
        Assert.Equal(HttpStatusCode.OK, archivedSource.StatusCode);
        using var unavailableReplay = await Mutate(owner, HttpMethod.Post, $"/lists/{source}/copy", body, key);
        Assert.Equal(HttpStatusCode.NotFound, unavailableReplay.StatusCode);
    }

    [Fact]
    public async Task List_copy_rejects_cross_Organization_destination_before_any_copy()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var sourceBoard = await TelemetryBoard(owner, ct); var otherOrganizationBoard = await TelemetryBoard(owner, ct);
        using var created = await Mutate(owner, HttpMethod.Post, $"/boards/{sourceBoard}/lists", new { name = "Private source" });
        var list = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var copy = await Mutate(owner, HttpMethod.Post, $"/lists/{list}/copy", new { destinationBoardId = otherOrganizationBoard, name = "Copy", version = 1 });
        Assert.Equal(HttpStatusCode.NotFound, copy.StatusCode);
        var snapshot = await owner.GetFromJsonAsync<JsonElement>($"/boards/{otherOrganizationBoard}", ct);
        Assert.Empty(snapshot.GetProperty("lists").EnumerateArray());
    }

    [Fact]
    public async Task Cross_Board_List_copy_creates_independent_label_definitions_and_preserves_distinct_identical_labels()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var sourceBoard = await TelemetryBoard(owner, ct);
        var sourceSnapshot = await owner.GetFromJsonAsync<JsonElement>($"/boards/{sourceBoard}", ct);
        var org = sourceSnapshot.GetProperty("board").GetProperty("organizationId").GetGuid();
        using var destinationResponse = await Mutate(owner, HttpMethod.Post, "/boards", new { organizationId = org, name = "Destination", visibility = "PRIVATE" });
        var destination = (await destinationResponse.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var listResponse = await Mutate(owner, HttpMethod.Post, $"/boards/{sourceBoard}/lists", new { name = "Source" });
        var list = (await listResponse.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var cardResponse = await Mutate(owner, HttpMethod.Post, $"/lists/{list}/cards", new { title = "Labeled Card" });
        var card = (await cardResponse.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        var labels = new List<Guid>();
        for (var i = 0; i < 2; i++)
        {
            using var labelResponse = await Mutate(owner, HttpMethod.Post, $"/boards/{sourceBoard}/labels", new { name = "Same name", color = "red" });
            var label = (await labelResponse.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid(); labels.Add(label);
            using var assigned = await Mutate(owner, HttpMethod.Put, $"/cards/{card}/labels/{label}?version={i + 1}", new { });
            Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);
        }
        var key = Guid.NewGuid().ToString(); var body = new { destinationBoardId = destination, name = "Copy", version = 1 };
        using var copy = await Mutate(owner, HttpMethod.Post, $"/lists/{list}/copy", body, key);
        Assert.Equal(HttpStatusCode.Created, copy.StatusCode);
        var snapshot = await owner.GetFromJsonAsync<JsonElement>($"/boards/{destination}", ct);
        var copiedCard = Assert.Single(Assert.Single(snapshot.GetProperty("lists").EnumerateArray()).GetProperty("cards").EnumerateArray());
        var page = await owner.GetFromJsonAsync<JsonElement>($"/cards/{copiedCard.GetProperty("id").GetGuid()}/labels", ct);
        var copiedLabels = page.GetProperty("items").EnumerateArray().ToArray(); Assert.Equal(2, copiedLabels.Length);
        Assert.Equal(1, page.GetProperty("cardVersion").GetInt64());
        foreach (var label in copiedLabels)
        {
            Assert.DoesNotContain(label.GetProperty("id").GetGuid(), labels);
            Assert.Equal(destination, label.GetProperty("boardId").GetGuid()); Assert.Equal("Same name", label.GetProperty("name").GetString());
            Assert.Equal("red", label.GetProperty("color").GetString()); Assert.Equal(1, label.GetProperty("version").GetInt64());
        }
        using var replay = await Mutate(owner, HttpMethod.Post, $"/lists/{list}/copy", body, key);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        var directory = await owner.GetFromJsonAsync<JsonElement>($"/boards/{destination}/labels", ct);
        Assert.Equal(2, directory.GetProperty("items").GetArrayLength());
        using var renameSource = await Mutate(owner, HttpMethod.Patch, $"/labels/{labels[0]}", new { name = "Changed source", color = "blue", version = 1 });
        Assert.Equal(HttpStatusCode.OK, renameSource.StatusCode);
        var unchanged = await owner.GetFromJsonAsync<JsonElement>($"/boards/{destination}/labels", ct);
        Assert.All(unchanged.GetProperty("items").EnumerateArray(), label => Assert.Equal("Same name", label.GetProperty("name").GetString()));
    }
}
