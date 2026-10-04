using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StrataAI.Application.WorkManagement;

// Real restricted adapters in a synthetic admitted scope. This deliberately
// refuses the transaction; it does not claim cookie/session command admission.
internal static class CommentMentionNotificationContract
{
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    public static async Task RunAsync(NpgsqlConnection admin, IServiceProvider provider, Guid tenant, Guid card,
        Guid author, Guid recipient, CancellationToken ct)
    {
        var unit = provider.GetRequiredService<IWorkManagementUnitOfWork>();
        var comments = provider.GetRequiredService<ICardCommentStore>(); var snapshots = provider.GetRequiredService<ICommentMentionSnapshotStore>();
        var work = provider.GetRequiredService<IWorkManagementStore>(); var events = provider.GetRequiredService<IWorkEventStore>();
        var notifications = provider.GetRequiredService<IWorkNotificationStore>();
        var id = Guid.NewGuid(); var eventId = Guid.NewGuid();
        await using var grant = new NpgsqlCommand("""
            INSERT INTO organization_members(id,tenant_id,user_id,role,status,created_at,updated_at)
            VALUES(@id,@tenant,@recipient,'MEMBER','ACTIVE',clock_timestamp(),clock_timestamp());
            """, admin);
        grant.Parameters.AddWithValue("tenant", tenant); grant.Parameters.AddWithValue("recipient", recipient);
        grant.Parameters.AddWithValue("id", Guid.NewGuid());
        await grant.ExecuteNonQueryAsync(ct);
        try
        {
            var original = await work.FindCardAsync(card, ct); Require(original is not null, "Mention fixture Card was absent.");
            var result = await unit.ExecuteReadAsync(tenant, null, "fixture_denied", () => Task.FromResult(true), async () =>
            {
                var now = AttachmentMetadataMapping.DatabaseTimestamp(DateTimeOffset.UtcNow.AddMinutes(1));
                var updated = await work.UpdateCardAsync(card, original!.Title, original.Description, original.Version, now, ct);
                Require(updated is not null, "Mention fixture Card did not advance.");
                var comment = await comments.CreateAsync(id, tenant, card, author, "Private mention fixture", now, ct);
                await snapshots.AppendSnapshotAsync(new(tenant, card, id, 1, comment.UpdatedAt, [recipient]), ct);
                var change = new WorkEvent(eventId, tenant, original.BoardId, author, "MENTION_CREATED", "Card", card,
                    updated!.Version, "mention-storage-fixture", now);
                try
                {
                    await notifications.AppendCardMentionAsync(change, recipient, ct);
                    throw new InvalidOperationException("Absent mention source event was accepted.");
                }
                catch (InvalidOperationException e) when (e.Message == "Assignment notification event was unavailable or reused.") { }
                await events.AppendAsync(change, ct);
                await notifications.AppendCardMentionsAsync(change, [recipient, author], ct);
                await notifications.AppendCardMentionAsync(change, recipient, ct);
                await notifications.AppendCardMentionAsync(change, recipient, ct);
                await notifications.AppendCardMentionsAsync(change, [recipient, author], ct);
                var items = await notifications.ListCardNotificationsAsync(tenant, recipient, cancellationToken: ct);
                var item = items.Single(row => row.EventId == eventId);
                Require(item.NotificationType == "MENTION_CREATED" && item.CardId == card && item.ActorId == author,
                    "Mention notification source scope or type changed.");
                await notifications.AppendCardMentionAsync(change, author, ct);
                Require(!(await notifications.ListCardNotificationsAsync(tenant, author, cancellationToken: ct)).Any(row => row.EventId == eventId),
                    "Mention self notification was persisted.");
                try
                {
                    await notifications.AppendCardMentionAsync(change with { Version = change.Version + 1 }, recipient, ct);
                    throw new InvalidOperationException("Mismatched mention source revision was accepted.");
                }
                catch (InvalidOperationException e) when (e.Message == "Assignment notification event was unavailable or reused.") { }
                try
                {
                    await notifications.AppendCardMentionsAsync(change with { CreatedAt = change.CreatedAt.AddSeconds(1) }, [recipient], ct);
                    throw new InvalidOperationException("Mismatched batch source time was accepted.");
                }
                catch (InvalidOperationException e) when (e.Message == "Assignment notification event was unavailable or reused.") { }
                return WorkOperation<bool>.Failure("fixture_refused");
            }, ct);
            Require(result.ErrorCode == "fixture_refused", "Mention storage fixture did not reach deliberate rollback.");
            Require(await work.FindCardAsync(card, ct) == original, "Refused mention publication retained Card revision.");
            await using var retained = new NpgsqlCommand("""
                SELECT EXISTS(SELECT 1 FROM card_comments WHERE tenant_id=@tenant AND id=@comment)
                OR EXISTS(SELECT 1 FROM comment_mention_snapshots WHERE tenant_id=@tenant AND comment_id=@comment)
                OR EXISTS(SELECT 1 FROM comment_mention_recipients WHERE tenant_id=@tenant AND comment_id=@comment)
                OR EXISTS(SELECT 1 FROM work_events WHERE tenant_id=@tenant AND event_id=@event)
                OR EXISTS(SELECT 1 FROM card_assignment_notifications WHERE tenant_id=@tenant AND event_id=@event)
                OR EXISTS(SELECT 1 FROM background_jobs WHERE tenant_id=@tenant AND safe_metadata->>'eventId'=@eventText);
                """, admin);
            retained.Parameters.AddWithValue("tenant", tenant); retained.Parameters.AddWithValue("comment", id);
            retained.Parameters.AddWithValue("event", eventId); retained.Parameters.AddWithValue("eventText", eventId.ToString("D"));
            Require(await retained.ExecuteScalarAsync(ct) is false, "Refused mention publication retained dependent effects.");
        }
        finally
        {
            await using var remove = new NpgsqlCommand("DELETE FROM organization_members WHERE tenant_id=@tenant AND user_id=@recipient;", admin);
            remove.Parameters.AddWithValue("tenant", tenant); remove.Parameters.AddWithValue("recipient", recipient);
            await remove.ExecuteNonQueryAsync(ct);
        }
        Console.WriteLine("Restricted mention publication: exact source event, duplicate identity, self suppression, mismatched revision refusal and atomic Card/comment/snapshot/event/notification/delivery rollback passed.");
    }
}
