using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task Board_filter_change_HTTP_returns_private_original_for_retry_without_mutating_work()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var board = await TelemetryBoard(owner, ct);
        var before = await owner.GetFromJsonAsync<JsonElement>($"/boards/{board}", ct);
        var organization = before.GetProperty("board").GetProperty("organizationId").GetGuid();
        var actor = (await owner.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        async Task<HttpResponseMessage> Change(HttpClient client, string query, Guid? key, bool csrf = true)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/boards/{board}/cards/filter-change?{query}");
            if (csrf) request.Headers.Add("X-StrataAI-Request", "1");
            request.Headers.Add("X-StrataAI-Expected-Actor", actor.ToString("D"));
            if (key is { } retry) request.Headers.Add("Idempotency-Key", retry.ToString("D"));
            return await client.SendAsync(request, ct);
        }
        var key = Guid.NewGuid();
        using var first = await Change(owner, "change=apply&keyword=roof", key);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Contains("no-store", first.Headers.CacheControl!.ToString());
        var source = await first.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(new[] { "actorId", "boardId", "createdAt", "entityId", "entityType", "eventId", "eventType", "metadata", "organizationId", "version" },
            source.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(actor, source.GetProperty("actorId").GetGuid()); Assert.Equal(organization, source.GetProperty("organizationId").GetGuid());
        Assert.Equal(board, source.GetProperty("boardId").GetGuid()); Assert.Equal("BOARD_FILTER_CHANGED", source.GetProperty("eventType").GetString());
        Assert.Equal("BoardFilter", source.GetProperty("entityType").GetString()); Assert.Equal(1, source.GetProperty("version").GetInt64());
        Assert.Empty(source.GetProperty("metadata").EnumerateObject()); Assert.Equal(source.GetProperty("eventId").GetGuid(), source.GetProperty("entityId").GetGuid());
        Assert.NotEqual(Guid.Empty, source.GetProperty("eventId").GetGuid()); Assert.NotEqual(default, source.GetProperty("createdAt").GetDateTimeOffset());
        using var retry = await Change(owner, "change=APPLY&keyword=%20roof%20&match=ALL", key);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(source.GetRawText(), (await retry.Content.ReadFromJsonAsync<JsonElement>(ct)).GetRawText());
        using var changed = await Change(owner, "change=apply&keyword=wall", key);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, changed.StatusCode);
        Assert.Equal("work_storage_unavailable", (await changed.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
        using var clear = await Change(owner, "change=clear", Guid.NewGuid());
        Assert.Equal(HttpStatusCode.OK, clear.StatusCode);
        Assert.NotEqual(source.GetProperty("eventId").GetGuid(), (await clear.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("eventId").GetGuid());
        var after = await owner.GetFromJsonAsync<JsonElement>($"/boards/{board}", ct);
        Assert.Equal(before.GetProperty("board").GetRawText(), after.GetProperty("board").GetRawText());
        Assert.Equal(before.GetProperty("lists").GetRawText(), after.GetProperty("lists").GetRawText());
        var read = await owner.GetFromJsonAsync<JsonElement>($"/boards/{board}/cards?keyword=roof", ct);
        Assert.False(read.TryGetProperty("interaction", out _));
        using var missingKey = await Change(owner, "change=apply", null); Assert.Equal(HttpStatusCode.BadRequest, missingKey.StatusCode);
        using var cursor = await Change(owner, "change=apply&after=" + Guid.NewGuid(), Guid.NewGuid()); Assert.Equal(HttpStatusCode.BadRequest, cursor.StatusCode);
        using var nonemptyClear = await Change(owner, "change=clear&keyword=roof", Guid.NewGuid()); Assert.Equal(HttpStatusCode.BadRequest, nonemptyClear.StatusCode);
        using var unknown = await Change(owner, "change=apply&actorId=" + actor, Guid.NewGuid()); Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        using var wrongAction = await Change(owner, "change=refresh", Guid.NewGuid()); Assert.Equal(HttpStatusCode.BadRequest, wrongAction.StatusCode);
        using var bodyRequest = new HttpRequestMessage(HttpMethod.Post, $"/boards/{board}/cards/filter-change?change=apply") { Content = JsonContent.Create(new { actorId = actor }) };
        bodyRequest.Headers.Add("X-StrataAI-Request", "1"); bodyRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        bodyRequest.Headers.Add("X-StrataAI-Expected-Actor", actor.ToString("D"));
        using var bodyRefused = await owner.SendAsync(bodyRequest, ct); Assert.Equal(HttpStatusCode.BadRequest, bodyRefused.StatusCode);
        var fencedKey = Guid.NewGuid();
        using var switchedRequest = new HttpRequestMessage(HttpMethod.Post, $"/boards/{board}/cards/filter-change?change=apply");
        switchedRequest.Headers.Add("X-StrataAI-Request", "1"); switchedRequest.Headers.Add("Idempotency-Key", fencedKey.ToString("D"));
        switchedRequest.Headers.Add("X-StrataAI-Expected-Actor", Guid.NewGuid().ToString("D"));
        using var switched = await owner.SendAsync(switchedRequest, ct); Assert.Equal(HttpStatusCode.Unauthorized, switched.StatusCode);
        Assert.Equal("session_unavailable", (await switched.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
        using var afterFence = await Change(owner, "change=apply", fencedKey); Assert.Equal(HttpStatusCode.OK, afterFence.StatusCode);
        using var invalid = await Change(owner, "change=apply&keyword=" + new string('a', 161), Guid.NewGuid()); Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var noCsrf = await Change(owner, "change=apply", Guid.NewGuid(), false); Assert.Equal(HttpStatusCode.Forbidden, noCsrf.StatusCode);
        using var anonymous = app.CreateClient();
        using var denied = await Change(anonymous, "change=apply", Guid.NewGuid()); Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
    }
}
