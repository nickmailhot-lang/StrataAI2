using Npgsql;
using NpgsqlTypes;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.WorkManagement;

// Source adapter only. The admitted producer must own the identity transaction
// and its final session proof; this adapter never starts or commits that scope.
public sealed class PostgresSearchInteractionEventStore(PostgresConnectionFactory connections) : ISearchInteractionEventStore, IBoardFilterInteractionReplayStore
{
    public async Task<SearchInteractionEvent> AppendOrReplayAsync(Guid requestId, string fingerprint,
        SearchInteractionEvent candidate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (!connections.OwnsIdentitySubject(candidate.ActorId))
            throw new InvalidOperationException("Search interaction requires the owning identity subject transaction.");
        if (requestId == Guid.Empty || fingerprint is null || fingerprint.Length != 64
            || fingerprint.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
            || candidate.EventType != "BOARD_FILTER_CHANGED") throw new InvalidOperationException("Search interaction unavailable.");
        await using var session = await connections.OpenRoutingSessionAsync(cancellationToken);
        await using (var subject = new NpgsqlCommand("SELECT set_config('app.identity_subject',@subject,true);", session.Connection, session.Transaction))
        {
            subject.Parameters.AddWithValue("subject", candidate.ActorId.ToString("D"));
            await subject.ExecuteNonQueryAsync(cancellationToken);
        }
        await using var command = new NpgsqlCommand("SELECT original_event,original_created FROM public.append_or_replay_board_filter_interaction(@request,@fingerprint,@event,@actor,@organization,@board,@created);",
            session.Connection, session.Transaction);
        command.Parameters.AddWithValue("request", requestId); command.Parameters.AddWithValue("fingerprint", fingerprint);
        command.Parameters.AddWithValue("event", candidate.EventId); command.Parameters.AddWithValue("actor", candidate.ActorId);
        command.Parameters.AddWithValue("organization", candidate.OrganizationId!.Value); command.Parameters.AddWithValue("board", candidate.BoardId!.Value);
        command.Parameters.AddWithValue("created", candidate.CreatedAt);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("Search interaction unavailable.");
        var original = SearchInteractionEvent.BoardFilterChanged(reader.GetGuid(0), candidate.ActorId,
            candidate.OrganizationId.Value, candidate.BoardId.Value, reader.GetFieldValue<DateTimeOffset>(1));
        if (await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("Search interaction unavailable.");
        return original;
    }

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
