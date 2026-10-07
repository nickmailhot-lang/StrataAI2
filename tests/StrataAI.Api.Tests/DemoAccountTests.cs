using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task Demo_documented_account_signs_in_without_registration_and_has_no_implicit_access()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory();
        using var client = app.CreateClient();
        using var login = await Mutate(client, HttpMethod.Post, "/auth/login",
            new { email = "demo@strataai.test", password = "StrataAI-Demo-2026!" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Contains("httponly", Assert.Single(login.Headers.GetValues("Set-Cookie")).ToLowerInvariant());
        var me = await client.GetFromJsonAsync<JsonElement>("/me", ct);
        Assert.Equal("demo@strataai.test", me.GetProperty("email").GetString());
        Assert.True(me.GetProperty("emailVerified").GetBoolean());
        Assert.Equal("Demo User", me.GetProperty("displayName").GetString());
        using var denied = await client.GetAsync("/organizations/11111111-1111-1111-1111-111111111111", ct);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
    }

    [Fact]
    public async Task Demo_documented_account_rejects_wrong_password_without_issuing_a_session()
    {
        await using var app = new ApiFactory();
        using var client = app.CreateClient();
        using var login = await Mutate(client, HttpMethod.Post, "/auth/login",
            new { email = "demo@strataai.test", password = "incorrect-password" });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        Assert.False(login.Headers.Contains("Set-Cookie"));
        using var me = await client.GetAsync("/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }
    // ARCH-06 / PRD-02: Development enables full service validation. The
    // ordinary Testing host does not prove standalone Demo composition.
    [Fact]
    public async Task Demo_documented_account_starts_in_Development_with_binary_storage_disabled()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(environment: "Development");
        using var client = app.CreateClient();
        using var login = await Mutate(client, HttpMethod.Post, "/auth/login",
            new { email = "demo@strataai.test", password = "StrataAI-Demo-2026!" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var cover = await client.GetAsync($"/cards/{Guid.NewGuid()}/cover", ct);
        Assert.Equal(HttpStatusCode.NotFound, cover.StatusCode);
    }
}
