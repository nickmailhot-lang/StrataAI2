using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    // AUTH-FR-005/007, TC-07/08: the first upgraded profile must be the stored
    // profile, even when successive clock observations differ during sign-in.
    [Fact]
    public async Task Legacy_hash_upgrade_acknowledgment_and_retry_use_the_authoritative_profile_with_an_advancing_clock()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new AdvancingLegacyHashClock();
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton<IClock>(clock));
        using var client = app.CreateClient();
        await RegisterAndLogin(client);
        var profile = await client.GetFromJsonAsync<JsonElement>("/me", ct);
        var user = profile.GetProperty("id").GetGuid();
        const string password = "api-host-correct-horse";
        var legacy = new PasswordHasher<object>(Options.Create(new PasswordHasherOptions {
            CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV2,
        })).HashPassword(new object(), password);
        var hashes = app.Services.GetRequiredService<IPasswordHashService>();
        Assert.True(hashes.Verify(user, legacy, password).NeedsRehash);
        var store = app.Services.GetRequiredService<IIdentityStore>();
        await store.UpdatePasswordHashAsync(user, legacy, clock.UtcNow, ct);
        var before = await store.FindUserByIdAsync(user, ct);
        Assert.NotNull(before);
        var key = Guid.NewGuid().ToString();
        var body = new { email = profile.GetProperty("email").GetString(), password };
        using var first = await Mutate(client, HttpMethod.Post, "/auth/login", body, key);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var acknowledgment = await first.Content.ReadFromJsonAsync<JsonElement>(ct);
        var persisted = await store.FindUserByIdAsync(user, ct);
        Assert.NotNull(persisted);
        Assert.Equal(before.Version + 1, persisted.Version);
        Assert.NotEqual(before.PasswordHash, persisted.PasswordHash);
        Assert.Equal(new PasswordVerification(true, false), hashes.Verify(user, persisted.PasswordHash, password));
        Assert.Equal(persisted.UpdatedAt, acknowledgment.GetProperty("user").GetProperty("updatedAt").GetDateTimeOffset());
        using var replay = await Mutate(client, HttpMethod.Post, "/auth/login", body, key);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(acknowledgment.GetRawText(), (await replay.Content.ReadFromJsonAsync<JsonElement>(ct)).GetRawText());
        Assert.Equal(persisted, await store.FindUserByIdAsync(user, ct));
    }

    private sealed class AdvancingLegacyHashClock : IClock
    {
        private long _ticks = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero).UtcTicks + 7;
        public DateTimeOffset UtcNow => new(Interlocked.Add(ref _ticks, 10), TimeSpan.Zero);
    }
}
