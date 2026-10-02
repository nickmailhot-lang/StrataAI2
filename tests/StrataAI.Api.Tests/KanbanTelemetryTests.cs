using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Api.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task Kanban_lifecycle_metrics_cover_consent_conflicts_retries_and_archive_reads_without_private_labels()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var outsider = app.CreateClient();
        await RegisterAndLogin(owner); await RegisterAndLogin(outsider);
        var board = await TelemetryBoard(owner, ct);
        using var capture = new SharingMetricCapture(app.Services.GetRequiredService<BoardSharingTelemetry>().Meter);
        using var listResponse = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/lists", new { name = "Private lifecycle parent" });
        Assert.Equal(HttpStatusCode.Created, listResponse.StatusCode);
        var list = (await listResponse.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var cardResponse = await Mutate(owner, HttpMethod.Post, $"/lists/{list}/cards", new { title = "Private lifecycle Card" });
        Assert.Equal(HttpStatusCode.Created, cardResponse.StatusCode);
        var card = (await cardResponse.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var edited = await Mutate(owner, HttpMethod.Patch, $"/cards/{card}", new { title = "Private revised title", version = 1 });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var key = Guid.NewGuid().ToString();
        using var archived = await Mutate(owner, HttpMethod.Post, $"/cards/{card}/archive", new { version = 2 }, key);
        Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        using var replay = await Mutate(owner, HttpMethod.Post, $"/cards/{card}/archive", new { version = 2 }, key);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        using var restored = await Mutate(owner, HttpMethod.Post, $"/cards/{card}/restore", new { version = 3 });
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        using var archivedAgain = await Mutate(owner, HttpMethod.Post, $"/cards/{card}/archive", new { version = 4 });
        Assert.Equal(HttpStatusCode.OK, archivedAgain.StatusCode);
        using var unconfirmed = await Mutate(owner, HttpMethod.Delete, $"/cards/{card}?version=5", new { });
        Assert.Equal(HttpStatusCode.BadRequest, unconfirmed.StatusCode);
        using var deleted = await Mutate(owner, HttpMethod.Delete, $"/cards/{card}?version=5&confirmed=true", new { });
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        using var listArchive = await Mutate(owner, HttpMethod.Post, $"/lists/{list}/archive", new { version = 1 });
        Assert.Equal(HttpStatusCode.OK, listArchive.StatusCode);
        using var listRestore = await Mutate(owner, HttpMethod.Post, $"/lists/{list}/restore", new { version = 2 });
        Assert.Equal(HttpStatusCode.OK, listRestore.StatusCode);
        using var listArchiveAgain = await Mutate(owner, HttpMethod.Post, $"/lists/{list}/archive", new { version = 3 });
        Assert.Equal(HttpStatusCode.OK, listArchiveAgain.StatusCode);
        using var impactChanged = await Mutate(owner, HttpMethod.Delete, $"/lists/{list}?version=4&confirmed=true&containedCardCount=1", new { });
        Assert.Equal(HttpStatusCode.Conflict, impactChanged.StatusCode);
        using var listDelete = await Mutate(owner, HttpMethod.Delete, $"/lists/{list}?version=4&confirmed=true&containedCardCount=0", new { });
        Assert.Equal(HttpStatusCode.OK, listDelete.StatusCode);
        using var lists = await owner.GetAsync($"/boards/{board}/archived-lists", ct);
        Assert.Equal(HttpStatusCode.OK, lists.StatusCode);
        using var cards = await owner.GetAsync($"/boards/{board}/archived-cards?after=private-invalid-input", ct);
        Assert.Equal(HttpStatusCode.BadRequest, cards.StatusCode);
        using var denied = await outsider.GetAsync($"/boards/{board}/archived-cards", ct);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        var samples = await capture.WaitAsync(17, ct); AssertSafeSharingSamples(samples, 17);
        foreach (var operation in new[] { "list_create", "card_create", "card_update", "card_archive", "card_restore",
            "card_delete", "list_archive", "list_restore", "list_delete", "archived_list_read", "archived_card_read" })
            Assert.Contains(samples, sample => sample.Tags["operation"] as string == operation);
        Assert.Contains(samples, sample => sample.Tags["operation"] as string == "card_delete" && sample.Tags["error_code"] as string == "delete_confirmation_required");
        Assert.Contains(samples, sample => sample.Tags["operation"] as string == "list_delete" && sample.Tags["error_code"] as string == "deletion_impact_changed");
        Assert.Contains(samples, sample => sample.Tags["error_code"] as string == "invalid_archive_cursor");
        Assert.Contains(samples, sample => sample.Tags["operation"] as string == "archived_card_read" && sample.Tags["outcome"] as string == "denied");
        Assert.Equal(3, samples.Count(sample => sample.Name == "strataai.board_sharing.requests"
            && sample.Tags["operation"] as string == "card_archive" && sample.Tags["outcome"] as string == "success"));
    }

    [Fact]
    public async Task Kanban_metrics_measure_moves_conflicts_and_denials_without_private_labels()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var outsider = app.CreateClient();
        await RegisterAndLogin(owner); await RegisterAndLogin(outsider);
        var board = await TelemetryBoard(owner, ct);
        async Task<Guid> CreateList(string name)
        {
            using var response = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/lists", new { name });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        }
        var source = await CreateList("Private source title"); var destination = await CreateList("Private destination title");
        using var created = await Mutate(owner, HttpMethod.Post, $"/lists/{source}/cards", new { title = "Private card title" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var card = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var capture = new SharingMetricCapture(app.Services.GetRequiredService<BoardSharingTelemetry>().Meter);
        var key = Guid.NewGuid().ToString(); var command = new { destinationListId = destination, expectedVersion = 1 };
        using var moved = await Mutate(owner, HttpMethod.Post, $"/cards/{card}/move", command, key);
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        using var recovered = await Mutate(owner, HttpMethod.Post, $"/cards/{card}/move", command, key);
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        using var stale = await Mutate(owner, HttpMethod.Post, $"/cards/{card}/move", command);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var denied = await Mutate(outsider, HttpMethod.Post, $"/cards/{card}/move", new { destinationListId = source, expectedVersion = 2 });
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        using var reordered = await Mutate(owner, HttpMethod.Patch, $"/lists/{source}", new { name = "Private source title", version = 1, moveToEnd = true });
        Assert.Equal(HttpStatusCode.OK, reordered.StatusCode);
        using var invalid = await Mutate(owner, HttpMethod.Patch, $"/lists/{source}", new { name = "Private source title", version = 2, beforeListId = source });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var samples = await capture.WaitAsync(6, ct); AssertSafeSharingSamples(samples, 6);
        Assert.Equal(2, samples.Count(sample => sample.Name == "strataai.board_sharing.requests"
            && sample.Tags["operation"] as string == "card_move" && sample.Tags["outcome"] as string == "success" && sample.Tags["keyed_attempt"] is true));
        Assert.Contains(samples, sample => sample.Tags["operation"] as string == "card_move" && sample.Tags["error_code"] as string == "version_conflict");
        Assert.Contains(samples, sample => sample.Tags["operation"] as string == "card_move" && sample.Tags["error_code"] as string == "card_not_found");
        Assert.Contains(samples, sample => sample.Tags["operation"] as string == "list_update" && sample.Tags["error_code"] as string == "invalid_move_position");
    }
}
