using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StrataAI.Application.Identity;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Infrastructure.WorkManagement;

// Actual restricted API adapter and owning identity transaction. Actor proof
// is the caller's synthetic fixture; this is not HTTP producer/replay evidence.
internal static class SearchInteractionSourceContract
{
    private static void Require(bool condition, string invariant)
    { if (!condition) throw new InvalidOperationException(invariant); }

    public static async Task RunAsync(NpgsqlConnection admin, IServiceProvider provider,
        Guid actor, Guid foreignActor, CancellationToken ct)
    {
        var store = new PostgresSearchInteractionEventStore(provider.GetRequiredService<PostgresConnectionFactory>());
        var unit = provider.GetRequiredService<IIdentityUnitOfWork>();
        var source = SearchInteractionEvent.SearchExecuted(Guid.NewGuid(), actor, DateTimeOffset.UtcNow);
        var foreign = SearchInteractionEvent.SearchExecuted(Guid.NewGuid(), foreignActor, source.CreatedAt);
        const string ownershipError = "Search interaction requires the owning identity subject transaction.";
        async Task RequireUnowned(SearchInteractionEvent candidate)
        {
            try
            {
                await store.AppendAsync(candidate, ct);
                throw new InvalidOperationException("Search source accepted an unowned actor transaction.");
            }
            catch (InvalidOperationException error) when (error.Message == ownershipError) { }
        }
        await RequireUnowned(source);
        await using (var before = new NpgsqlCommand("SELECT count(*) FROM search_interaction_streams WHERE actor_id=@actor;", admin))
        {
            before.Parameters.AddWithValue("actor", actor);
            Require((long)(await before.ExecuteScalarAsync(ct))! == 0, "Search rollback fixture already has a stream.");
        }
        var entered = false;
        var result = await unit.ExecuteAsync<bool>(actor, async () =>
        {
            entered = true;
            await RequireUnowned(foreign);
            await store.AppendAsync(source, ct);
            await store.AppendAsync(source, ct);
            // Returning a failure must roll back both appends and the stream
            // allocation. The adapter must never commit the borrowed scope.
            return IdentityOperation<bool>.Failure("search_fixture_late_refusal");
        }, ct);
        Require(entered && result.ErrorCode == "search_fixture_late_refusal",
            "Restricted C# search source append did not reach the declared late refusal.");
        await using (var after = new NpgsqlCommand("""
            SELECT (SELECT count(*) FROM search_interaction_events WHERE event_id=ANY(@events))
                 + (SELECT count(*) FROM search_interaction_streams WHERE actor_id=@actor);
            """, admin))
        {
            after.Parameters.AddWithValue("events", new[] { source.EventId, foreign.EventId });
            after.Parameters.AddWithValue("actor", actor);
            Require((long)(await after.ExecuteScalarAsync(ct))! == 0,
                "Search source or private sequence stream survived the owning refusal.");
        }
        await RequireUnowned(source);
        await RunReplayAsync(admin, provider, actor, ct);
        Console.WriteLine("Restricted C# search source adapter: owning subject, foreign refusal, duplicate append and full owning rollback passed; producer/replay remain separate acceptance.");
    }

    private static async Task RunReplayAsync(NpgsqlConnection admin, IServiceProvider provider, Guid actor, CancellationToken ct)
    {
        var organization = Guid.NewGuid(); var board = Guid.NewGuid(); var membership = Guid.NewGuid(); var request = Guid.NewGuid();
        await using (var seed = new NpgsqlCommand("""
            INSERT INTO organizations(id,name,created_at,updated_at) VALUES(@organization,'Search retry fixture',now(),now());
            INSERT INTO organization_members(id,tenant_id,user_id,role,status,created_at,updated_at)
             VALUES(@membership,@organization,@actor,'OWNER','ACTIVE',now(),now());
            INSERT INTO boards(id,tenant_id,name,visibility,background_type,background_value,created_at,updated_at)
             VALUES(@board,@organization,'Retry fixture','PRIVATE','COLOR','blue',now(),now());
            """, admin))
        {
            seed.Parameters.AddWithValue("organization", organization); seed.Parameters.AddWithValue("board", board);
            seed.Parameters.AddWithValue("membership", membership); seed.Parameters.AddWithValue("actor", actor);
            await seed.ExecuteNonQueryAsync(ct);
        }
        var store = provider.GetRequiredService<IBoardFilterInteractionReplayStore>();
        var unit = provider.GetRequiredService<IIdentityUnitOfWork>();
        var first = SearchInteractionEvent.BoardFilterChanged(Guid.NewGuid(), actor, organization, board, DateTimeOffset.UtcNow);
        var second = SearchInteractionEvent.BoardFilterChanged(Guid.NewGuid(), actor, organization, board, first.CreatedAt.AddSeconds(1));
        try
        {
            await store.AppendOrReplayAsync(request, new string('a',64), first, ct);
            throw new InvalidOperationException("Restricted retry adapter accepted unowned actor.");
        }
        catch (InvalidOperationException error) when (error.Message == "Search interaction requires the owning identity subject transaction.") { }
        var entered = false;
        var result = await unit.ExecuteAsync<bool>(actor, async () =>
        {
            entered = true;
            Require(await store.AppendOrReplayAsync(request, new string('a',64), first, ct) == first,
                "Restricted retry adapter did not return first canonical source.");
            Require(await store.AppendOrReplayAsync(request, new string('a',64), second, ct) == first,
                "Restricted retry adapter substituted candidate identity or clock.");
            return IdentityOperation<bool>.Failure("retry_fixture_late_refusal");
        }, ct);
        Require(entered && result.ErrorCode == "retry_fixture_late_refusal", "Restricted retry adapter did not reach owning rollback.");
        await using (var verify = new NpgsqlCommand("""
            SELECT (SELECT count(*) FROM search_interaction_events WHERE actor_id=@actor)
             +(SELECT count(*) FROM search_interaction_streams WHERE actor_id=@actor)
             +(SELECT count(*) FROM board_filter_interaction_replays WHERE actor_id=@actor);
            """, admin))
        {
            verify.Parameters.AddWithValue("actor", actor);
            Require((long)(await verify.ExecuteScalarAsync(ct))! == 0, "Owning rollback retained source, stream or receipt.");
        }
        await using (var cleanup = new NpgsqlCommand("DELETE FROM boards WHERE id=@board; DELETE FROM organization_members WHERE id=@membership; DELETE FROM organizations WHERE id=@organization;", admin))
        {
            cleanup.Parameters.AddWithValue("board", board); cleanup.Parameters.AddWithValue("membership", membership);
            cleanup.Parameters.AddWithValue("organization", organization); await cleanup.ExecuteNonQueryAsync(ct);
        }
        Console.WriteLine("Restricted C# Board filter retry adapter: owned identity, original EventId/clock on retry and source/receipt/stream rollback passed; HTTP delivery remains separate acceptance.");
    }
}
