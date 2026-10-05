using Npgsql;
using NpgsqlTypes;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.WorkManagement;

// Source adapter only. The admitted producer must own the identity transaction
// and its final session proof; this adapter never starts or commits that scope.
public sealed class PostgresSearchInteractionEventStore(PostgresConnectionFactory connections) : ISearchInteractionEventStore
{
    public async Task AppendAsync(SearchInteractionEvent source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!connections.OwnsIdentitySubject(source.ActorId))
            throw new InvalidOperationException("Search interaction requires the owning identity subject transaction.");
        await using var session = await connections.OpenRoutingSessionAsync(cancellationToken);
        // The owning subject lease is an application guard. Forced database
        // RLS also needs that same admitted actor in this borrowed transaction;
        // do not rely on another adapter having established the SQL subject.
        await using (var subject = new NpgsqlCommand("SELECT set_config('app.identity_subject',@subject,true);",
            session.Connection, session.Transaction))
        {
            subject.Parameters.AddWithValue("subject", source.ActorId.ToString("D"));
            await subject.ExecuteNonQueryAsync(cancellationToken);
        }
        await using var command = new NpgsqlCommand("SELECT public.append_search_interaction(@event,@actor,@type,@organization,@board,@created);",
            session.Connection, session.Transaction);
        command.Parameters.AddWithValue("event", source.EventId);
        command.Parameters.AddWithValue("actor", source.ActorId);
        command.Parameters.AddWithValue("type", source.EventType);
        command.Parameters.Add("organization", NpgsqlDbType.Uuid).Value = (object?)source.OrganizationId ?? DBNull.Value;
        command.Parameters.Add("board", NpgsqlDbType.Uuid).Value = (object?)source.BoardId ?? DBNull.Value;
        command.Parameters.AddWithValue("created", source.CreatedAt);
        if (await command.ExecuteScalarAsync(cancellationToken) is not true)
            throw new InvalidOperationException("Search interaction unavailable.");
    }
}
