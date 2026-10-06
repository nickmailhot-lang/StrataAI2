using Npgsql;
using NpgsqlTypes;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.WorkManagement;

public sealed class PostgresNavigationInteractionEventStore(PostgresConnectionFactory connections) : INavigationInteractionEventStore
{
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
