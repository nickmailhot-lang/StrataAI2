using Npgsql;
using NpgsqlTypes;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.WorkManagement;

public sealed class PostgresNavigationInteractionEventStore(PostgresConnectionFactory connections) : INavigationInteractionEventStore, INavigationInteractionReplayStore
{
    public async Task<NavigationInteractionEvent?> AppendOrReplayAuthorizedAsync(Guid requestId, string fingerprint,
        NavigationInteractionEvent candidate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (!connections.OwnsIdentitySubject(candidate.ActorId)) throw new InvalidOperationException("Navigation requires owning identity transaction.");
        await using var session = await connections.OpenRoutingSessionAsync(ct);
        await using (var subject = new NpgsqlCommand("SELECT set_config('app.identity_subject',@actor,true);", session.Connection, session.Transaction)) {
            subject.Parameters.AddWithValue("actor", candidate.ActorId.ToString("D")); await subject.ExecuteNonQueryAsync(ct);
        }
        await using var command = new NpgsqlCommand("SELECT original_event,original_created FROM public.append_or_replay_navigation_interaction(@request,@fingerprint,@event,@actor,@type,@organization,@board,@entity,@version,@created);", session.Connection, session.Transaction);
        command.Parameters.AddWithValue("request", requestId); command.Parameters.AddWithValue("fingerprint", fingerprint);
        command.Parameters.AddWithValue("event", candidate.EventId); command.Parameters.AddWithValue("actor", candidate.ActorId);
        command.Parameters.AddWithValue("type", candidate.EventType);
        command.Parameters.Add("organization", NpgsqlDbType.Uuid).Value = (object?)candidate.OrganizationId ?? DBNull.Value;
        command.Parameters.Add("board", NpgsqlDbType.Uuid).Value = (object?)candidate.BoardId ?? DBNull.Value;
        command.Parameters.AddWithValue("entity", candidate.EntityId); command.Parameters.AddWithValue("version", candidate.Version);
        command.Parameters.AddWithValue("created", candidate.CreatedAt);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        var id = reader.GetGuid(0); var at = new DateTimeOffset(reader.GetDateTime(1));
        return candidate.EventType switch {
            "APPLICATION_CONTEXT_CHANGED" => NavigationInteractionEvent.ApplicationContextChanged(id, candidate.ActorId, candidate.OrganizationId, at),
            "BOARD_OPENED" => NavigationInteractionEvent.BoardOpened(id, candidate.ActorId, candidate.OrganizationId!.Value, candidate.BoardId!.Value, candidate.Version, at),
            "CARD_OPENED" => NavigationInteractionEvent.CardOpened(id, candidate.ActorId, candidate.OrganizationId!.Value, candidate.BoardId!.Value, candidate.EntityId, candidate.Version, at),
            _ => null
        };
    }
    public async Task<bool> AppendAuthorizedAsync(NavigationInteractionEvent source, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!connections.OwnsIdentitySubject(source.ActorId))
            throw new InvalidOperationException("Navigation requires owning identity transaction.");
        await using var session = await connections.OpenRoutingSessionAsync(ct);
        await using (var subject = new NpgsqlCommand("SELECT set_config('app.identity_subject',@actor,true);", session.Connection, session.Transaction)) {
            subject.Parameters.AddWithValue("actor", source.ActorId.ToString("D"));
            await subject.ExecuteNonQueryAsync(ct);
        }
        await using var command = new NpgsqlCommand("SELECT public.append_navigation_interaction(@event,@actor,@type,@organization,@board,@entity,@version,@created);", session.Connection, session.Transaction);
        command.Parameters.AddWithValue("event", source.EventId); command.Parameters.AddWithValue("actor", source.ActorId);
        command.Parameters.AddWithValue("type", source.EventType);
        command.Parameters.Add("organization", NpgsqlDbType.Uuid).Value = (object?)source.OrganizationId ?? DBNull.Value;
        command.Parameters.Add("board", NpgsqlDbType.Uuid).Value = (object?)source.BoardId ?? DBNull.Value;
        command.Parameters.AddWithValue("entity", source.EntityId); command.Parameters.AddWithValue("version", source.Version);
        command.Parameters.AddWithValue("created", source.CreatedAt);
        return await command.ExecuteScalarAsync(ct) is true;
    }
}
