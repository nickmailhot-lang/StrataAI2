using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    // PRD-02/60-TC-05: command identity matches the authenticated session and
    // revoked sessions/deactivated accounts never authorize future commands.
    [Fact]
    public async Task Command_actor_requires_matching_live_session_and_active_account()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var client = app.CreateClient();
        var cookie = await RegisterAndLogin(client);
        var actor = (await client.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        var store = app.Services.GetRequiredService<IIdentityStore>();
        var hash = app.Services.GetRequiredService<ISecureTokenService>().Hash(cookie[(cookie.IndexOf('=') + 1)..]);
        var context = new CommandActorTestContext(actor, hash);
        var verifier = new CommandActorAuthorization(context, store, app.Services.GetRequiredService<IClock>(), app.Services.GetRequiredService<IdentityPolicy>());
        Assert.True(await verifier.VerifyAsync(actor, ct));
        Assert.False(await verifier.VerifyAsync(Guid.NewGuid(), ct));
        await store.RevokeSessionAsync(hash, DateTimeOffset.UtcNow, ct);
        Assert.False(await verifier.VerifyAsync(actor, ct));
        Assert.True(await store.DeactivateUserAsync(actor, DateTimeOffset.UtcNow, ct));
        Assert.False(await verifier.VerifyAsync(actor, ct));
    }

    private sealed record CommandActorTestContext(Guid Actor, string Hash) : ICommandActorContext
    {
        public bool HasHttpRequest => true;
        public Guid? AuthenticatedUserId => Actor;
        public string? SessionTokenHash => Hash;
    }
}
