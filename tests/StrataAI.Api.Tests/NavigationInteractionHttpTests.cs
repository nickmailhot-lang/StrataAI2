using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task PRD_01_HTTP_navigation_checks_target_revision_and_current_access_on_replay()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var member = app.CreateClient(); using var outsider = app.CreateClient();
        await RegisterAndLogin(owner); await RegisterAndLogin(member); await RegisterAndLogin(outsider);
        var actor = (await member.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        var other = (await outsider.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        using var organizationResponse = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Navigation HTTP scope" });
        var organizationId = (await organizationResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct))
            .GetProperty("organization").GetProperty("id").GetGuid();
        var organizations = app.Services.GetRequiredService<IOrganizationStore>();
        await organizations.AddOrRestoreMemberAsync(organizationId, actor, OrganizationRole.Member, DateTimeOffset.UtcNow, ct);
        using var boardResponse = await Mutate(member, HttpMethod.Post, "/boards", new { organizationId, name = "Private navigation target" });
        Assert.Equal(HttpStatusCode.Created, boardResponse.StatusCode);
        var board = await boardResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var boardId = board.GetProperty("id").GetGuid();
        var boardVersion = board.GetProperty("version").GetInt64();
        using var listResponse = await Mutate(member, HttpMethod.Post, $"/boards/{boardId}/lists", new { name = "Navigation list" });
        var listId = (await listResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("id").GetGuid();
        using var cardResponse = await Mutate(member, HttpMethod.Post, $"/lists/{listId}/cards", new { title = "Navigation card" });
        var card = await cardResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var cardId = card.GetProperty("id").GetGuid(); var cardVersion = card.GetProperty("version").GetInt64();
        async Task<HttpResponseMessage> Observe(HttpClient client, Guid expected, string path, string key) {
            using var request = new HttpRequestMessage(HttpMethod.Post, path);
            request.Headers.Add("X-StrataAI-Request", "1");
            request.Headers.Add("X-StrataAI-Expected-Actor", expected.ToString("D"));
            request.Headers.Add("Idempotency-Key", key);
            return await client.SendAsync(request, ct);
        }
        var boardPath = $"/navigation/observations?kind=board&organizationId={organizationId}&boardId={boardId}&version={boardVersion}";
        var cardPath = $"/navigation/observations?kind=card&organizationId={organizationId}&boardId={boardId}&cardId={cardId}&version={cardVersion}";
        var boardKey = Guid.NewGuid().ToString("D"); var cardKey = Guid.NewGuid().ToString("D");
        using var openedBoard = await Observe(member, actor, boardPath, boardKey);
        using var openedCard = await Observe(member, actor, cardPath, cardKey);
        Assert.Equal(HttpStatusCode.OK, openedBoard.StatusCode); Assert.Equal(HttpStatusCode.OK, openedCard.StatusCode);
        var originalCard = await openedCard.Content.ReadAsStringAsync(ct);
        using var cardEvent = JsonDocument.Parse(originalCard);
        Assert.Equal("CARD_OPENED", cardEvent.RootElement.GetProperty("eventType").GetString());
        Assert.Equal(cardId, cardEvent.RootElement.GetProperty("entityId").GetGuid());
        Assert.Equal(10, cardEvent.RootElement.EnumerateObject().Count());
        using var edited = await Mutate(member, HttpMethod.Patch, $"/cards/{cardId}", new { title = "Later navigation Card", version = cardVersion });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        using var repeated = await Observe(member, actor, cardPath, cardKey);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(originalCard, await repeated.Content.ReadAsStringAsync(ct));
        using var stale = await Observe(member, actor, cardPath.Replace($"version={cardVersion}", $"version={cardVersion + 2}"), Guid.NewGuid().ToString("D"));
        Assert.Equal(HttpStatusCode.NotFound, stale.StatusCode);
        using var inaccessible = await Observe(outsider, other, boardPath, Guid.NewGuid().ToString("D"));
        Assert.Equal(HttpStatusCode.NotFound, inaccessible.StatusCode);
        var store = app.Services.GetRequiredService<IWorkManagementStore>();
        Assert.Equal(cardVersion + 1, (await store.FindCardAsync(cardId, ct))!.Version);
        await store.RemoveBoardMemberAsync(boardId, actor, DateTimeOffset.UtcNow, ct);
        using var revokedBoard = await Observe(member, actor, boardPath, boardKey);
        using var revokedCard = await Observe(member, actor, cardPath, cardKey);
        Assert.Equal(HttpStatusCode.NotFound, revokedBoard.StatusCode); Assert.Equal(HttpStatusCode.NotFound, revokedCard.StatusCode);
        Assert.DoesNotContain("eventId", await revokedCard.Content.ReadAsStringAsync(ct));
    }

    [Fact]
    public async Task PRD_01_HTTP_navigation_requires_account_binding_empty_body_and_preserves_original_reply()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var client = app.CreateClient();
        await RegisterAndLogin(client);
        var actor = (await client.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        var key = Guid.NewGuid().ToString("D");
        async Task<HttpResponseMessage> Observe(string path, Guid? expected, string? retry, bool body = false) {
            using var request = new HttpRequestMessage(HttpMethod.Post, path);
            request.Headers.Add("X-StrataAI-Request", "1");
            if (expected.HasValue) request.Headers.Add("X-StrataAI-Expected-Actor", expected.Value.ToString("D"));
            if (retry is not null) request.Headers.Add("Idempotency-Key", retry);
            if (body) request.Content = JsonContent.Create(new { });
            return await client.SendAsync(request, ct);
        }
        using var missingActor = await Observe("/navigation/observations?kind=context", null, key);
        Assert.Equal(HttpStatusCode.BadRequest, missingActor.StatusCode);
        using var wrongActor = await Observe("/navigation/observations?kind=context", Guid.NewGuid(), key);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongActor.StatusCode);
        using var withBody = await Observe("/navigation/observations?kind=context", actor, key, true);
        Assert.Equal(HttpStatusCode.BadRequest, withBody.StatusCode);
        using var missingKey = await Observe("/navigation/observations?kind=context", actor, null);
        Assert.Equal(HttpStatusCode.BadRequest, missingKey.StatusCode);
        using var first = await Observe("/navigation/observations?kind=context", actor, key);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Contains("no-store", first.Headers.CacheControl!.ToString());
        var original = await first.Content.ReadAsStringAsync(ct);
        using var repeat = await Observe("/navigation/observations?kind=context", actor, key);
        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode); Assert.Equal(original, await repeat.Content.ReadAsStringAsync(ct));
        using var source = JsonDocument.Parse(original);
        Assert.Equal("APPLICATION_CONTEXT_CHANGED", source.RootElement.GetProperty("eventType").GetString());
        Assert.Equal(actor, source.RootElement.GetProperty("actorId").GetGuid());
        Assert.Empty(source.RootElement.GetProperty("metadata").EnumerateObject());
        using var denied = await Observe($"/navigation/observations?kind=context&organizationId={Guid.NewGuid():D}", actor, key);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
    }
}
