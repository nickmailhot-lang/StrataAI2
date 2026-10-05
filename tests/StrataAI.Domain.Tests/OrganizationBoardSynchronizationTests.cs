using StrataAI.Application.WorkManagement;

namespace StrataAI.Domain.Tests;

public sealed class OrganizationBoardSynchronizationTests
{
    private static readonly Guid Organization = Guid.NewGuid(), Actor = Guid.NewGuid();
    private static OrganizationBoardCursorBinding Scope() => new(Organization, Actor, Guid.NewGuid(), 1, Guid.NewGuid(), 1);

    [Fact]
    public async Task DeniedScopeDoesNotReadOrIssueCursor()
    {
        var reader = new Reader { Binding = null }; var codec = new Codec();
        var result = await new OrganizationBoardSynchronizationService(reader, codec).ReadAsync(Organization, Actor, null,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("organization_not_found", result.ErrorCode);
        Assert.Equal(0, reader.HeadReads); Assert.Equal(0, reader.EventReads); Assert.Equal(0, codec.Encoded);
    }

    [Fact]
    public async Task BootstrapAndChangedPermissionTokenRequireFreshSnapshotWithoutOldEnvelopes()
    {
        var reader = new Reader(); var codec = new Codec();
        var service = new OrganizationBoardSynchronizationService(reader, codec);
        var old = codec.Encode(reader.Binding!, 3);
        reader.Binding = reader.Binding! with { PermissionRevision = 2 };
        foreach (var token in new string?[] { null, old, "damaged" })
        {
            var result = await service.ReadAsync(Organization, Actor, token, cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(result.Succeeded); Assert.True(result.Value!.ResetRequired); Assert.Empty(result.Value.Events);
            Assert.True(codec.TryDecode(reader.Binding!, result.Value.Cursor, out var position)); Assert.Equal(7, position);
        }
        Assert.Equal(0, reader.EventReads); Assert.Equal(3, reader.HeadReads);
    }

    [Fact]
    public async Task WithdrawalDuringEmptyBootstrapRejectsPageAndCursor()
    {
        var reader = new Reader(); var codec = new Codec();
        reader.OnHead = async () => { await Task.Yield(); reader.Binding = null; return 7; };
        var result = await new OrganizationBoardSynchronizationService(reader, codec).ReadAsync(Organization, Actor, null,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("organization_not_found", result.ErrorCode); Assert.Null(result.Value); Assert.Equal(0, codec.Encoded);
        Assert.Equal(2, reader.ScopeReads);
    }

    [Fact]
    public async Task GrantWithdrawalAndRestorationDuringReplayDiscardsWholePage()
    {
        var reader = new Reader(); var codec = new Codec();
        var token = codec.Encode(reader.Binding!, 3);
        reader.OnRead = async () =>
        {
            await Task.Yield(); reader.Binding = reader.Binding! with { PermissionRevision = 3 };
            return WorkOperation<OrganizationBoardEventPage>.Success(Page());
        };
        var result = await new OrganizationBoardSynchronizationService(reader, codec).ReadAsync(Organization, Actor, token,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("organization_sync_unavailable", result.ErrorCode); Assert.Null(result.Value);
        Assert.Equal(1, codec.Encoded); Assert.Equal(2, reader.ScopeReads);
    }

    [Fact]
    public async Task AdmittedReplayPreservesSourceIdentityWithOnlyOpaquePageCursor()
    {
        var reader = new Reader(); var codec = new Codec(); var page = Page();
        reader.OnRead = () => Task.FromResult(WorkOperation<OrganizationBoardEventPage>.Success(page));
        var result = await new OrganizationBoardSynchronizationService(reader, codec).ReadAsync(Organization, Actor,
            codec.Encode(reader.Binding!, 3), cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded); Assert.Single(result.Value!.Events);
        Assert.Equal(page.Events[0].EventId, result.Value.Events[0].EventId);
        Assert.Equal(page.Events[0].BoardId, result.Value.Events[0].BoardId);
        Assert.True(codec.TryDecode(reader.Binding!, result.Value.Cursor, out var position)); Assert.Equal(7, position);
        Assert.Equal(0, reader.HeadReads);
    }

    private static OrganizationBoardEventPage Page() => new(7, false, false, false,
        [new(7, Guid.NewGuid(), Guid.NewGuid(), "BOARD_ARCHIVED", 2, DateTimeOffset.UtcNow, true)]);
    private sealed class Reader : IOrganizationBoardEventReader
    {
        public OrganizationBoardCursorBinding? Binding = Scope();
        public int ScopeReads, HeadReads, EventReads;
        public Func<Task<long>> OnHead = () => Task.FromResult(7L);
        public Func<Task<WorkOperation<OrganizationBoardEventPage>>> OnRead = () => Task.FromResult(WorkOperation<OrganizationBoardEventPage>.Success(Page()));
        public Task<OrganizationBoardCursorBinding?> GetScopeAsync(Guid organizationId, Guid actorId, CancellationToken cancellationToken = default)
        { ScopeReads++; return Task.FromResult(Binding); }
        public Task<long> GetHeadAsync(Guid organizationId, CancellationToken cancellationToken = default)
        { HeadReads++; return OnHead(); }
        public Task<WorkOperation<OrganizationBoardEventPage>> ReadAsync(Guid organizationId, Guid actorId, long since, int limit, CancellationToken cancellationToken = default)
        { EventReads++; return OnRead(); }
    }
    private sealed class Codec : IOrganizationBoardCursorCodec
    {
        private readonly Dictionary<string, (OrganizationBoardCursorBinding Binding, long Position)> _tokens = [];
        public int Encoded;
        public string Encode(OrganizationBoardCursorBinding binding, long position)
        {
            Encoded++; var token = Guid.NewGuid().ToString("N"); _tokens.Add(token, (binding, position)); return token;
        }
        public bool TryDecode(OrganizationBoardCursorBinding binding, string token, out long position)
        {
            position = 0;
            if (!_tokens.TryGetValue(token, out var stored) || stored.Binding != binding) return false;
            position = stored.Position; return true;
        }
    }
}
