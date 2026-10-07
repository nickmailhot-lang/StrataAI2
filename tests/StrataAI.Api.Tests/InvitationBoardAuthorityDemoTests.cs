using System.Net;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Onboarding;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Theory]
    [InlineData("BOARD_UPDATED")]
    [InlineData("BOARD_VISIBILITY_CHANGED")]
    [InlineData("BOARD_ARCHIVED")]
    [InlineData("BOARD_RESTORED")]
    [InlineData("BOARD_DELETED")]
    [InlineData("BOARD_MEMBER_UPDATED")]
    [InlineData("BOARD_MEMBER_ADDED")]
    [InlineData("BOARD_MEMBER_SELF_ROLE_CHANGED")]
    [InlineData("BOARD_MEMBER_REMOVED")]
    [InlineData("BOARD_MEMBER_SELF_REMOVED")]
    public async Task PRD_04_60_Demo_Board_authority_delivers_actual_HTTP_sources_without_invitation_transitions(string kind)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var member = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct);
        if (kind is "BOARD_RESTORED" or "BOARD_DELETED")
        {
            using var archived = await Mutate(owner, HttpMethod.Post, $"/boards/{f.Board}/archive", new { version = 1 });
            Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        }
        if (kind is "BOARD_MEMBER_SELF_REMOVED" or "BOARD_MEMBER_SELF_ROLE_CHANGED")
        {
            using var grant = await Mutate(owner, HttpMethod.Patch, $"/boards/{f.Board}/members/{f.Recipient}", new { role = "ADMIN" });
            Assert.Equal(HttpStatusCode.OK, grant.StatusCode);
        }
        if (kind == "BOARD_MEMBER_ADDED")
        {
            using var removed = await Mutate(owner, HttpMethod.Delete, $"/boards/{f.Board}/members/{f.Recipient}", new { });
            Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        }
        var invitations = app.Services.GetRequiredService<IInvitationStore>();
        var identities = app.Services.GetRequiredService<IIdentityStore>();
        var replay = app.Services.GetRequiredService<TransactionalInvitationRecipientSynchronization>();
        var codec = app.Services.GetRequiredService<IInvitationRecipientCursorCodec>();
        var now = app.Services.GetRequiredService<IClock>().UtcNow;
        var last = Guid.NewGuid(); var future = Guid.NewGuid();
        foreach (var id in new[] { last, future })
            Assert.True(await identities.TryCreateUserAsync(new UserIdentity(id, $"board-authority-{id:N}@example.test",
                $"board-authority-{id:N}@example.test".ToUpperInvariant(), "Board recipient", null, "en-CA", "UTC",
                AccountStatus.Active, true, "unused-fixture-hash", now, now, 1), null, null, ct));
        for (var i = 1; i <= 206; i++)
        {
            var email = i == 205 ? $"board-authority-{last:N}@example.test" : i == 206 ? $"board-authority-{future:N}@example.test" : "demo@strataai.test";
            await invitations.CreateAsync(new InvitationRecord(Guid.NewGuid(), f.Organization, email, email.ToUpperInvariant(),
                i.ToString("x64"), InvitationSurface.Portal, "OWNER", f.Owner,
                i == 206 ? now.AddDays(1) : now.AddSeconds(-1).AddTicks(i), now.AddDays(7), null, null), ct);
        }
        var before = await replay.ReadAsync(DemoRecipient, null, cancellationToken: ct);
        var lastBefore = await replay.ReadAsync(last, null, cancellationToken: ct);
        var futureBefore = await replay.ReadAsync(future, null, cancellationToken: ct);
        Assert.True(before.Succeeded); Assert.True(lastBefore.Succeeded); Assert.True(futureBefore.Succeeded);
        using var response = kind switch
        {
            "BOARD_UPDATED" => await Mutate(owner, HttpMethod.Patch, $"/boards/{f.Board}", new { name = "Changed Board authority", version = 1 }),
            "BOARD_VISIBILITY_CHANGED" => await Mutate(owner, HttpMethod.Patch, $"/boards/{f.Board}/visibility", new { visibility = "ORGANIZATION", version = 1 }),
            "BOARD_ARCHIVED" => await Mutate(owner, HttpMethod.Post, $"/boards/{f.Board}/archive", new { version = 1 }),
            "BOARD_RESTORED" => await Mutate(owner, HttpMethod.Post, $"/boards/{f.Board}/restore", new { version = 2 }),
            "BOARD_DELETED" => await Mutate(owner, HttpMethod.Delete, $"/boards/{f.Board}?version=2&confirmed=true", new { }),
            "BOARD_MEMBER_UPDATED" => await Mutate(owner, HttpMethod.Patch, $"/boards/{f.Board}/members/{f.Recipient}", new { role = "ADMIN" }),
            "BOARD_MEMBER_ADDED" => await Mutate(owner, HttpMethod.Patch, $"/boards/{f.Board}/members/{f.Recipient}", new { role = "MEMBER" }),
            "BOARD_MEMBER_SELF_ROLE_CHANGED" => await Mutate(member, HttpMethod.Patch, $"/boards/{f.Board}/members/{f.Recipient}", new { role = "MEMBER" }),
            "BOARD_MEMBER_SELF_REMOVED" => await Mutate(member, HttpMethod.Delete, $"/boards/{f.Board}/members/{f.Recipient}", new { }),
            _ => await Mutate(owner, HttpMethod.Delete, $"/boards/{f.Board}/members/{f.Recipient}", new { }),
        };
        Assert.Equal(kind is "BOARD_MEMBER_REMOVED" or "BOARD_MEMBER_SELF_REMOVED"
            ? HttpStatusCode.NoContent : HttpStatusCode.OK, response.StatusCode);
        foreach (var pair in new[] { (Id: DemoRecipient, Email: "DEMO@STRATAAI.TEST", Cursor: before.Value!.Cursor),
            (Id: last, Email: $"board-authority-{last:N}@example.test".ToUpperInvariant(), Cursor: lastBefore.Value!.Cursor) })
        {
            Assert.False((await replay.IsCursorCurrentAsync(pair.Id, pair.Cursor, ct)).Value);
            var reset = await replay.ReadAsync(pair.Id, pair.Cursor, cancellationToken: ct);
            Assert.True(reset.Succeeded); Assert.True(reset.Value!.ResetRequired); Assert.Empty(reset.Value.Events);
            Assert.True(codec.TryDecode(new(pair.Id, pair.Email, 1, 1), reset.Value.Cursor, out _));
            var quiet = await replay.ReadAsync(pair.Id, reset.Value.Cursor, cancellationToken: ct);
            Assert.True(quiet.Succeeded); Assert.False(quiet.Value!.ResetRequired); Assert.Empty(quiet.Value.Events);
        }
        Assert.True((await replay.IsCursorCurrentAsync(future, futureBefore.Value!.Cursor, ct)).Value);
    }

    [Fact]
    public async Task PRD_04_60_Demo_Board_authority_rolls_back_delivered_effects_and_refuses_unproven_sources()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var member = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct);
        var store = app.Services.GetRequiredService<IWorkManagementStore>();
        var events = app.Services.GetRequiredService<IWorkEventStore>();
        var unit = app.Services.GetRequiredService<IWorkManagementUnitOfWork>();
        var invitations = app.Services.GetRequiredService<IInvitationStore>();
        var replay = app.Services.GetRequiredService<TransactionalInvitationRecipientSynchronization>();
        var at = app.Services.GetRequiredService<IClock>().UtcNow;
        await invitations.CreateAsync(new(Guid.NewGuid(), f.Organization, "demo@strataai.test", "DEMO@STRATAAI.TEST",
            new string('a', 64), InvitationSurface.Portal, "OWNER", f.Owner, at.AddSeconds(-1), at.AddDays(7), null, null), ct);
        var before = await replay.ReadAsync(DemoRecipient, null, cancellationToken: ct);
        var board = (await store.FindBoardAsync(f.Board, ct))!;
        // Trusted transaction fixture isolates proof/rollback; real HTTP source
        // and session admission are exercised by the theory above.
        Task<WorkOperation<bool>> InScope(Func<Task<WorkOperation<bool>>> action) =>
            unit.ExecuteReadAsync(f.Organization, null, "fixture_denied", () => Task.FromResult(true), action, ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => InScope(async () => {
            await events.AppendAsync(new(Guid.NewGuid(), f.Organization, f.Board, f.Owner, "BOARD_UPDATED", "Board", f.Board, 1, "unproven-board", at), ct);
            return WorkOperation<bool>.Success(true);
        }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => InScope(async () => {
            Assert.NotNull(await store.UpdateBoardAsync(f.Board, "Tentative Board", null, "COLOR", null, 1, at, ct));
            await events.AppendAsync(new(Guid.NewGuid(), f.Organization, f.Board, f.Owner, "BOARD_UPDATED", "Board", f.Board, 2, "tentative-board", at), ct);
            throw new InvalidOperationException("Injected late failure after authority delivery.");
        }));
        Assert.Equal(board, await store.FindBoardAsync(f.Board, ct));
        Assert.True((await replay.IsCursorCurrentAsync(DemoRecipient, before.Value!.Cursor, ct)).Value);
        await Assert.ThrowsAsync<InvalidOperationException>(() => InScope(async () => {
            Assert.NotNull(await store.UpdateBoardAsync(f.Board, "Duplicate tentative Board", null, "COLOR", null, 1, at, ct));
            var source = new WorkEvent(Guid.NewGuid(), f.Organization, f.Board, f.Owner, "BOARD_UPDATED", "Board", f.Board, 2, "duplicate-board", at);
            await events.AppendAsync(source, ct);
            await events.AppendAsync(source, ct); // Same canonical source is idempotent.
            await events.AppendAsync(source with { EventId = Guid.NewGuid() }, ct); // A second source cannot consume the proof.
            return WorkOperation<bool>.Success(true);
        }));
        Assert.Equal(board, await store.FindBoardAsync(f.Board, ct));
        Assert.True((await replay.IsCursorCurrentAsync(DemoRecipient, before.Value.Cursor, ct)).Value);
        Assert.True((await InScope(async () => {
            Assert.NotNull(await store.UpdateBoardAsync(f.Board, "Earlier command Board", null, "COLOR", null, 1, at, ct));
            return WorkOperation<bool>.Success(true);
        })).Succeeded);
        await Assert.ThrowsAsync<InvalidOperationException>(() => InScope(async () => {
            await events.AppendAsync(new(Guid.NewGuid(), f.Organization, f.Board, f.Owner, "BOARD_UPDATED", "Board", f.Board, 2, "earlier-command-board", at), ct);
            return WorkOperation<bool>.Success(true);
        }));
        Assert.True((await replay.IsCursorCurrentAsync(DemoRecipient, before.Value.Cursor, ct)).Value);
        // Proof captured outside this command cannot be used by a later command.
        Assert.NotNull(await store.UpdateBoardAsync(f.Board, "Legacy Board", null, "COLOR", null, 2, at, ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => InScope(async () => {
            await events.AppendAsync(new(Guid.NewGuid(), f.Organization, f.Board, f.Owner, "BOARD_UPDATED", "Board", f.Board, 3, "legacy-board", at), ct);
            return WorkOperation<bool>.Success(true);
        }));
        using var committed = await Mutate(owner, HttpMethod.Patch, $"/boards/{f.Board}", new { name = "Committed Board", version = 3 });
        Assert.Equal(HttpStatusCode.OK, committed.StatusCode);
        Assert.False((await replay.IsCursorCurrentAsync(DemoRecipient, before.Value.Cursor, ct)).Value);
        var reset = await replay.ReadAsync(DemoRecipient, before.Value.Cursor, cancellationToken: ct);
        Assert.True(reset.Succeeded); Assert.True(reset.Value!.ResetRequired); Assert.Empty(reset.Value.Events);
    }
}
