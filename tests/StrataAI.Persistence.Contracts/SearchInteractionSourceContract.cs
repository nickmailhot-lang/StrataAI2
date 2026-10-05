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
        Console.WriteLine("Restricted C# search source adapter: owning subject, foreign refusal, duplicate append and full owning rollback passed; producer/replay remain separate acceptance.");
    }
}
