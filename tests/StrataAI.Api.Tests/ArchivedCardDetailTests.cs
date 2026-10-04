using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace StrataAI.Api.Tests;
public sealed partial class ApiHostTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Archived_Card_details_admit_current_members_and_never_deleted_content(bool archiveList)
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var member = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct);
        using var created = await Mutate(owner, HttpMethod.Post, $"/lists/{f.List}/cards", new { title = "Archived <script>🙂" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var card = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        var path = $"/boards/{f.Board}/cards/{card}/archived-details";
        using var active = await member.GetAsync(path, ct); Assert.Equal(HttpStatusCode.NotFound, active.StatusCode);
        using var archived = await Mutate(owner, HttpMethod.Post, archiveList ? $"/lists/{f.List}/archive" : $"/cards/{card}/archive", new { version = 1 });
        Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        using var read = await member.GetAsync(path, ct); Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Contains("no-store", read.Headers.CacheControl!.ToString());
        var detail = await read.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(card, detail.GetProperty("cardId").GetGuid()); Assert.Equal(f.Board, detail.GetProperty("boardId").GetGuid());
        Assert.Equal("Archived <script>🙂", detail.GetProperty("title").GetString());
        using var wrongBoard = await member.GetAsync($"/boards/{Guid.NewGuid()}/cards/{card}/archived-details", ct);
        Assert.Equal(HttpStatusCode.NotFound, wrongBoard.StatusCode);
        using var removed = await Mutate(owner, HttpMethod.Delete, $"/boards/{f.Board}/members/{f.Recipient}", new { });
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        using var revoked = await member.GetAsync(path, ct); Assert.Equal(HttpStatusCode.NotFound, revoked.StatusCode);
        if (!archiveList)
        {
            using var deleted = await Mutate(owner, HttpMethod.Delete, $"/cards/{card}?version=2&confirmed=true", new { });
            Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
            using var tombstone = await owner.GetAsync(path, ct); Assert.Equal(HttpStatusCode.NotFound, tombstone.StatusCode);
            Assert.DoesNotContain("Archived <script>", await tombstone.Content.ReadAsStringAsync(ct));
        }
    }
}
