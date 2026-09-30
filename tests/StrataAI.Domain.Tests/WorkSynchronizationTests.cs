using StrataAI.Application.WorkManagement;

namespace StrataAI.Domain.Tests;

// PRD-22-TC-04/05/07/09, RT-FR-003/010/011: bounded, scoped replay.
public sealed class WorkSynchronizationTests
{
    private static readonly Guid Organization = Guid.NewGuid(), Board = Guid.NewGuid(), Actor = Guid.NewGuid();
    private static BoardSyncScope Scope(bool admin = true) => new(new(Board, Organization, "Protected", null,
        BoardVisibility.Private, "COLOR", null, BoardLifecycleState.Active, DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow, 7), new(true, admin, admin, admin));
    private static WorkEventReadCandidate Row(long sequence, bool ready = true, bool visible = true) =>
        new(sequence, new(Guid.NewGuid(), Organization, Board, Actor, "CARD_UPDATED", "Card", Guid.NewGuid(),
            3, "safe-correlation", DateTimeOffset.UtcNow), ready, visible);

    [Fact]
    public void ReadyEventsCannotJumpAnEarlierPendingSequence()
    {
        var rows = new[] { Row(1), Row(2, false), Row(3) };
        var first = WorkEventReadWindow.Build(0, 3, 3, rows);
        Assert.Equal(1, first.Cursor); Assert.True(first.Pending); Assert.False(first.HasMore);
        Assert.Single(first.Events);
        var waiting = WorkEventReadWindow.Build(1, 3, 3, rows[1..]);
        Assert.Equal(1, waiting.Cursor); Assert.Empty(waiting.Events); Assert.True(waiting.Pending);
        rows[1] = rows[1] with { Ready = true };
        var recovered = WorkEventReadWindow.Build(1, 3, 3, rows[1..]);
        Assert.Equal(3, recovered.Cursor); Assert.False(recovered.Pending); Assert.Equal(2, recovered.Events.Count);
    }

    [Fact]
    public void BoundedPagesPreserveStableEventIdentityAndCursor()
    {
        var rows = new[] { Row(1), Row(2), Row(3) };
        var first = WorkEventReadWindow.Build(0, 3, 2, rows);
        var replay = WorkEventReadWindow.Build(0, 3, 2, rows);
        Assert.Equal(2, first.Cursor); Assert.True(first.HasMore); Assert.Equal(first.Events, replay.Events);
        var second = WorkEventReadWindow.Build(first.Cursor, 3, 2, rows[2..]);
        Assert.Equal(3, second.Cursor); Assert.False(second.HasMore); Assert.Single(second.Events);
    }

    [Theory]
    [InlineData("future")]
    [InlineData("missing-first")]
    [InlineData("missing-tail")]
    public void UnknownOrMissingCursorHistoryRequiresSnapshotRecovery(string kind)
    {
        var page = kind switch
        {
            "future" => WorkEventReadWindow.Build(10, 2, 2, []),
            "missing-first" => WorkEventReadWindow.Build(0, 2, 2, [Row(2)]),
            _ => WorkEventReadWindow.Build(0, 2, 2, [Row(1)])
        };
        Assert.True(page.ResetRequired); Assert.Equal(0, page.Cursor); Assert.Empty(page.Events);
        Assert.False(page.HasMore); Assert.False(page.Pending);
    }

    [Fact]
    public async Task DeniedScopeNeverReadsTheEventStore()
    {
        var authorization = new Authorization { Denied = true }; var reader = new Reader();
        var result = await new WorkSynchronizationService(authorization, reader).ReadAsync(Board, Actor, 0,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("board_not_found", result.ErrorCode); Assert.Null(result.Value); Assert.Equal(0, reader.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RevocationDuringReadRejectsTheWholeResponseIncludingEmptyPages(bool empty)
    {
        var authorization = new Authorization();
        var reader = new Reader { Read = async () =>
        {
            await Task.Yield(); authorization.Denied = true;
            return empty ? new(0, false, false, false, []) : new(1, false, false, false, [Row(1)]);
        } };
        var result = await new WorkSynchronizationService(authorization, reader).ReadAsync(Board, Actor, 0,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(2, authorization.Calls); Assert.False(result.Succeeded); Assert.Null(result.Value);
        Assert.Equal("board_not_found", result.ErrorCode);
    }

    [Fact]
    public async Task PublicViewRedactsActorAndCoarsensHiddenEntityHistory()
    {
        var visible = Row(1); var hidden = Row(2, visible: false);
        var reader = new Reader { Read = () => Task.FromResult(new WorkEventReadPage(2, false, false, false, [visible, hidden])) };
        var result = await new WorkSynchronizationService(new Authorization { Current = Scope(false) }, reader)
            .ReadAsync(Board, null, 0, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded);
        var page = Assert.IsType<BoardSyncPage>(result.Value);
        Assert.Equal(visible.Event.EntityId, page.Events[0].EntityId); Assert.Null(page.Events[0].ActorId);
        var protectedEvent = page.Events[1];
        Assert.Equal("BOARD_INVALIDATED", protectedEvent.EventType); Assert.Equal("Board", protectedEvent.EntityType);
        Assert.Equal(Board, protectedEvent.EntityId); Assert.Equal(7, protectedEvent.Version); Assert.Null(protectedEvent.ActorId);
        Assert.Equal(hidden.Event.EventId, protectedEvent.EventId); Assert.Empty(protectedEvent.Metadata);
    }

    [Fact]
    public async Task AdministratorGetsActorOnlyForVisibleEntityEvents()
    {
        var reader = new Reader { Read = () => Task.FromResult(new WorkEventReadPage(2, false, false, false,
            [Row(1), Row(2, visible: false)])) };
        var result = await new WorkSynchronizationService(new Authorization(), reader).ReadAsync(Board, Actor, 0,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(Actor, result.Value!.Events[0].ActorId); Assert.Null(result.Value.Events[1].ActorId);
    }

    [Theory]
    [InlineData("organization")]
    [InlineData("board")]
    [InlineData("sequence")]
    [InlineData("type")]
    public async Task FaultyAdapterCannotDiscloseAnotherScopeOrSkipEvents(string invalid)
    {
        var row = Row(1);
        row = invalid switch
        {
            "organization" => row with { Event = row.Event with { OrganizationId = Guid.NewGuid() } },
            "board" => row with { Event = row.Event with { BoardId = Guid.NewGuid() } },
            "sequence" => row with { Sequence = 2 },
            _ => row with { Event = row.Event with { EntityType = "Secret" } }
        };
        var reader = new Reader { Read = () => Task.FromResult(new WorkEventReadPage(row.Sequence, false, false, false, [row])) };
        var result = await new WorkSynchronizationService(new Authorization(), reader).ReadAsync(Board, Actor, 0,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(result.Succeeded); Assert.Equal("work_sync_unavailable", result.ErrorCode); Assert.Null(result.Value);
    }

    [Fact]
    public async Task BrowserCursorRetainsBigintPrecision()
    {
        const long since = 9007199254740992;
        var row = Row(since + 1);
        var reader = new Reader { Read = () => Task.FromResult(new WorkEventReadPage(row.Sequence, false, false, false, [row])) };
        var result = await new WorkSynchronizationService(new Authorization(), reader).ReadAsync(Board, Actor, since,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("9007199254740993", result.Value!.Cursor);
        Assert.Equal(result.Value.Cursor, Assert.Single(result.Value.Events).Sequence);
    }

    private sealed class Authorization : IWorkBoardAuthorization
    {
        public bool Denied { get; set; }
        public BoardSyncScope Current { get; init; } = Scope();
        public int Calls { get; private set; }
        public Task<WorkOperation<BoardSyncScope>> GetSyncScopeAsync(Guid boardId, Guid? actorId, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(Denied ? WorkOperation<BoardSyncScope>.Failure("board_not_found") : WorkOperation<BoardSyncScope>.Success(Current));
        }
    }
    private sealed class Reader : IWorkEventReader
    {
        public Func<Task<WorkEventReadPage>> Read { get; init; } = () => Task.FromResult(new WorkEventReadPage(0, false, false, false, []));
        public int Calls { get; private set; }
        public Task<WorkEventReadPage> ReadAsync(Guid organizationId, Guid boardId, long since, int limit, CancellationToken cancellationToken = default)
        { Calls++; return Read(); }
    }
}
