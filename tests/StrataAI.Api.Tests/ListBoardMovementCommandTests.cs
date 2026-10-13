using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    private sealed class ListMovementCommandContext : IWorkCommandContext
    {
        public Guid? IdempotencyKey { get; set; } = Guid.NewGuid();
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("source")]
    [InlineData("destination")]
    [InlineData("fabricated")]
    public async Task PRD_07_List_command_rolls_back_late_admission_loss_and_retries_original_key(string loss)
    {
        var ct = TestContext.Current.CancellationToken;
        var fence = new OrganizationTransactionActorFixture();
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton<ICommandActorAuthorization>(fence));
        using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var source = await TelemetryBoard(owner, ct);
        var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var org = (await work.FindBoardAsync(source, ct))!.OrganizationId;
        var actor = (await owner.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        using var created = await Mutate(owner, HttpMethod.Post, "/boards", new { organizationId = org, name = "Command target", visibility = "PRIVATE" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var destination = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        var now = DateTimeOffset.UtcNow;
        var list = await work.CreateListAsync(source, Guid.NewGuid(), "Command source", RankToken.Initial(), now, ct);
        var card = await work.CreateCardAsync(list.Id, Guid.NewGuid(), "Stable child", "Retained body", RankToken.Initial(), now, ct);
        var context = new ListMovementCommandContext();
        var command = new ListBoardMovementCommand(work, app.Services.GetRequiredService<IWorkBoardAuthorization>(),
            app.Services.GetRequiredService<IOrganizationStore>(), fence, app.Services.GetRequiredService<IWorkManagementUnitOfWork>(), context);
        var movement = app.Services.GetRequiredService<IListBoardMovementStore>();
        var attempts = 0;
        async Task<WorkOperation<BoardListRecord>> Transfer(bool fail)
        {
            attempts++;
            if (fail && loss == "fabricated")
                return WorkOperation<BoardListRecord>.Success(list with { BoardId = destination, Version = 2 });
            var moved = await movement.TransferListBoardAsync(org, source, list.Id, destination, 1, now.AddSeconds(1), false, null, ct);
            Assert.NotNull(moved);
            if (fail && loss == "actor") fence.Allowed = false;
            if (fail && loss is "source" or "destination")
            {
                var id = loss == "source" ? source : destination;
                var board = (await work.FindBoardAsync(id, ct))!;
                Assert.NotNull(await work.SetBoardLifecycleAsync(id, BoardLifecycleState.Active, BoardLifecycleState.Archived,
                    board.Version, now.AddSeconds(2), ct));
            }
            return WorkOperation<BoardListRecord>.Success(moved.List);
        }
        if (loss == "fabricated")
            await Assert.ThrowsAsync<InvalidOperationException>(() => command.ExecuteAsync(list.Id, actor, source, destination, 1, null, () => Transfer(true), ct));
        else
        {
            var refused = await command.ExecuteAsync(list.Id, actor, source, destination, 1, null, () => Transfer(true), ct);
            Assert.False(refused.Succeeded);
            Assert.Equal(loss == "actor" ? "session_unavailable" : "list_not_found", refused.ErrorCode);
        }
        fence.Allowed = true;
        Assert.Equal(list, await work.FindListAsync(list.Id, ct));
        Assert.Equal(card, await work.FindCardAsync(card.Id, ct));
        Assert.Equal(BoardLifecycleState.Active, (await work.FindBoardAsync(source, ct))!.LifecycleState);
        Assert.Equal(BoardLifecycleState.Active, (await work.FindBoardAsync(destination, ct))!.LifecycleState);
        var success = await command.ExecuteAsync(list.Id, actor, source, destination, 1, null, () => Transfer(false), ct);
        Assert.True(success.Succeeded); Assert.Equal(2, success.Value!.Version);
        var replay = await command.ExecuteAsync(list.Id, actor, source, destination, 1, null, () => Transfer(false), ct);
        Assert.Equal(success, replay); Assert.Equal(2, attempts);
        Assert.Equal(card.Version + 1, (await work.FindCardAsync(card.Id, ct))!.Version);
        var mismatch = await command.ExecuteAsync(list.Id, actor, source, destination, 1, Guid.NewGuid(), () => Transfer(false), ct);
        Assert.Equal("idempotency_key_reused", mismatch.ErrorCode); Assert.Equal(2, attempts);
        var revokedBoard = loss == "destination" ? destination : source;
        if (loss == "fabricated")
        {
            using var thirdCreated = await Mutate(owner, HttpMethod.Post, "/boards", new { organizationId = org, name = "Later parent", visibility = "PRIVATE" });
            Assert.Equal(HttpStatusCode.Created, thirdCreated.StatusCode);
            revokedBoard = (await thirdCreated.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
            using (app.Services.GetRequiredService<StrataAI.Infrastructure.WorkManagement.DemoWorkTransactionScope>().Enter(org))
                Assert.NotNull(await movement.TransferListBoardAsync(org, destination, list.Id, revokedBoard, 2,
                    DateTimeOffset.UtcNow.AddMinutes(1), false, null, ct));
            Assert.Equal(success, await command.ExecuteAsync(list.Id, actor, source, destination, 1, null, () => Transfer(false), ct));
            Assert.Equal(2, attempts);
        }
        var original = (await work.FindBoardAsync(revokedBoard, ct))!;
        Assert.NotNull(await work.SetBoardLifecycleAsync(revokedBoard, BoardLifecycleState.Active, BoardLifecycleState.Archived,
            original.Version, now.AddSeconds(3), ct));
        var withdrawn = await command.ExecuteAsync(list.Id, actor, source, destination, 1, null, () => Transfer(false), ct);
        Assert.Equal("list_not_found", withdrawn.ErrorCode); Assert.Equal(2, attempts);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(long.MaxValue)]
    public async Task PRD_07_List_command_refuses_invalid_versions_before_transfer(long version)
    {
        await using var app = new ApiFactory();
        var command = new ListBoardMovementCommand(app.Services.GetRequiredService<IWorkManagementStore>(),
            app.Services.GetRequiredService<IWorkBoardAuthorization>(), app.Services.GetRequiredService<IOrganizationStore>(),
            app.Services.GetRequiredService<ICommandActorAuthorization>(), app.Services.GetRequiredService<IWorkManagementUnitOfWork>(),
            new ListMovementCommandContext());
        var result = await command.ExecuteAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), version, null,
            () => throw new InvalidOperationException("Invalid input reached transfer."), TestContext.Current.CancellationToken);
        Assert.Equal("invalid_version", result.ErrorCode);
    }
}
