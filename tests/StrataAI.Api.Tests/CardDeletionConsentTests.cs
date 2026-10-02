using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task Card_deletion_requires_explicit_consent_and_recovers_only_current_authorized_receipts()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var board = await TelemetryBoard(owner, ct);
        using var createdList = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/lists", new { name = "Card parent" });
        var list = (await createdList.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var created = await Mutate(owner, HttpMethod.Post, $"/lists/{list}/cards", new { title = "Delete reviewed Card" });
        var card = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var activeDelete = await Mutate(owner, HttpMethod.Delete, $"/cards/{card}?version=1&confirmed=true", new { });
        Assert.Equal(HttpStatusCode.BadRequest, activeDelete.StatusCode);
        using var archived = await Mutate(owner, HttpMethod.Post, $"/cards/{card}/archive", new { version = 1 });
        Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        foreach (var query in new[] { "", "&confirmed=false" })
        {
            using var rejected = await Mutate(owner, HttpMethod.Delete, $"/cards/{card}?version=2{query}", new { });
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            Assert.Equal("delete_confirmation_required", (await rejected.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
            var page = await owner.GetFromJsonAsync<JsonElement>($"/boards/{board}/archived-cards", ct);
            Assert.Equal(2, Assert.Single(page.GetProperty("items").EnumerateArray()).GetProperty("card").GetProperty("version").GetInt64());
        }
        var key = Guid.NewGuid().ToString(); var path = $"/cards/{card}?version=2&confirmed=true";
        using var deleted = await Mutate(owner, HttpMethod.Delete, path, new { }, key);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode); var receipt = await deleted.Content.ReadAsStringAsync(ct);
        var value = JsonSerializer.Deserialize<JsonElement>(receipt);
        Assert.Equal("deleted", value.GetProperty("lifecycleState").GetString()); Assert.Equal(3, value.GetProperty("version").GetInt64());
        using var replay = await Mutate(owner, HttpMethod.Delete, path, new { }, key);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode); Assert.Equal(receipt, await replay.Content.ReadAsStringAsync(ct));
        using var changed = await Mutate(owner, HttpMethod.Delete, $"/cards/{card}?version=2&confirmed=false", new { }, key);
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        Assert.Equal("idempotency_key_reused", (await changed.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
        using var restore = await Mutate(owner, HttpMethod.Post, $"/cards/{card}/restore", new { version = 3 });
        Assert.Equal(HttpStatusCode.NotFound, restore.StatusCode);
        using var outsider = app.CreateClient(); await RegisterAndLogin(outsider);
        using var denied = await Mutate(outsider, HttpMethod.Delete, path, new { }, key);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode); Assert.DoesNotContain("Delete reviewed Card", await denied.Content.ReadAsStringAsync(ct));
        using var archivedBoard = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/archive", new { version = 1 });
        Assert.Equal(HttpStatusCode.OK, archivedBoard.StatusCode);
        using var inactive = await Mutate(owner, HttpMethod.Delete, path, new { }, key);
        Assert.Equal(HttpStatusCode.NotFound, inactive.StatusCode);
        using var restoredBoard = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/restore", new { version = 2 });
        Assert.Equal(HttpStatusCode.OK, restoredBoard.StatusCode);
        using var recovered = await Mutate(owner, HttpMethod.Delete, path, new { }, key);
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode); Assert.Equal(receipt, await recovered.Content.ReadAsStringAsync(ct));
        using var archivedList = await Mutate(owner, HttpMethod.Post, $"/lists/{list}/archive", new { version = 1 });
        Assert.Equal(HttpStatusCode.OK, archivedList.StatusCode);
        using var deletedList = await Mutate(owner, HttpMethod.Delete, $"/lists/{list}?version=2&confirmed=true&containedCardCount=0", new { });
        Assert.Equal(HttpStatusCode.OK, deletedList.StatusCode);
        using var parentGone = await Mutate(owner, HttpMethod.Delete, path, new { }, key);
        Assert.Equal(HttpStatusCode.NotFound, parentGone.StatusCode);
        var empty = await owner.GetFromJsonAsync<JsonElement>($"/boards/{board}/archived-cards", ct);
        Assert.Empty(empty.GetProperty("items").EnumerateArray());
    }
}
