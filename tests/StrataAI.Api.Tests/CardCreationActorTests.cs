using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task PRD_08_original_Card_creation_actor_preserves_receipt_and_refuses_replacement()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var member = app.CreateClient();
        var actors = await BoardCreationActors(app, owner, member, ct);
        using var boardResponse = await Mutate(member, HttpMethod.Post, "/boards", new { organizationId = actors.Organization, name = "Original Card Board" });
        Assert.Equal(HttpStatusCode.Created, boardResponse.StatusCode);
        var board = (await boardResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("id").GetGuid();
        using var listResponse = await Mutate(member, HttpMethod.Post, $"/boards/{board}/lists", new { name = "Planning" });
        Assert.Equal(HttpStatusCode.Created, listResponse.StatusCode);
        var list = (await listResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("id").GetGuid();
        var path = $"/lists/{list}/cards"; var key = Guid.NewGuid().ToString(); var body = new { title = "Original Card command" };
        using var first = await Mutate(member, HttpMethod.Post, $"{path}?expectedActorId={actors.Member}", body, key);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var id = (await first.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("id").GetGuid();
        using var replaced = await Mutate(owner, HttpMethod.Post, $"{path}?expectedActorId={actors.Member}", body, key);
        Assert.Equal(HttpStatusCode.Unauthorized, replaced.StatusCode);
        Assert.Equal("session_unavailable", (await replaced.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("code").GetString());
        Assert.DoesNotContain(body.title, await replaced.Content.ReadAsStringAsync(ct));
        foreach (var replayPath in new[] { path, $"{path}?expectedActorId={actors.Member}" })
        {
            using var replay = await Mutate(member, HttpMethod.Post, replayPath, body, key);
            Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
            Assert.Equal(id, (await replay.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("id").GetGuid());
        }
        var unchanged = await member.GetFromJsonAsync<JsonElement>($"/boards/{board}", ct);
        var only = Assert.Single(unchanged.GetProperty("lists")[0].GetProperty("cards").EnumerateArray());
        Assert.Equal(id, only.GetProperty("id").GetGuid()); Assert.Equal(body.title, only.GetProperty("title").GetString());
        Assert.False(string.IsNullOrWhiteSpace(only.GetProperty("rank").GetString()));
        // The refused command did not consume this actor's independent receipt.
        using var fresh = await Mutate(owner, HttpMethod.Post, $"{path}?expectedActorId={actors.Owner}", body, key);
        Assert.Equal(HttpStatusCode.Created, fresh.StatusCode);
        Assert.NotEqual(id, (await fresh.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("id").GetGuid());
    }

    [Theory]
    [InlineData("other", HttpStatusCode.Unauthorized)]
    [InlineData("empty", HttpStatusCode.Unauthorized)]
    [InlineData("malformed", HttpStatusCode.BadRequest)]
    [InlineData("multiple", HttpStatusCode.BadRequest)]
    public async Task PRD_08_creation_expected_actor_rejects_before_Card_mutation(string scenario, HttpStatusCode status)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var member = app.CreateClient();
        var actors = await BoardCreationActors(app, owner, member, ct);
        using var boardResponse = await Mutate(member, HttpMethod.Post, "/boards", new { organizationId = actors.Organization, name = "No rejected Card" });
        var board = (await boardResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("id").GetGuid();
        using var listResponse = await Mutate(member, HttpMethod.Post, $"/boards/{board}/lists", new { name = "Planning" });
        var list = (await listResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("id").GetGuid();
        var expected = scenario switch { "other" => actors.Owner.ToString(), "empty" => Guid.Empty.ToString(), "multiple" => $"{actors.Owner}&expectedActorId={actors.Member}", _ => "not-a-guid" };
        var before = await member.GetFromJsonAsync<JsonElement>($"/boards/{board}", ct);
        var key = Guid.NewGuid().ToString(); var body = new { title = "Rejected Card" };
        using var refused = await Mutate(member, HttpMethod.Post, $"/lists/{list}/cards?expectedActorId={expected}", body, key);
        Assert.Equal(status, refused.StatusCode);
        var after = await member.GetFromJsonAsync<JsonElement>($"/boards/{board}", ct);
        Assert.Equal(before.GetRawText(), after.GetRawText());
        using var permitted = await Mutate(member, HttpMethod.Post, $"/lists/{list}/cards?expectedActorId={actors.Member}", body, key);
        Assert.Equal(HttpStatusCode.Created, permitted.StatusCode);
    }

    [Fact]
    public async Task PRD_08_creation_without_expected_actor_preserves_legacy_Card_receipt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var actor = (await owner.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        using var orgResponse = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Legacy Card Organization" });
        var org = (await orgResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("organization").GetProperty("id").GetGuid();
        using var boardResponse = await Mutate(owner, HttpMethod.Post, "/boards", new { organizationId = org, name = "Legacy Card Board" });
        var board = (await boardResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("id").GetGuid();
        using var listResponse = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/lists", new { name = "Planning" });
        var list = (await listResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("id").GetGuid();
        var path = $"/lists/{list}/cards"; var body = new { title = "Legacy Card" }; var key = Guid.NewGuid().ToString();
        using var first = await Mutate(owner, HttpMethod.Post, path, body, key); Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var id = (await first.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("id").GetGuid();
        using var bound = await Mutate(owner, HttpMethod.Post, $"{path}?expectedActorId={actor}", body, key);
        Assert.Equal(HttpStatusCode.Created, bound.StatusCode);
        Assert.Equal(id, (await bound.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("id").GetGuid());
        var canonical = await owner.GetFromJsonAsync<JsonElement>($"/boards/{board}", ct);
        Assert.Single(canonical.GetProperty("lists")[0].GetProperty("cards").EnumerateArray());
    }
}
