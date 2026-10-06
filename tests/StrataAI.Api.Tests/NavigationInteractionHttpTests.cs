using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
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
