using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StrataAI.Application.WorkManagement;

internal static class ActivityEventSourceStoreContract
{
    private static void Require(bool condition, string invariant)
    { if (!condition) throw new InvalidOperationException(invariant); }
    public static async Task RunAsync(NpgsqlConnection admin, IServiceProvider provider, Guid tenant, Guid actor, CancellationToken ct)
    {
        var board = Guid.NewGuid(); var otherBoard = Guid.NewGuid(); var at = AttachmentMetadataMapping.DatabaseTimestamp(DateTimeOffset.UtcNow);
        var sources = provider.GetRequiredService<IActivityEventSourceStore>(); var events = provider.GetRequiredService<IWorkEventStore>();
        var unit = provider.GetRequiredService<IWorkManagementUnitOfWork>(); string originalCaption; string originalStatus;
        await using (var profile = new NpgsqlCommand("SELECT display_name,status FROM users WHERE id=@actor;", admin))
        {
            profile.Parameters.AddWithValue("actor", actor); await using var row = await profile.ExecuteReaderAsync(ct);
            Require(await row.ReadAsync(ct), "Activity actor fixture is unavailable."); originalCaption = row.GetString(0); originalStatus = row.GetString(1);
        }
        async Task<T> Scope<T>(Func<Task<T>> action)
        {
            var result = await unit.ExecuteReadAsync(tenant, null, "fixture_denied", () => Task.FromResult(true),
                async () => WorkOperation<T>.Success(await action()), ct);
            Require(result.Succeeded, "Activity owning operation failed."); return result.Value!;
        }
        await using (var seed = new NpgsqlCommand("INSERT INTO boards(id,tenant_id,name,created_at,updated_at) VALUES(@board,@tenant,'Activity source contract',@at,@at),(@other_board,@tenant,'Historical activity source contract',@at,@at);", admin))
        {
            seed.Parameters.AddWithValue("board", board); seed.Parameters.AddWithValue("tenant", tenant); seed.Parameters.AddWithValue("at", at);
            seed.Parameters.AddWithValue("other_board", otherBoard);
            await seed.ExecuteNonQueryAsync(ct);
        }
        try
        {
            try { await sources.ReadBoardWindowAsync(tenant, board, null, null, ct); throw new InvalidOperationException("Unowned activity source read accepted."); }
            catch (InvalidOperationException exception) when (exception.Message == "Activity sources require the owning Work transaction.") { }
            var recorded = new List<WorkEvent>();
            await Scope(async () =>
            {
                for (var index = 1; index <= 65; index++)
                {
                    var change = new WorkEvent(Guid.NewGuid(), tenant, board, actor, "BOARD_UPDATED", "Board", board, index, "activity-source-contract", at);
                    recorded.Add(change); await events.AppendAsync(change, ct);
                }
                return true;
            });
            var first = await Scope(() => sources.ReadBoardWindowAsync(tenant, board, null, null, ct));
            Require(first.Count == 51 && first.All(row => row.ActorLabel == originalCaption && row.Metadata.Count == 0), "Activity bounded window or captured attribution failed.");
            Require(first.Select(row => row.EventId).SequenceEqual(recorded.Select(row => row.EventId).OrderByDescending(id => id.ToString("N"), StringComparer.Ordinal).Take(51)),
                "Activity same-time source identities use inconsistent ordering.");
            var anchor = first[49]; var tail = await Scope(() => sources.ReadBoardWindowAsync(tenant, board, anchor.CreatedAt, anchor.EventId, ct));
            Require(tail.Count == 15 && first.Take(50).Concat(tail).Select(row => row.EventId).Distinct().Count() == 65, "Activity timestamp/ID seek lost or duplicated a tie.");
            Require(first.SequenceEqual(await Scope(() => sources.ReadBoardWindowAsync(tenant, board, null, null, ct))), "Activity repeated read changed historical fields.");
            await using (var rename = new NpgsqlCommand("UPDATE users SET display_name='Later activity actor',status='DEACTIVATED' WHERE id=@actor;", admin))
            { rename.Parameters.AddWithValue("actor", actor); await rename.ExecuteNonQueryAsync(ct); }
            await Scope(async () => { await events.AppendAsync(recorded[0], ct); return true; });
            Require((await Scope(() => sources.ReadBoardWindowAsync(tenant, board, null, null, ct))).All(row => row.ActorLabel == originalCaption),
                "Activity read/retry rebound history to a renamed/deactivated account.");
            var failed = new WorkEvent(Guid.NewGuid(), tenant, board, actor, "BOARD_UPDATED", "Board", board, 66, "activity-source-rollback", at.AddSeconds(1));
            var refused = await unit.ExecuteReadAsync(tenant, null, "fixture_denied", () => Task.FromResult(true), async () =>
            { await events.AppendAsync(failed, ct); return WorkOperation<bool>.Failure("fixture_refused"); }, ct);
            Require(refused.ErrorCode == "fixture_refused" && !(await Scope(() => sources.ReadBoardWindowAsync(tenant, board, null, null, ct))).Any(row => row.EventId == failed.EventId),
                "Activity source survived owning rollback.");
            await Scope(async () => { await events.AppendAsync(failed, ct); return true; });
            Require((await Scope(() => sources.ReadBoardWindowAsync(tenant, board, null, null, ct)))[0].ActorLabel == "Later activity actor", "New activity did not capture its own event-time caption.");
            Require((await Scope(() => sources.ReadBoardWindowAsync(tenant, Guid.NewGuid(), null, null, ct))).Count == 0, "Activity Board affinity widened.");
            try { await sources.ReadCardWindowAsync(tenant, board, board, null, null, ct); throw new InvalidOperationException("Unowned Card activity source read accepted."); }
            catch (InvalidOperationException exception) when (exception.Message == "Activity sources require the owning Work transaction.") { }
            // Raw Card identities intentionally overlap Board identities: entity
            // type and source-Board boundaries must filter before the row limit.
            var cardRecorded = new List<WorkEvent>();
            await Scope(async () =>
            {
                for (var index = 1; index <= 65; index++)
                {
                    var change = new WorkEvent(Guid.NewGuid(), tenant, board, actor, "CARD_UPDATED", "Card", board, index, "card-activity-source-contract", at.AddSeconds(2));
                    cardRecorded.Add(change); await events.AppendAsync(change, ct);
                }
                await events.AppendAsync(new(Guid.NewGuid(), tenant, board, actor, "BOARD_UPDATED", "Board", board, 67, "card-activity-type-boundary", at.AddSeconds(3)), ct);
                await events.AppendAsync(new(Guid.NewGuid(), tenant, board, actor, "CARD_UPDATED", "Card", Guid.NewGuid(), 1, "card-activity-identity-boundary", at.AddSeconds(3)), ct);
                await events.AppendAsync(new(Guid.NewGuid(), tenant, otherBoard, actor, "CARD_UPDATED", "Card", board, 66, "card-activity-historical-board", at.AddSeconds(3)), ct);
                return true;
            });
            var cardFirst = await Scope(() => sources.ReadCardWindowAsync(tenant, board, board, null, null, ct));
            Require(cardFirst.Count == 51 && cardFirst.All(row => row.EntityType == "Card" && row.EntityId == board && row.BoardId == board), "Card activity source type/identity/Board boundary or bound failed.");
            Require(cardFirst.Select(row => row.EventId).SequenceEqual(cardRecorded.Select(row => row.EventId).OrderByDescending(id => id.ToString("N"), StringComparer.Ordinal).Take(51)), "Card activity tied order failed.");
            var cardAnchor = cardFirst[49];
            var cardTail = await Scope(() => sources.ReadCardWindowAsync(tenant, board, board, cardAnchor.CreatedAt, cardAnchor.EventId, ct));
            Require(cardTail.Count == 15 && cardFirst.Take(50).Concat(cardTail).Select(row => row.EventId).Distinct().Count() == 65, "Card activity tied seek lost or duplicated a source.");
            Require((await Scope(() => sources.ReadCardWindowAsync(tenant, otherBoard, board, null, null, ct))).Count == 1, "Explicit historical Card Board slice was unavailable.");
            Require((await Scope(() => sources.ReadCardWindowAsync(tenant, board, Guid.NewGuid(), null, null, ct))).Count == 0, "Card activity identity widened.");
            Console.WriteLine("Real restricted activity source adapter: bounded same-time seek, immutable captions after rename/deactivation, pending source visibility, exact retry, owning rollback and recovery passed. Raw sources do not establish HTTP/feed audience admission.");
            Console.WriteLine("Real restricted Card activity source slices: bounded tied seek, entity-type/identity/source-Board isolation and explicit historical Board reads passed. Synthetic source setup does not prove actual Card movement or authorized feeds.");
        }
        finally
        {
            await using var cleanup = new NpgsqlCommand("""
                UPDATE users SET display_name=@caption,status=@status WHERE id=@actor;
                DELETE FROM background_jobs WHERE tenant_id=@tenant AND safe_metadata->>'boardId'=ANY(@board_texts);
                DELETE FROM work_events WHERE tenant_id=@tenant AND board_id=ANY(@boards);
                DELETE FROM work_event_streams WHERE tenant_id=@tenant AND board_id=ANY(@boards);
                DELETE FROM boards WHERE tenant_id=@tenant AND id=ANY(@boards);
                """, admin);
            cleanup.Parameters.AddWithValue("caption", originalCaption); cleanup.Parameters.AddWithValue("status", originalStatus);
            cleanup.Parameters.AddWithValue("actor", actor); cleanup.Parameters.AddWithValue("tenant", tenant);
            cleanup.Parameters.AddWithValue("boards", new[] { board, otherBoard });
            cleanup.Parameters.AddWithValue("board_texts", new[] { board.ToString("D"), otherBoard.ToString("D") });
            await cleanup.ExecuteNonQueryAsync(ct);
        }
    }
}
