using StrataAI.Application.Organizations;

namespace StrataAI.Domain.Tests;

public sealed class OrganizationMetadataReplayTests
{
    private static readonly Guid Organization = Guid.NewGuid(), Actor = Guid.NewGuid();
    private static OrganizationMetadataEventCandidate Row(long sequence, bool ready = true) => new(sequence,
        new(Guid.NewGuid(), "ORGANIZATION_UPDATED", Actor, Organization, sequence + 1, DateTimeOffset.UtcNow), ready);
    [Fact]
    public void PRD_03_Metadata_replay_never_skips_an_earlier_pending_event()
    {
        var blocked = OrganizationMetadataEventWindow.Build(0, 2, 2, [Row(1, false), Row(2)]);
        Assert.True(blocked.Pending); Assert.Equal(0, blocked.Position); Assert.Empty(blocked.Events);
        var prefix = OrganizationMetadataEventWindow.Build(0, 3, 3, [Row(1), Row(2, false), Row(3)]);
        Assert.True(prefix.Pending); Assert.Equal(1, prefix.Position); Assert.Single(prefix.Events);
    }
    [Fact]
    public void PRD_03_Metadata_replay_pages_only_contiguous_ready_prefixes()
    {
        var first = OrganizationMetadataEventWindow.Build(0, 2, 1, [Row(1), Row(2)]);
        Assert.True(first.HasMore); Assert.False(first.Pending); Assert.Equal(1, first.Position); Assert.Single(first.Events);
        var final = OrganizationMetadataEventWindow.Build(first.Position, 2, 1, [Row(2)]);
        Assert.False(final.HasMore); Assert.Equal(2, final.Position); Assert.Single(final.Events);
        var empty = OrganizationMetadataEventWindow.Build(2, 2, 1, []);
        Assert.Equal(2, empty.Position); Assert.Empty(empty.Events); Assert.False(empty.ResetRequired);
    }
    [Fact]
    public void PRD_03_Metadata_missing_duplicate_reordered_or_future_history_requires_snapshot_reset()
    {
        foreach (var rows in new IReadOnlyList<OrganizationMetadataEventCandidate>[] { [], [Row(2)], [Row(1), Row(1)], [Row(1), Row(3)] })
        {
            var result = OrganizationMetadataEventWindow.Build(0, 3, 3, rows);
            Assert.True(result.ResetRequired); Assert.Empty(result.Events);
        }
        Assert.True(OrganizationMetadataEventWindow.Build(4, 3, 3, []).ResetRequired);
        Assert.True(OrganizationMetadataEventWindow.Build(long.MaxValue, long.MaxValue, 1, [Row(long.MaxValue - 1)]).ResetRequired);
    }
    [Fact]
    public async Task PRD_03_Metadata_bootstrap_and_invalid_cursor_require_snapshot_without_historical_payload()
    {
        var reader = new Reader(); var codec = new Codec(); var service = new OrganizationMetadataSynchronizationService(reader, codec);
        foreach (var cursor in new string?[] { null, "invalid" })
        {
            var page = await service.ReadAsync(Organization, Actor, cursor, cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(page.Succeeded); Assert.True(page.Value!.ResetRequired); Assert.Empty(page.Value.Events);
            Assert.True(codec.TryDecode(reader.Binding!, page.Value.Cursor, out var position)); Assert.Equal(2, position);
        }
        Assert.Equal(0, reader.EventReads);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PRD_03_Metadata_final_membership_withdrawal_or_revision_change_discards_entire_page(bool revision)
    {
        var reader = new Reader(); var codec = new Codec(); var cursor = codec.Encode(reader.Binding!, 0);
        reader.OnRead = () => reader.Binding = revision ? reader.Binding! with { MembershipVersion = 2 } : null;
        var result = await new OrganizationMetadataSynchronizationService(reader, codec).ReadAsync(Organization, Actor, cursor,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(result.Succeeded); Assert.Null(result.Value);
        Assert.Equal(revision ? "organization_sync_unavailable" : "organization_not_found", result.ErrorCode);
    }
    [Fact]
    public async Task PRD_03_Metadata_payload_is_content_free_and_uses_original_source_identity()
    {
        var reader = new Reader(); var codec = new Codec(); var cursor = codec.Encode(reader.Binding!, 0);
        var result = await new OrganizationMetadataSynchronizationService(reader, codec).ReadAsync(Organization, Actor, cursor,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded);
        var envelope = Assert.Single(result.Value!.Events); Assert.Null(envelope.BoardId);
        Assert.Equal(Organization, envelope.OrganizationId); Assert.Equal(Organization, envelope.EntityId);
        Assert.Equal(Actor, envelope.ActorId); Assert.Equal("Organization", envelope.EntityType); Assert.Empty(envelope.Metadata);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task PRD_03_Metadata_replay_refuses_unbounded_limits(int limit)
    {
        var reader = new Reader();
        var result = await new OrganizationMetadataSynchronizationService(reader, new Codec()).ReadAsync(Organization, Actor, null, limit,
            TestContext.Current.CancellationToken);
        Assert.Equal("invalid_sync_limit", result.ErrorCode); Assert.Equal(0, reader.EventReads);
    }
    [Fact]
    public async Task PRD_03_Metadata_delivery_rechecks_cursor_authority_after_session_IO()
    {
        var reader = new Reader(); var codec = new Codec();
        var service = new OrganizationMetadataSynchronizationService(reader, codec);
        var ct = TestContext.Current.CancellationToken;
        var cursor = codec.Encode(reader.Binding!, 1);
        Assert.True((await service.IsCursorCurrentAsync(Organization, Actor, cursor, ct)).Value);
        reader.Binding = reader.Binding! with { MembershipVersion = 2 };
        Assert.False((await service.IsCursorCurrentAsync(Organization, Actor, cursor, ct)).Value);
        reader.Binding = null;
        Assert.Equal("organization_not_found", (await service.IsCursorCurrentAsync(Organization, Actor, cursor, ct)).ErrorCode);
        Assert.Equal(0, reader.EventReads);
    }
    [Theory]
    [InlineData("ORGANIZATION_MEMBER_ADDED", 1)]
    [InlineData("ORGANIZATION_MEMBER_ADDED", 4)]
    [InlineData("ORGANIZATION_MEMBER_REMOVED", 2)]
    [InlineData("ORGANIZATION_MEMBER_REMOVED", 4)]
    [InlineData("ORGANIZATION_MEMBER_INVITED", 1)]
    [InlineData("INVITATION_REVOKED", 2)]
    [InlineData("INVITATION_REVOKED", 4)]
    [InlineData("INVITATION_ACCEPTED", 2)]
    [InlineData("INVITATION_ACCEPTED", 4)]
    public async Task PRD_03_Member_change_replay_preserves_subject_identity_and_revision(string eventType, long version)
    {
        var member = Guid.NewGuid(); var eventId = Guid.NewGuid();
        var reader = new Reader { Candidate = new(1, new(eventId, eventType, Actor,
            Organization, version, DateTimeOffset.UtcNow, eventType is "ORGANIZATION_MEMBER_INVITED" or "INVITATION_REVOKED" or "INVITATION_ACCEPTED" ? "Invitation" : "OrganizationMembership", member), true) };
        var codec = new Codec(); var service = new OrganizationMetadataSynchronizationService(reader, codec);
        var cursor = codec.Encode(reader.Binding!, 0); var ct = TestContext.Current.CancellationToken;
        var result = await service.ReadAsync(Organization, Actor, cursor, cancellationToken: ct);
        Assert.True(result.Succeeded);
        var row = Assert.Single(result.Value!.Events);
        Assert.Equal(eventId, row.EventId); Assert.Equal(member, row.EntityId); Assert.Equal(version, row.Version);
        Assert.Equal(eventType is "ORGANIZATION_MEMBER_INVITED" or "INVITATION_REVOKED" or "INVITATION_ACCEPTED" ? "Invitation" : "OrganizationMembership", row.EntityType); Assert.Empty(row.Metadata); Assert.Null(row.BoardId);
        foreach (var invalid in new[] { row with { EntityType = "Organization" }, row with { EntityId = Guid.Empty }, row with { EventType = "UNKNOWN" } })
        {
            reader.Candidate = new(1, invalid, true);
            Assert.Equal("organization_sync_unavailable", (await service.ReadAsync(Organization, Actor, cursor, cancellationToken: ct)).ErrorCode);
        }
    }
    private sealed class Reader : IOrganizationMetadataEventReader
    {
        public OrganizationMetadataEventCandidate? Candidate { get; set; }
        public OrganizationMetadataCursorBinding? Binding { get; set; } = new(Organization, Actor, Guid.NewGuid(), 1);
        public Action? OnRead { get; set; }
        public int EventReads { get; private set; }
        public Task<OrganizationMetadataCursorBinding?> GetScopeAsync(Guid org, Guid actor, CancellationToken ct) => Task.FromResult(Binding);
        public Task<long> GetHeadAsync(Guid org, CancellationToken ct) => Task.FromResult(2L);
        public Task<OrganizationMetadataEventWindow> ReadAsync(Guid org, long since, int limit, CancellationToken ct)
        { EventReads++; OnRead?.Invoke(); return Task.FromResult(new OrganizationMetadataEventWindow(1, true, false, false, [Candidate ?? Row(1)])); }
    }
    private sealed class Codec : IOrganizationMetadataCursorCodec
    {
        private readonly Dictionary<string, (OrganizationMetadataCursorBinding Binding, long Position)> _tokens = [];
        public string Encode(OrganizationMetadataCursorBinding binding, long position)
        { var token = Guid.NewGuid().ToString("N"); _tokens.Add(token, (binding, position)); return token; }
        public bool TryDecode(OrganizationMetadataCursorBinding binding, string token, out long position)
        { position = 0; if (!_tokens.TryGetValue(token, out var value) || value.Binding != binding) return false; position = value.Position; return true; }
    }
}
