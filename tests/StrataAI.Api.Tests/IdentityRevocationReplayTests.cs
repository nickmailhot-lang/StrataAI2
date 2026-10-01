using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    // PRD-02/60-TC-05/07: the copied original cookie proves only a completed receipt, never protected reads.
    [Theory]
    [InlineData("/auth/logout", "SESSION_REVOKED", 1)]
    [InlineData("/me/deactivate", "USER_DEACTIVATED", 2)]
    public async Task Revocation_retries_acknowledge_once_without_reopening_authentication(string route, string eventType, long version)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient();
        var cookie = await RegisterAndLogin(owner);
        var userId = (await owner.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        using var copied = app.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        copied.DefaultRequestHeaders.Add("Cookie", cookie);
        var key = Guid.NewGuid().ToString();
        var replies = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Mutate(copied, HttpMethod.Post, route, new { }, key)));
        foreach (var response in replies)
        {
            using (response) { Assert.Equal(HttpStatusCode.NoContent, response.StatusCode); Assert.Empty(await response.Content.ReadAsStringAsync(ct)); }
        }
        using var protectedRead = await copied.GetAsync("/me/sync?after=0", ct);
        Assert.Equal(HttpStatusCode.Unauthorized, protectedRead.StatusCode);
        var identities = app.Services.GetRequiredService<IIdentityStore>();
        var events = (await identities.ReadEventsAsync(userId, 0, ct)).Value!.Events;
        Assert.Equal(2, events.Count);
        Assert.Equal(eventType, events[1].EventType);
        Assert.Equal(version, events[1].Version);
        Assert.Equal(version, (await identities.FindUserByIdAsync(userId, ct))!.Version);
        using var anotherIntent = await Mutate(copied, HttpMethod.Post, route, new { }, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Unauthorized, anotherIntent.StatusCode);
        using var differentOperation = await Mutate(copied, HttpMethod.Post, route == "/auth/logout" ? "/me/deactivate" : "/auth/logout", new { }, key);
        Assert.Equal(HttpStatusCode.Conflict, differentOperation.StatusCode);
        Assert.Equal("idempotency_key_reused", (await differentOperation.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
        using var unrelated = app.CreateClient();
        unrelated.DefaultRequestHeaders.Add("Cookie", "strataai_session=unrelated-proof");
        using var denied = await Mutate(unrelated, HttpMethod.Post, route, new { }, key);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.Equal(2, (await identities.ReadEventsAsync(userId, 0, ct)).Value!.Events.Count);
    }

    [Fact]
    public async Task Revocation_receipt_cannot_be_replayed_by_another_session_of_the_same_account()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var second = app.CreateClient();
        await RegisterAndLogin(owner);
        var user = await owner.GetFromJsonAsync<JsonElement>("/me", ct);
        using var login = await Mutate(second, HttpMethod.Post, "/auth/login", new { email = user.GetProperty("email").GetString(), password = "api-host-correct-horse" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var key = Guid.NewGuid().ToString();
        using var first = await Mutate(owner, HttpMethod.Post, "/auth/logout", new { }, key);
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        using var collision = await Mutate(second, HttpMethod.Post, "/auth/logout", new { }, key);
        Assert.Equal(HttpStatusCode.Conflict, collision.StatusCode);
        using var stillActive = await second.GetAsync("/me", ct);
        Assert.Equal(HttpStatusCode.OK, stillActive.StatusCode);
    }

    [Fact]
    public async Task Expired_original_session_cannot_retrieve_its_revocation_receipt()
    {
        var clock = new ReceiptTestClock();
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton<IClock>(clock));
        using var owner = app.CreateClient();
        var cookie = await RegisterAndLogin(owner);
        using var copied = app.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        copied.DefaultRequestHeaders.Add("Cookie", cookie);
        var key = Guid.NewGuid().ToString();
        using var first = await Mutate(copied, HttpMethod.Post, "/auth/logout", new { }, key);
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        clock.UtcNow = clock.UtcNow.AddDays(40);
        using var expired = await Mutate(copied, HttpMethod.Post, "/auth/logout", new { }, key);
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
    }

    [Theory]
    [InlineData("/auth/logout")]
    [InlineData("/me/deactivate")]
    public async Task Receipt_endpoints_deny_missing_cookie_and_invalid_keys(string route)
    {
        await using var app = new ApiFactory(); using var client = app.CreateClient();
        using var noProof = await Mutate(client, HttpMethod.Post, route, new { }, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Unauthorized, noProof.StatusCode);
        using var noKey = await Mutate(client, HttpMethod.Post, route, new { });
        Assert.Equal(HttpStatusCode.Unauthorized, noKey.StatusCode);
        await RegisterAndLogin(client);
        using var invalid = await Mutate(client, HttpMethod.Post, route, new { }, Guid.Empty.ToString());
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var preserved = await client.GetAsync("/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, preserved.StatusCode);
    }
    private sealed class ReceiptTestClock : IClock { public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow; }
}
