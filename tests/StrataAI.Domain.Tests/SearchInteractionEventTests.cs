using System.Text.Json;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Domain.Tests;

public sealed class SearchInteractionEventTests
{
    [Fact]
    public void Global_source_has_no_fabricated_tenant_and_serializes_only_canonical_personal_fields()
    {
        var id = Guid.NewGuid(); var actor = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.FromHours(-7)).AddTicks(17);
        var source = SearchInteractionEvent.SearchExecuted(id, actor, at);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(source));
        Assert.Equal(new[] { "ActorId", "BoardId", "CreatedAt", "EntityId", "EntityType", "EventId", "EventType", "Metadata", "OrganizationId", "Version" },
            json.RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.Null(source.OrganizationId); Assert.Null(source.BoardId); Assert.Equal(id, source.EntityId);
        Assert.Equal(actor, source.ActorId); Assert.Equal("SEARCH_EXECUTED", source.EventType);
        Assert.Empty(json.RootElement.GetProperty("Metadata").EnumerateObject());
        Assert.Equal(TimeSpan.Zero, source.CreatedAt.Offset); Assert.Equal(0, source.CreatedAt.UtcTicks % 10);
        Assert.Equal(at.UtcTicks - 7, source.CreatedAt.UtcTicks);
    }

    [Fact]
    public void Board_source_requires_real_scope_and_cannot_carry_query_or_result_metadata()
    {
        var actor = Guid.NewGuid(); var organization = Guid.NewGuid(); var board = Guid.NewGuid();
        var at = DateTimeOffset.UtcNow;
        var source = SearchInteractionEvent.BoardFilterChanged(Guid.NewGuid(), actor, organization, board, at);
        Assert.Equal(organization, source.OrganizationId); Assert.Equal(board, source.BoardId);
        Assert.Equal("BOARD_FILTER_CHANGED", source.EventType); Assert.Equal("BoardFilter", source.EntityType);
        Assert.Equal(1, source.Version); Assert.Empty(source.Metadata);
        Assert.Throws<ArgumentException>(() => SearchInteractionEvent.BoardFilterChanged(Guid.NewGuid(), actor, Guid.Empty, board, at));
        Assert.Throws<ArgumentException>(() => SearchInteractionEvent.BoardFilterChanged(Guid.NewGuid(), actor, organization, Guid.Empty, at));
        Assert.Throws<ArgumentException>(() => SearchInteractionEvent.SearchExecuted(Guid.Empty, actor, at));
        Assert.Throws<ArgumentException>(() => SearchInteractionEvent.SearchExecuted(Guid.NewGuid(), Guid.Empty, at));
        Assert.Throws<ArgumentException>(() => SearchInteractionEvent.SearchExecuted(Guid.NewGuid(), actor, default));
    }
}
