using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Identity;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    // AUTH-FR-005/009: corrupt hash storage is ordinary credential refusal.
    [Theory]
    [InlineData("not-base64!")]
    [InlineData("")]
    [InlineData("AQ==")]
    [InlineData("Ag==")]
    public async Task Malformed_password_hash_refuses_credentials_without_identity_mutation(string malformed)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var client = app.CreateClient();
        var service = app.Services.GetRequiredService<IIdentityService>();
        var store = app.Services.GetRequiredService<IIdentityStore>();
        var hashes = app.Services.GetRequiredService<IPasswordHashService>();
        var email = $"corrupt-hash-{Guid.NewGuid():N}@example.test";
        const string password = "corrupt-hash-correct-horse";
        var registered = await service.RegisterAsync(email, password, "Protected hash subject", null, null, "fixture", ct);
        Assert.True(registered.Succeeded); var actor = registered.Value!.User.Id;
        await store.UpdatePasswordHashAsync(actor, malformed, DateTimeOffset.UtcNow, ct);
        var before = await store.FindUserByIdAsync(actor, ct);
        var events = (await store.ReadEventsAsync(actor, 0, ct)).Value!.Events.ToArray();
        Assert.Equal(new PasswordVerification(false, false), hashes.Verify(actor, malformed, password));
        var key = Guid.NewGuid();
        using var denied = await Mutate(client, HttpMethod.Post, "/auth/login", new { email, password }, key.ToString());
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.Equal("invalid_credentials", (await denied.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
        Assert.False(denied.Headers.Contains("Set-Cookie"));
        var body = await denied.Content.ReadAsStringAsync(ct);
        Assert.DoesNotContain(email, body); Assert.DoesNotContain("Protected hash subject", body);
        Assert.DoesNotContain("FormatException", body);
        Assert.Equal(before, await store.FindUserByIdAsync(actor, ct));
        Assert.Equal(events, (await store.ReadEventsAsync(actor, 0, ct)).Value!.Events.ToArray());
        Assert.Null(await app.Services.GetRequiredService<IIdentityLoginReplayStore>().ReadAsync(actor, key, ct));
    }
}
