using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Identity;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    // PRD-02-TC-05/09: simulate withdrawal at final admission of real protected state.
    [Theory]
    [InlineData("/me/sync")]
    [InlineData("/me/sync?after=0")]
    public async Task Identity_sync_withholds_profile_events_and_cursor_after_final_actor_denial(string route)
    {
        var ct = TestContext.Current.CancellationToken;
        var admission = new SyncFinalActorFixture();
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton<ICommandActorAuthorization>(admission));
        using var client = app.CreateClient();
        await RegisterAndLogin(client);
        var before = await client.GetFromJsonAsync<JsonElement>("/me/sync?after=0", ct);
        var actor = before.GetProperty("profile").GetProperty("id").GetGuid();
        var store = app.Services.GetRequiredService<IIdentityStore>();
        var user = await store.FindUserByIdAsync(actor, ct);
        var events = (await store.ReadEventsAsync(actor, 0, ct)).Value!.Events.ToArray();
        admission.DenyFinal = true; admission.Checks = 0;
        using var denied = await client.GetAsync(route, ct);
        Assert.Equal(2, admission.Checks);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        var body = await denied.Content.ReadAsStringAsync(ct);
        Assert.DoesNotContain(actor.ToString(), body);
        Assert.DoesNotContain("cursor", body); Assert.DoesNotContain("profile", body);
        Assert.DoesNotContain("USER_REGISTERED", body);
        Assert.Equal(user, await store.FindUserByIdAsync(actor, ct));
        Assert.Equal(events, (await store.ReadEventsAsync(actor, 0, ct)).Value!.Events.ToArray());
        admission.DenyFinal = false;
        var recovered = await client.GetFromJsonAsync<JsonElement>("/me/sync?after=0", ct);
        Assert.Equal(before.GetRawText(), recovered.GetRawText());
    }

    private sealed class SyncFinalActorFixture : ICommandActorAuthorization
    {
        public bool DenyFinal { get; set; }
        public int Checks { get; set; }
        public Task<bool> VerifyAsync(Guid actorId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Checks++;
            return Task.FromResult(!DenyFinal || Checks == 1);
        }
    }
}
