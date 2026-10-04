using System.Text.Json;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Domain.Tests;

public sealed class NotificationRealtimeEventTests
{
    private static CardNotification Notification() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 3, DateTimeOffset.Parse("2026-10-04T12:00:00.123456Z"), null);

    [Fact]
    public void Creation_preserves_private_recipient_and_historical_scope_without_projected_Card_content()
    {
        var n = Notification() with { CurrentBoardId = Guid.NewGuid() }; var eventId = Guid.NewGuid();
        var result = NotificationRealtimeEvent.Created(n, eventId, long.MaxValue);
        Assert.Equal(eventId, result.EventId); Assert.Equal("NOTIFICATION_CREATED", result.EventType);
        Assert.Equal(n.ActorId, result.ActorId); Assert.Equal(n.RecipientId, result.RecipientId);
        Assert.Equal(n.OrganizationId, result.OrganizationId); Assert.Equal(n.BoardId, result.BoardId);
        Assert.Equal(n.Id, result.EntityId); Assert.Equal("Notification", result.EntityType); Assert.Equal(1, result.Version);
        Assert.Equal("9223372036854775807", result.Sequence); Assert.Empty(result.Metadata); Assert.Equal(n.CreatedAt, result.CreatedAt);
        var json = JsonSerializer.Serialize(result);
        Assert.DoesNotContain(n.CardId.ToString(), json); Assert.DoesNotContain(n.CurrentBoardId!.Value.ToString(), json);
    }

    [Fact]
    public void Read_uses_recipient_actor_and_persisted_first_read_time_at_full_precision()
    {
        var original = Notification(); var read = original.CreatedAt.AddTicks(1); var n = original with { ReadAt = read };
        var result = NotificationRealtimeEvent.Read(n, Guid.NewGuid(), 2, read);
        Assert.Equal("NOTIFICATION_READ", result.EventType); Assert.Equal(n.RecipientId, result.ActorId);
        Assert.Equal(2, result.Version); Assert.Equal(read, result.CreatedAt); Assert.Empty(result.Metadata);
        Assert.Throws<ArgumentException>(() => NotificationRealtimeEvent.Read(n, Guid.NewGuid(), 3, read.AddTicks(1)));
        Assert.Throws<ArgumentException>(() => NotificationRealtimeEvent.Read(original, Guid.NewGuid(), 3, read));
    }

    [Fact]
    public void Invalid_scope_identity_and_order_cannot_create_a_journal_envelope()
    {
        var n = Notification();
        foreach (var bad in new[] { n with { Id = Guid.Empty }, n with { OrganizationId = Guid.Empty }, n with { BoardId = Guid.Empty },
            n with { RecipientId = Guid.Empty }, n with { ActorId = Guid.Empty }, n with { CardId = Guid.Empty },
            n with { EventId = Guid.Empty }, n with { CardVersion = 0 } })
            Assert.Throws<ArgumentException>(() => NotificationRealtimeEvent.Created(bad, Guid.NewGuid(), 1));
        Assert.Throws<ArgumentException>(() => NotificationRealtimeEvent.Created(n, Guid.Empty, 1));
        Assert.Throws<ArgumentException>(() => NotificationRealtimeEvent.Created(n, Guid.NewGuid(), 0));
        Assert.Throws<ArgumentException>(() => NotificationRealtimeEvent.Read(n with { ReadAt = n.CreatedAt.AddTicks(-1) }, Guid.NewGuid(), 1, n.CreatedAt.AddTicks(-1)));
    }
}
