using StrataAI.Application.Organizations;

namespace StrataAI.Domain.Tests;

public sealed class OrganizationConfigurationReplayTests
{
    private static readonly Guid Organization = Guid.NewGuid(), Actor = Guid.NewGuid();
    private static readonly DateTimeOffset SourceTime = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
    private static OrganizationConfigurationEventCandidate Row(long version, bool ready = true) => new(
        new(Guid.NewGuid(), Organization, Actor, version, "configuration-source", SourceTime),
        ready ? SourceTime.AddSeconds(3) : null);

    [Fact]
    public void PRD_27_Replay_blocks_out_of_order_publication_and_preserves_a_ready_prefix()
    {
        var blocked = OrganizationConfigurationEventWindow.Build(Organization, 0, 2, 2, [Row(1, false), Row(2)]);
        Assert.True(blocked.Pending); Assert.Equal(0, blocked.Position); Assert.Empty(blocked.Events);
        var first = Row(1);
        var prefix = OrganizationConfigurationEventWindow.Build(Organization, 0, 3, 3, [first, Row(2, false), Row(3)]);
        Assert.True(prefix.Pending); Assert.Equal(1, prefix.Position); Assert.Same(first, Assert.Single(prefix.Events));
        Assert.False(prefix.HasMore); Assert.False(prefix.ResetRequired);
    }

    [Fact]
    public void PRD_27_Replay_lookahead_does_not_advance_over_pending_delivery()
    {
        var first = Row(1); var second = Row(2);
        var page = OrganizationConfigurationEventWindow.Build(Organization, 0, 2, 1, [first, second]);
        Assert.True(page.HasMore); Assert.Equal(1, page.Position); Assert.Same(first, Assert.Single(page.Events));
        var pending = OrganizationConfigurationEventWindow.Build(Organization, 0, 2, 1, [first, Row(2, false)]);
        Assert.True(pending.Pending); Assert.False(pending.HasMore); Assert.Equal(1, pending.Position);
        var final = OrganizationConfigurationEventWindow.Build(Organization, page.Position, 2, 1, [second]);
        Assert.Equal(2, final.Position); Assert.False(final.HasMore); Assert.Same(second, Assert.Single(final.Events));
    }

    [Fact]
    public void PRD_27_Replay_missing_reordered_duplicate_and_future_history_discards_the_entire_window()
    {
        var first = Row(1);
        foreach (var rows in new IReadOnlyList<OrganizationConfigurationEventCandidate>[]
            { [], [Row(2)], [first, first], [Row(1), Row(3)], [first, Row(2) with { Source = first.Source with { Version = 2 } }] })
        {
            var result = OrganizationConfigurationEventWindow.Build(Organization, 0, 3, 3, rows);
            Assert.True(result.ResetRequired); Assert.Empty(result.Events); Assert.Equal(0, result.Position);
        }
        Assert.True(OrganizationConfigurationEventWindow.Build(Organization, 4, 3, 1, []).ResetRequired);
        Assert.True(OrganizationConfigurationEventWindow.Build(Organization, long.MaxValue, long.MaxValue, 1, [Row(1)]).ResetRequired);
    }

    [Fact]
    public void PRD_27_Replay_refuses_foreign_or_invalid_sources_and_a_readiness_clock_before_the_source()
    {
        var row = Row(1);
        foreach (var source in new[] { row.Source with { OrganizationId = Guid.NewGuid() }, row.Source with { ActorId = Guid.Empty },
            row.Source with { EventId = Guid.Empty }, row.Source with { CorrelationId = " " }, row.Source with { CorrelationId = new string('x', 257) } })
            Assert.True(OrganizationConfigurationEventWindow.Build(Organization, 0, 1, 1, [row with { Source = source }]).ResetRequired);
        Assert.True(OrganizationConfigurationEventWindow.Build(Organization, 0, 1, 1,
            [row with { ReadyAt = SourceTime.AddTicks(-1) }]).ResetRequired);
    }

    [Fact]
    public void PRD_27_Replay_preserves_original_source_identity_and_clock_without_configuration_fields()
    {
        var row = Row(1);
        var result = OrganizationConfigurationEventWindow.Build(Organization, 0, 1, 1, [row]);
        var delivered = Assert.Single(result.Events);
        Assert.Same(row.Source, delivered.Source); Assert.Equal(SourceTime, delivered.Source.CreatedAt);
        Assert.Equal(SourceTime.AddSeconds(3), delivered.ReadyAt);
        Assert.Equal("ORGANIZATION_CONFIGURATION_CHANGED", delivered.Source.EventType);
        Assert.Equal("OrganizationConfiguration", delivered.Source.EntityType); Assert.Equal(Organization, delivered.Source.EntityId);
        Assert.False(result.Pending); Assert.False(result.ResetRequired);
        Assert.Empty(OrganizationConfigurationEventWindow.Build(Organization, 0, 0, 1, []).Events);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(4, 0)]
    [InlineData(5, 0)]
    [InlineData(5, 10)]
    [InlineData(6, 10)]
    [InlineData(9, 10)]
    public void PRD_27_Replay_preserves_finer_JSON_source_clock_and_actual_microsecond_publication(int sourceTicks, int storedTicks)
    {
        var original = Row(1).Source with { CreatedAt = SourceTime.AddTicks(sourceTicks) };
        var stored = SourceTime.AddTicks(storedTicks);
        var candidate = new OrganizationConfigurationEventCandidate(original, stored, stored);
        var page = OrganizationConfigurationEventWindow.Build(Organization, 0, 1, 1, [candidate]);
        Assert.False(page.ResetRequired); Assert.Equal(1, page.Position);
        var delivered = Assert.Single(page.Events);
        Assert.Same(original, delivered.Source); Assert.Equal(original.CreatedAt, delivered.Source.CreatedAt);
        Assert.Equal(stored, delivered.ReadyAt); Assert.Equal(stored, delivered.StoredSourceTime);
        Assert.True(OrganizationConfigurationEventWindow.Build(Organization, 0, 1, 1,
            [candidate with { ReadyAt = stored.AddTicks(-1) }]).ResetRequired);
    }

    [Fact]
    public void PRD_27_Replay_refuses_unproven_stored_clock_precision_or_a_changed_source_clock()
    {
        var row = Row(1);
        foreach (var invalid in new[] { SourceTime.AddTicks(1), SourceTime.AddTicks(10),
            SourceTime.ToOffset(TimeSpan.FromHours(1)) })
            Assert.True(OrganizationConfigurationEventWindow.Build(Organization, 0, 1, 1,
                [row with { StoredSourceTime = invalid }]).ResetRequired);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void PRD_27_Replay_requires_bounded_pages(int limit) => Assert.Throws<ArgumentOutOfRangeException>(() =>
        OrganizationConfigurationEventWindow.Build(Organization, 0, 0, limit, []));
}
