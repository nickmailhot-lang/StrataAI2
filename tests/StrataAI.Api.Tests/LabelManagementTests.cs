using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task Board_labels_validate_version_and_retain_one_receipt_without_exposing_deleted_records()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var board = await TelemetryBoard(owner, ct);
        var key = Guid.NewGuid().ToString(); var body = new { name = "  Important  ", color = " BLUE " };
        using var created = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/labels", body, key);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var receipt = await created.Content.ReadAsStringAsync(ct); var label = JsonSerializer.Deserialize<JsonElement>(receipt);
        var id = label.GetProperty("id").GetGuid(); Assert.NotEqual(Guid.Empty, id);
        Assert.Equal("Important", label.GetProperty("name").GetString()); Assert.Equal("blue", label.GetProperty("color").GetString());
        Assert.Equal(1, label.GetProperty("version").GetInt64());
        using var replay = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/labels", body, key);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode); Assert.Equal(receipt, await replay.Content.ReadAsStringAsync(ct));
        using var changedKey = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/labels", new { name = "Changed", color = "blue" }, key);
        Assert.Equal(HttpStatusCode.Conflict, changedKey.StatusCode);
        using var invalidColor = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/labels", new { name = "", color = "unknown" });
        Assert.Equal(HttpStatusCode.BadRequest, invalidColor.StatusCode);
        using var updated = await Mutate(owner, HttpMethod.Patch, $"/labels/{id}", new { name = "", color = "green", version = 1 });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(2, (await updated.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("version").GetInt64());
        using var stale = await Mutate(owner, HttpMethod.Patch, $"/labels/{id}", new { name = "Stale", color = "red", version = 1 });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var unconfirmed = await Mutate(owner, HttpMethod.Delete, $"/labels/{id}?version=2", new { });
        Assert.Equal(HttpStatusCode.BadRequest, unconfirmed.StatusCode);
        var deleteKey = Guid.NewGuid().ToString();
        using var deleted = await Mutate(owner, HttpMethod.Delete, $"/labels/{id}?version=2&confirmed=true", new { }, deleteKey);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode); var deletionReceipt = await deleted.Content.ReadAsStringAsync(ct);
        using var deleteReplay = await Mutate(owner, HttpMethod.Delete, $"/labels/{id}?version=2&confirmed=true", new { }, deleteKey);
        Assert.Equal(HttpStatusCode.OK, deleteReplay.StatusCode); Assert.Equal(deletionReceipt, await deleteReplay.Content.ReadAsStringAsync(ct));
        var page = await owner.GetFromJsonAsync<JsonElement>($"/boards/{board}/labels", ct);
        Assert.Empty(page.GetProperty("items").EnumerateArray());
        using var editDeleted = await Mutate(owner, HttpMethod.Patch, $"/labels/{id}", new { name = "Resurrect", color = "blue", version = 3 });
        Assert.Equal(HttpStatusCode.NotFound, editDeleted.StatusCode);
    }

    [Fact]
    public async Task Label_receipts_require_current_Board_access_and_active_lifecycle()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var board = await TelemetryBoard(owner, ct); var key = Guid.NewGuid().ToString(); var body = new { name = "Private label", color = "red" };
        using var created = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/labels", body, key);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var outsider = app.CreateClient(); await RegisterAndLogin(outsider);
        using var outsiderPage = await outsider.GetAsync($"/boards/{board}/labels", ct);
        Assert.Equal(HttpStatusCode.NotFound, outsiderPage.StatusCode);
        using var outsiderEdit = await Mutate(outsider, HttpMethod.Patch, $"/labels/{id}", new { name = "Guess", color = "red", version = 1 });
        Assert.Equal(HttpStatusCode.NotFound, outsiderEdit.StatusCode); Assert.DoesNotContain("Private label", await outsiderEdit.Content.ReadAsStringAsync(ct));
        using var invalidCursor = await owner.GetAsync($"/boards/{board}/labels?after=invalid", ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalidCursor.StatusCode);
        using var archive = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/archive", new { version = 1 });
        Assert.Equal(HttpStatusCode.OK, archive.StatusCode);
        using var historicalCreate = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/labels", body, key);
        Assert.Equal(HttpStatusCode.NotFound, historicalCreate.StatusCode);
        using var archivedEdit = await Mutate(owner, HttpMethod.Patch, $"/labels/{id}", new { name = "Changed", color = "blue", version = 1 });
        Assert.Equal(HttpStatusCode.NotFound, archivedEdit.StatusCode);
    }
}
