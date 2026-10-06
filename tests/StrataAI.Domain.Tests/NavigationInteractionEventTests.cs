using System.Text.Json;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Domain.Tests;

public sealed class NavigationInteractionEventTests
{
    [Fact]
    public void PRD_01_sources_retain_actual_targets_without_browsing_content()
    {
        var actor = Guid.NewGuid(); var org = Guid.NewGuid(); var board = Guid.NewGuid(); var card = Guid.NewGuid();
        var at = DateTimeOffset.UtcNow;
        var sources = new[] {
            NavigationInteractionEvent.ApplicationContextChanged(Guid.NewGuid(), actor, null, at),
            NavigationInteractionEvent.ApplicationContextChanged(Guid.NewGuid(), actor, org, at),
            NavigationInteractionEvent.BoardOpened(Guid.NewGuid(), actor, org, board, 3, at),
            NavigationInteractionEvent.CardOpened(Guid.NewGuid(), actor, org, board, card, 7, at) };
        Assert.Null(sources[0].OrganizationId); Assert.Null(sources[0].BoardId);
        Assert.Equal(org, sources[1].EntityId); Assert.Equal(board, sources[2].EntityId);
        Assert.Equal(card, sources[3].EntityId); Assert.Equal(7, sources[3].Version);
        foreach (var source in sources) {
            Assert.Equal(actor, source.ActorId); Assert.Empty(source.Metadata);
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(source));
            Assert.Equal(10, json.RootElement.EnumerateObject().Count());
            Assert.Equal(TimeSpan.Zero, source.CreatedAt.Offset); Assert.Equal(0, source.CreatedAt.UtcTicks % 10);
        }
        Assert.Throws<ArgumentException>(() => NavigationInteractionEvent.CardOpened(Guid.NewGuid(), actor, org, board, Guid.Empty, 1, at));
        Assert.Throws<ArgumentException>(() => NavigationInteractionEvent.BoardOpened(Guid.NewGuid(), actor, Guid.Empty, board, 1, at));
        Assert.Throws<ArgumentException>(() => NavigationInteractionEvent.BoardOpened(Guid.NewGuid(), actor, org, board, 0, at));
        Assert.Throws<ArgumentException>(() => NavigationInteractionEvent.ApplicationContextChanged(Guid.NewGuid(), Guid.Empty, org, at));
    }
}
