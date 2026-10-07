using Npgsql;
using StrataAI.Application.Identity;
using StrataAI.Application.Onboarding;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Onboarding;

public sealed class PostgresInvitationRecipientEventReader(PostgresConnectionFactory connections, IIdentityStore identities)
    : IInvitationRecipientEventReader
{
    public async Task<InvitationRecipientCursorBinding?> GetScopeAsync(Guid actorId, CancellationToken cancellationToken)
    {
        if (!connections.OwnsIdentitySubject(actorId))
            throw new InvalidOperationException("Recipient replay requires its owning account transaction.");
        var user = await identities.FindUserByIdAsync(actorId, cancellationToken);
        return user is { Status: AccountStatus.Active, EmailVerified: true } && user.Id == actorId
            ? new(actorId, user.EmailNormalized, user.Version) : null;
    }
    private async Task RequireAsync(InvitationRecipientCursorBinding binding, CancellationToken cancellationToken)
    {
        if (await GetScopeAsync(binding.ActorId, cancellationToken) != binding)
            throw new InvalidOperationException("Recipient account admission changed.");
    }
    public async Task<long> GetHeadAsync(InvitationRecipientCursorBinding binding, CancellationToken cancellationToken)
    {
        await RequireAsync(binding, cancellationToken);
        await using var session = await connections.OpenRoutingSessionAsync(cancellationToken);
        await session.SetLookupAsync(RoutingLookup.InvitationRecipient, binding.EmailNormalized, cancellationToken);
        await using var query = new NpgsqlCommand("SELECT last_sequence FROM invitation_recipient_streams WHERE email_normalized=@email;",
            session.Connection, session.Transaction);
        query.Parameters.AddWithValue("email", binding.EmailNormalized);
        return await query.ExecuteScalarAsync(cancellationToken) is long head ? head : 0;
    }
    public async Task<InvitationRecipientEventWindow> ReadAsync(InvitationRecipientCursorBinding binding, long since, int limit,
        CancellationToken cancellationToken)
    {
        if (since < 0 || limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(since));
        var head = await GetHeadAsync(binding, cancellationToken);
        await using var session = await connections.OpenRoutingSessionAsync(cancellationToken);
        await session.SetLookupAsync(RoutingLookup.InvitationRecipient, binding.EmailNormalized, cancellationToken);
        await using var query = new NpgsqlCommand("""
            SELECT event_id,event_type,sequence,created_at FROM invitation_recipient_events
             WHERE email_normalized=@email AND sequence>@since AND sequence<=@head ORDER BY sequence LIMIT @limit;
            """, session.Connection, session.Transaction);
        query.Parameters.AddWithValue("email", binding.EmailNormalized); query.Parameters.AddWithValue("since", since);
        query.Parameters.AddWithValue("head", head); query.Parameters.AddWithValue("limit", limit + 1);
        List<InvitationRecipientEvent> rows = [];
        await using (var row = await query.ExecuteReaderAsync(cancellationToken))
            while (await row.ReadAsync(cancellationToken))
                rows.Add(new(row.GetGuid(0), row.GetString(1), row.GetInt64(2), row.GetFieldValue<DateTimeOffset>(3)));
        return InvitationRecipientEventWindow.Build(since, head, limit, rows);
    }
}
