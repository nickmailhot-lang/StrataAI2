using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Theory]
    [InlineData("metadata", null)]
    [InlineData("metadata", "http://evil.example.test")]
    [InlineData("lifecycle", null)]
    [InlineData("lifecycle", "http://evil.example.test")]
    public async Task Metadata_or_lifecycle_live_refuses_missing_or_foreign_origin(string channel, string? origin)
    {
        await using var app = new ApiFactory(); using var client = app.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/organizations/live/{channel}/negotiate?negotiateVersion=1");
        request.Headers.Add("X-StrataAI-Request", "1");
        if (origin is not null) request.Headers.Add("Origin", origin);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("realtime_origin_denied", (await response.Content.ReadFromJsonAsync<JsonElement>(
            cancellationToken: TestContext.Current.CancellationToken)).GetProperty("code").GetString());
    }
}
