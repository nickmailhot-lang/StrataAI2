using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StrataAI.Application.WorkManagement;

internal static class ActivityEventSourceStoreContract
{
    private static void Require(bool condition, string invariant)
    { if (!condition) throw new InvalidOperationException(invariant); }
    public static async Task RunAsync(NpgsqlConnection admin, IServiceProvider provider, Guid tenant, Guid actor, CancellationToken ct)
    {
        var board = Guid.NewGuid(); var at = AttachmentMetadataMapping.DatabaseTimestamp(DateTimeOffset.UtcNow);
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
        await using (var seed = new NpgsqlCommand("INSERT INTO boards(id,tenant_id,name,created_at,updated_at) VALUES(@board,@tenant,'Activity source contract',@at,@at);", admin))
        {
            seed.Parameters.AddWithValue("board", board); seed.Parameters.AddWithValue("tenant", tenant); seed.Parameters.AddWithValue("at", at);
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
            Console.WriteLine("Real restricted activity source adapter: bounded same-time seek, immutable captions after rename/deactivation, pending source visibility, exact retry, owning rollback and recovery passed. Raw sources do not establish HTTP/feed audience admission.");
        }
        finally
        {
            await using var cleanup = new NpgsqlCommand("""
                UPDATE users SET display_name=@caption,status=@status WHERE id=@actor;
                DELETE FROM background_jobs WHERE tenant_id=@tenant AND safe_metadata->>'boardId'=@board_text;
                DELETE FROM work_events WHERE tenant_id=@tenant AND board_id=@board;
                DELETE FROM work_event_streams WHERE tenant_id=@tenant AND board_id=@board;
                DELETE FROM boards WHERE tenant_id=@tenant AND id=@board;
                """, admin);
            cleanup.Parameters.AddWithValue("caption", originalCaption); cleanup.Parameters.AddWithValue("status", originalStatus);
            cleanup.Parameters.AddWithValue("actor", actor); cleanup.Parameters.AddWithValue("tenant", tenant);
            cleanup.Parameters.AddWithValue("board", board); cleanup.Parameters.AddWithValue("board_text", board.ToString("D"));
            await cleanup.ExecuteNonQueryAsync(ct);
        }
    }
}
