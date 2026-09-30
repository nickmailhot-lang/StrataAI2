using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task Independent_default_creates_allocate_unique_ranks_and_skip_archived_siblings()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory();
        using var owner = app.CreateClient();
        await RegisterAndLogin(owner);
        using var orgResponse = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Ranks" });
        var org = (await orgResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct))
            .GetProperty("organization").GetProperty("id").GetGuid();
        using var boardResponse = await Mutate(owner, HttpMethod.Post, "/boards", new { organizationId = org, name = "Ranks" });
        var board = (await boardResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("id").GetGuid();
        async Task<JsonElement[]> CreateMany(string route, object body, int count)
        {
            var responses = await Task.WhenAll(Enumerable.Range(0, count)
                .Select(_ => Mutate(owner, HttpMethod.Post, route, body)));
            var records = new List<JsonElement>();
            foreach (var response in responses)
            {
                using (response)
                {
                    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                    records.Add(await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct));
                }
            }
            Assert.Equal(count, records.Select(record => record.GetProperty("rank").GetString()).Distinct().Count());
            return records.ToArray();
        }
        var lists = await CreateMany($"/boards/{board}/lists", new { name = "List" }, 200);
        var list = lists[0].GetProperty("id").GetGuid();
        var cards = await CreateMany($"/lists/{list}/cards", new { title = "Card" }, 128);
        var last = cards.OrderBy(card => card.GetProperty("rank").GetString(), StringComparer.Ordinal).Last();
        using var archived = await Mutate(owner, HttpMethod.Post,
            $"/cards/{last.GetProperty("id").GetGuid()}/archive", new { version = 1 });
        Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        using var replacement = await Mutate(owner, HttpMethod.Post, $"/lists/{list}/cards", new { title = "Replacement" });
        Assert.Equal(HttpStatusCode.Created, replacement.StatusCode);
        var created = await replacement.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        Assert.Equal(last.GetProperty("rank").GetString(), created.GetProperty("rank").GetString());
        var exhaustedList = lists[1].GetProperty("id").GetGuid();
        using var edge = await Mutate(owner, HttpMethod.Post, $"/lists/{exhaustedList}/cards",
            new { title = "Legacy edge", rank = "999999999999999999999999999998" });
        Assert.Equal(HttpStatusCode.Created, edge.StatusCode);
        using var exhausted = await Mutate(owner, HttpMethod.Post, $"/lists/{exhaustedList}/cards", new { title = "No space" });
        Assert.Equal(HttpStatusCode.Conflict, exhausted.StatusCode);
        Assert.Equal("rank_space_exhausted", (await exhausted.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct))
            .GetProperty("code").GetString());
    }
}
