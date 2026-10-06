using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    // PRD-02-TC-05/07: expiry after real profile/event writes must roll back.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Profile_expiry_after_publication_refuses_and_restores_state_before_retry(bool keyed)
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new SignInReceiptExpiryClock();
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton<IClock>(clock));
        using var client = app.CreateClient();
        await RegisterAndLogin(client);
        var profile = await client.GetFromJsonAsync<JsonElement>("/me", ct);
        var actor = profile.GetProperty("id").GetGuid();
        var store = app.Services.GetRequiredService<IIdentityStore>();
        var receipts = app.Services.GetRequiredService<IIdentityProfileReplayStore>();
        var original = await store.FindUserByIdAsync(actor, ct);
        var events = (await store.ReadEventsAsync(actor, 0, ct)).Value!.Events.ToArray();
        var key = Guid.NewGuid(); var observed = false;
        clock.AfterReceipt = () => {
            var current = store.ReadEventsAsync(actor, 0, ct).GetAwaiter().GetResult().Value!.Events;
            if (current.Count == events.Length) return null;
            Assert.Equal("USER_PROFILE_UPDATED", current.Last().EventType);
            observed = true;
            return clock.Instant.AddYears(10);
        };
        var body = new { displayName = "Final admission profile", version = original!.Version };
        using var denied = await Mutate(client, HttpMethod.Patch, "/me", body, keyed ? key.ToString() : null);
        Assert.True(observed); Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.DoesNotContain("Final admission profile", await denied.Content.ReadAsStringAsync(ct));
        clock.AfterReceipt = null;
        Assert.Equal(original, await store.FindUserByIdAsync(actor, ct));
        Assert.Equal(events, (await store.ReadEventsAsync(actor, 0, ct)).Value!.Events.ToArray());
        Assert.Null(await receipts.ReadAsync(actor, key, ct));
        using var retry = await Mutate(client, HttpMethod.Patch, "/me", body, keyed ? key.ToString() : null);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(original.Version + 1, (await retry.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("version").GetInt64());
        if (keyed)
        {
            using var replay = await Mutate(client, HttpMethod.Patch, "/me", body, key.ToString());
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        }
        Assert.Equal(events.Length + 1, (await store.ReadEventsAsync(actor, 0, ct)).Value!.Events.Count);
    }
}
