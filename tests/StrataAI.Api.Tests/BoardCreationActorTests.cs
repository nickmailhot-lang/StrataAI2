using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Organizations;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    // PRD-04-VS-05/AC-05, PERM-FR-010: a current member is not the
    // original actor merely because it can independently create Boards.
    [Fact]
    public async Task PRD_04_original_Board_creation_actor_survives_account_replacement_and_legacy_replay()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory();
        using var owner = app.CreateClient();
        using var member = app.CreateClient();
        var actors = await BoardCreationActors(app, owner, member, ct);
        var key = Guid.NewGuid().ToString();
        var body = new { organizationId = actors.Organization, name = "Original account Board", visibility = "PRIVATE" };
        using var original = await Mutate(owner, HttpMethod.Post, $"/boards?expectedActorId={actors.Owner}", body, key);
        Assert.Equal(HttpStatusCode.Created, original.StatusCode);
        var id = (await original.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("id").GetGuid();
        using var replaced = await Mutate(member, HttpMethod.Post, $"/boards?expectedActorId={actors.Owner}", body, key);
        Assert.Equal(HttpStatusCode.Unauthorized, replaced.StatusCode);
        Assert.Equal("session_unavailable", (await replaced.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("code").GetString());
        Assert.DoesNotContain(body.name, await replaced.Content.ReadAsStringAsync(ct));
        var unchanged = await member.GetFromJsonAsync<JsonElement>($"/organizations/{actors.Organization}/boards/directory", ct);
        Assert.Equal(0, unchanged.GetProperty("items").GetArrayLength());
        foreach (var path in new[] { $"/boards?expectedActorId={actors.Owner}", "/boards" })
        {
            using var replay = await Mutate(owner, HttpMethod.Post, path, body, key);
            Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
            Assert.Equal(id, (await replay.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("id").GetGuid());
        }
        // A refusal consumed no replacement-actor receipt. A fresh, explicitly
        // bound authorized actor may use its own independent key namespace.
        using var fresh = await Mutate(member, HttpMethod.Post, $"/boards?expectedActorId={actors.Member}", body, key);
        Assert.Equal(HttpStatusCode.Created, fresh.StatusCode);
        Assert.NotEqual(id, (await fresh.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("id").GetGuid());
        var canonical = await owner.GetFromJsonAsync<JsonElement>($"/boards/{id}", ct);
        Assert.Equal(body.name, canonical.GetProperty("board").GetProperty("name").GetString());
        Assert.Equal(1, canonical.GetProperty("board").GetProperty("version").GetInt64());
    }

    [Theory]
    [InlineData("other", HttpStatusCode.Unauthorized)]
    [InlineData("empty", HttpStatusCode.Unauthorized)]
    [InlineData("malformed", HttpStatusCode.BadRequest)]
    [InlineData("multiple", HttpStatusCode.BadRequest)]
    public async Task PRD_04_creation_expected_actor_refusal_precedes_graph_mutation(string scenario, HttpStatusCode status)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory();
        using var owner = app.CreateClient();
        using var member = app.CreateClient();
        var actors = await BoardCreationActors(app, owner, member, ct);
        var expected = scenario switch
        {
            "other" => actors.Owner.ToString(),
            "empty" => Guid.Empty.ToString(),
            "multiple" => $"{actors.Owner}&expectedActorId={actors.Member}",
            _ => "not-a-guid",
        };
        var before = await member.GetFromJsonAsync<JsonElement>($"/organizations/{actors.Organization}/boards/directory", ct);
        var body = new { organizationId = actors.Organization, name = "No rejected creation" };
        var key = Guid.NewGuid().ToString();
        using var refused = await Mutate(member, HttpMethod.Post, $"/boards?expectedActorId={expected}", body, key);
        Assert.Equal(status, refused.StatusCode);
        if (status == HttpStatusCode.Unauthorized)
            Assert.Equal("session_unavailable", (await refused.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("code").GetString());
        var after = await member.GetFromJsonAsync<JsonElement>($"/organizations/{actors.Organization}/boards/directory", ct);
        Assert.Equal(before.GetRawText(), after.GetRawText());
        using var permitted = await Mutate(member, HttpMethod.Post, $"/boards?expectedActorId={actors.Member}", body, key);
        Assert.Equal(HttpStatusCode.Created, permitted.StatusCode);
    }

    [Fact]
    public async Task PRD_04_creation_without_expected_actor_preserves_legacy_receipt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory();
        using var owner = app.CreateClient();
        await RegisterAndLogin(owner);
        var actor = (await owner.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        using var organization = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Legacy Board Organization" });
        var org = (await organization.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("organization").GetProperty("id").GetGuid();
        var key = Guid.NewGuid().ToString(); var body = new { organizationId = org, name = "Legacy receipt Board" };
        using var first = await Mutate(owner, HttpMethod.Post, "/boards", body, key);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var id = (await first.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("id").GetGuid();
        using var bound = await Mutate(owner, HttpMethod.Post, $"/boards?expectedActorId={actor}", body, key);
        Assert.Equal(HttpStatusCode.Created, bound.StatusCode);
        Assert.Equal(id, (await bound.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("id").GetGuid());
        var directory = await owner.GetFromJsonAsync<JsonElement>($"/organizations/{org}/boards/directory", ct);
        Assert.Equal(1, directory.GetProperty("items").GetArrayLength());
    }

    private static async Task<(Guid Organization, Guid Owner, Guid Member)> BoardCreationActors(
        ApiFactory app, HttpClient owner, HttpClient member, CancellationToken ct)
    {
        await RegisterAndLogin(owner); await RegisterAndLogin(member);
        var ownerId = (await owner.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        var memberId = (await member.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        using var created = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Account-bound Board Organization" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var org = (await created.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("organization").GetProperty("id").GetGuid();
        await app.Services.GetRequiredService<IOrganizationStore>().AddOrRestoreMemberAsync(org, memberId, OrganizationRole.Member, DateTimeOffset.UtcNow, ct);
        return (org, ownerId, memberId);
    }
}
