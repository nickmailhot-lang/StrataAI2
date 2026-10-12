using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    // PRD-21 API-FR-003: actual middleware, binding and resource errors share
    // the response-bound reference without exposing submitted private values.
    [Theory]
    [InlineData("unauthenticated", HttpStatusCode.Unauthorized, "authentication_required")]
    [InlineData("csrf", HttpStatusCode.Forbidden, "csrf_rejected")]
    [InlineData("missing", HttpStatusCode.NotFound, "board_not_found")]
    [InlineData("binding", HttpStatusCode.BadRequest, "invalid_request")]
    public async Task Api_errors_include_public_message_and_actual_correlation(
        string scenario, HttpStatusCode status, string code)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory();
        using var client = app.CreateClient();
        if (scenario == "binding") await RegisterAndLogin(client);
        using var request = scenario switch
        {
            "unauthenticated" => new HttpRequestMessage(HttpMethod.Get, "/me"),
            "missing" => new HttpRequestMessage(HttpMethod.Get, $"/boards/{Guid.NewGuid()}"),
            _ => new HttpRequestMessage(HttpMethod.Post, scenario == "csrf" ? "/auth/login" : "/boards")
            {
                Content = new StringContent("{\"private-probe\":", Encoding.UTF8, "application/json"),
            },
        };
        request.Headers.Add("X-Correlation-ID", "error-contract-reference");
        if (scenario == "binding")
        {
            request.Headers.Add("X-StrataAI-Request", "1");
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        }
        using var response = await client.SendAsync(request, ct);
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("message").GetString()));
        Assert.Equal("error-contract-reference", body.GetProperty("correlationId").GetString());
        Assert.Equal(body.GetProperty("correlationId").GetString(), response.Headers.GetValues("X-Correlation-ID").Single());
        Assert.DoesNotContain("private-probe", body.GetRawText());
    }

    [Theory]
    [InlineData("application/json")]
    [InlineData("text/html")]
    [InlineData("text/plain")]
    public async Task Error_contract_survives_non_json_accept_headers(string accept)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory();
        using var client = app.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/me");
        request.Headers.Accept.ParseAdd(accept);
        request.Headers.Add("X-Correlation-ID", "media-type-reference");
        using var response = await client.SendAsync(request, ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        Assert.Equal("authentication_required", body.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("message").GetString()));
        Assert.Equal("media-type-reference", body.GetProperty("correlationId").GetString());
        Assert.Equal(body.GetProperty("correlationId").GetString(), response.Headers.GetValues("X-Correlation-ID").Single());
    }

    [Theory]
    [InlineData("unsafe private-probe")]
    [InlineData("first,second")]
    public async Task Error_body_uses_the_sanitized_response_reference(string supplied)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory();
        using var client = app.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/me");
        request.Headers.TryAddWithoutValidation("X-Correlation-ID", supplied);
        using var response = await client.SendAsync(request, ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var reference = body.GetProperty("correlationId").GetString();
        Assert.Matches("^[0-9a-f]{32}$", reference!);
        Assert.Equal(reference, response.Headers.GetValues("X-Correlation-ID").Single());
        Assert.DoesNotContain(supplied, body.GetRawText());
    }

    [Fact]
    public async Task Date_validation_error_keeps_domain_code_and_canonical_state_unchanged()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory();
        using var client = app.CreateClient();
        await RegisterAndLogin(client);
        using var organization = await Mutate(client, HttpMethod.Post, "/organizations", new { name = "Error scope" });
        var org = (await organization.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("organization").GetProperty("id").GetGuid();
        using var createdBoard = await Mutate(client, HttpMethod.Post, "/boards", new { organizationId = org, name = "Error Board" });
        var board = (await createdBoard.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("id").GetGuid();
        using var createdList = await Mutate(client, HttpMethod.Post, $"/boards/{board}/lists", new { name = "Error List" });
        var list = (await createdList.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("id").GetGuid();
        using var createdCard = await Mutate(client, HttpMethod.Post, $"/lists/{list}/cards", new { title = "Unchanged Card" });
        var card = (await createdCard.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("id").GetGuid();
        var before = await client.GetStringAsync($"/boards/{board}", ct);
        using var refused = await Mutate(client, HttpMethod.Patch, $"/cards/{card}/dates", new
        {
            version = 1, dueAt = "2040-01-02", dueTimezone = "private-probe", dueHasTime = false, dueComplete = false,
        });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        var error = await refused.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        Assert.Equal("invalid_card_dates", error.GetProperty("code").GetString());
        Assert.Equal(error.GetProperty("title").GetString(), error.GetProperty("message").GetString());
        Assert.Equal(refused.Headers.GetValues("X-Correlation-ID").Single(), error.GetProperty("correlationId").GetString());
        Assert.DoesNotContain("private-probe", error.GetRawText());
        Assert.Equal(before, await client.GetStringAsync($"/boards/{board}", ct));
    }

    [Fact]
    public async Task Empty_head_error_preserves_bodyless_HTTP_semantics()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory();
        using var client = app.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Head, "/unknown-contract-route");
        using var response = await client.SendAsync(request, ct);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(ct));
        Assert.Matches("^[0-9a-f]{32}$", response.Headers.GetValues("X-Correlation-ID").Single());
    }
}
