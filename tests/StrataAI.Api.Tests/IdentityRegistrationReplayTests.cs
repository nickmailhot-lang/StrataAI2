using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Identity;
using StrataAI.Application.Common;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task Registration_retry_never_reconstructs_an_expired_verification_bearer()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new ReceiptTestClock();
        await using var app = new ApiFactory(configureServices: services => {
            services.AddSingleton<IClock>(clock);
            services.AddSingleton(new IdentityPolicy(true, true, 12, TimeSpan.FromHours(12), TimeSpan.FromMinutes(30)));
        });
        using var client = app.CreateClient();
        var body = new { email = $"expired-registration-{Guid.NewGuid():N}@example.test", password = "registration-correct-horse", displayName = "Expiry registration" };
        var key = Guid.NewGuid().ToString();
        using var first = await Mutate(client, HttpMethod.Post, "/auth/register", body, key);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var acknowledgment = await first.Content.ReadFromJsonAsync<JsonElement>(ct);
        var id = acknowledgment.GetProperty("user").GetProperty("id").GetGuid();
        var original = acknowledgment.GetProperty("verificationToken").GetString()!;
        clock.UtcNow = clock.UtcNow.AddMinutes(31);
        using var expired = await Mutate(client, HttpMethod.Post, "/auth/register", body, key);
        Assert.Equal(HttpStatusCode.Conflict, expired.StatusCode);
        var denial = await expired.Content.ReadAsStringAsync(ct);
        Assert.DoesNotContain(original, denial); Assert.DoesNotContain(id.ToString(), denial);
        Assert.Single((await app.Services.GetRequiredService<IIdentityStore>().ReadEventsAsync(id, 0, ct)).Value!.Events);
    }

    [Fact]
    public async Task Registration_retries_create_one_account_and_preserve_only_the_original_verification_proof()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton(
            new IdentityPolicy(true, true, 12, TimeSpan.FromHours(12), TimeSpan.FromMinutes(30))));
        using var client = app.CreateClient();
        var body = new { email = $"registration-{Guid.NewGuid():N}@example.test", password = "registration-correct-horse", displayName = "Original registration" };
        var key = Guid.NewGuid().ToString();
        var replies = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Mutate(client, HttpMethod.Post, "/auth/register", body, key)));
        string? acknowledgment = null;
        foreach (var reply in replies)
        {
            using (reply)
            {
                Assert.Equal(HttpStatusCode.Created, reply.StatusCode);
                var json = await reply.Content.ReadAsStringAsync(ct);
                acknowledgment ??= json; Assert.Equal(acknowledgment, json);
            }
        }
        var registration = JsonSerializer.Deserialize<JsonElement>(acknowledgment!);
        var id = registration.GetProperty("user").GetProperty("id").GetGuid();
        Assert.Single((await app.Services.GetRequiredService<IIdentityStore>().ReadEventsAsync(id, 0, ct)).Value!.Events);
        using var newKey = await Mutate(client, HttpMethod.Post, "/auth/register", body, Guid.NewGuid().ToString());
        Assert.NotEqual(HttpStatusCode.Created, newKey.StatusCode);
        using var wrong = await Mutate(client, HttpMethod.Post, "/auth/register", new { body.email, password = "incorrect-private-password", body.displayName }, key);
        Assert.NotEqual(HttpStatusCode.Created, wrong.StatusCode);
        Assert.DoesNotContain(id.ToString(), await wrong.Content.ReadAsStringAsync(ct));
        using var collision = await Mutate(client, HttpMethod.Post, "/auth/register", new { body.email, body.password, displayName = "Changed intent" }, key);
        Assert.Equal(HttpStatusCode.Conflict, collision.StatusCode);
        using var verified = await Mutate(client, HttpMethod.Post, "/auth/verify-email", new { token = registration.GetProperty("verificationToken").GetString() });
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        using var replay = await Mutate(client, HttpMethod.Post, "/auth/register", body, key);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        var current = await replay.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.True(current.GetProperty("user").GetProperty("emailVerified").GetBoolean());
        Assert.Equal(JsonValueKind.Null, current.GetProperty("verificationToken").ValueKind);
        Assert.Equal(2, (await app.Services.GetRequiredService<IIdentityStore>().ReadEventsAsync(id, 0, ct)).Value!.Events.Count);
    }
}
