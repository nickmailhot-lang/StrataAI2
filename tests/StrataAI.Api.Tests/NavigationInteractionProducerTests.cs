using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    // Synthetic storage admission: these tests prove producer/transaction
    // acknowledgment behavior, not PostgreSQL target authorization or rollback.
    private sealed class NavigationAdmissionFixture : INavigationInteractionEventStore
    {
        public bool Allowed { get; set; } = true;
        public Action? AfterAppend { get; set; }
        public List<NavigationInteractionEvent> Observed { get; } = [];
        public Task<bool> AppendAuthorizedAsync(NavigationInteractionEvent source, CancellationToken ct = default)
        { ct.ThrowIfCancellationRequested(); Observed.Add(source); AfterAppend?.Invoke(); return Task.FromResult(Allowed); }
    }

    [Fact]
    public async Task PRD_01_navigation_producer_refuses_invalid_scope_denied_target_and_late_session_without_acknowledgment()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton<ICommandActorAuthorization, SearchSourceActorFixture>());
        using var client = app.CreateClient(); await RegisterAndLogin(client);
        var actor = (await client.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        var proof = (SearchSourceActorFixture)app.Services.GetRequiredService<ICommandActorAuthorization>();
        var store = new NavigationAdmissionFixture();
        var producer = new NavigationInteractionEventProducer(store,
            app.Services.GetRequiredService<IIdentityUnitOfWork>(), proof, app.Services.GetRequiredService<IClock>());
        var org = Guid.NewGuid(); var board = Guid.NewGuid();
        Assert.Equal("invalid_navigation", (await producer.BoardOpenedAsync(actor, org, Guid.Empty, 1, ct)).ErrorCode);
        Assert.Equal("session_unavailable", (await producer.ApplicationContextChangedAsync(Guid.Empty, null, ct)).ErrorCode);
        Assert.Empty(store.Observed);
        store.Allowed = false;
        var denied = await producer.BoardOpenedAsync(actor, org, board, 4, ct);
        Assert.Equal("navigation_unavailable", denied.ErrorCode); Assert.Null(denied.Value);
        store.Allowed = true; store.AfterAppend = () => proof.RefuseAt(1);
        var late = await producer.CardOpenedAsync(actor, org, board, Guid.NewGuid(), 7, ct);
        Assert.Equal("session_unavailable", late.ErrorCode); Assert.Null(late.Value);
        proof.RefuseAt(null); store.AfterAppend = null;
        var accepted = await producer.BoardOpenedAsync(actor, org, board, 4, ct);
        Assert.True(accepted.Succeeded); Assert.Equal("BOARD_OPENED", accepted.Value!.EventType);
        Assert.Equal(actor, accepted.Value.ActorId); Assert.Equal(board, accepted.Value.EntityId);
        Assert.Empty(accepted.Value.Metadata); Assert.Equal(3, store.Observed.Count);
    }
}
