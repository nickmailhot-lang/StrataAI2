using System.Text.Json;
using Npgsql;
using StrataAI.Application.Identity;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Identity;

internal sealed class PostgresIdentityProfileReplayStore(PostgresConnectionFactory connections) : IIdentityProfileReplayStore
{
    public async Task<IdentityProfileReplay?> ReadAsync(Guid userId, Guid key, CancellationToken cancellationToken)
    {
        RequireScope();
        await using var session = await connections.OpenGlobalSessionAsync(cancellationToken);
        await using var subject = new NpgsqlCommand("SELECT set_config('app.identity_subject',@subject,true);", session.Connection, session.Transaction);
        subject.Parameters.AddWithValue("subject", userId.ToString());
        await subject.ExecuteNonQueryAsync(cancellationToken);
        await using var read = new NpgsqlCommand("""
            SELECT fingerprint,result_json::text FROM identity_profile_replays
            WHERE user_id=@user AND key_id=@key AND expires_at>clock_timestamp();
            """, session.Connection, session.Transaction);
        read.Parameters.AddWithValue("user", userId); read.Parameters.AddWithValue("key", key);
        await using var rows = await read.ExecuteReaderAsync(cancellationToken);
        if (!await rows.ReadAsync(cancellationToken)) return null;
        var profile = JsonSerializer.Deserialize<UserProfile>(rows.GetString(1))
            ?? throw new InvalidOperationException("Invalid stored profile acknowledgment.");
        if (profile.Id != userId || profile.Version < 1)
            throw new InvalidOperationException("Invalid stored profile subject.");
        return new IdentityProfileReplay(rows.GetString(0), profile);
    }

    public async Task SaveAsync(Guid userId, Guid key, IdentityProfileReplay replay, CancellationToken cancellationToken)
    {
        RequireScope();
        if (replay.Profile.Id != userId) throw new InvalidOperationException("Profile replay subject differs.");
        await using var session = await connections.OpenGlobalSessionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT set_config('app.identity_subject',@subject,true);
            INSERT INTO identity_profile_replays(user_id,key_id,fingerprint,result_json)
            VALUES (@user,@key,@fingerprint,@result::jsonb)
            ON CONFLICT(user_id,key_id) DO UPDATE SET fingerprint=EXCLUDED.fingerprint,
                result_json=EXCLUDED.result_json,created_at=clock_timestamp(),updated_at=clock_timestamp(),
                expires_at=clock_timestamp()+interval '24 hours'
            WHERE identity_profile_replays.expires_at<=clock_timestamp();
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("subject", userId.ToString());
        command.Parameters.AddWithValue("user", userId); command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("fingerprint", replay.Fingerprint);
        command.Parameters.AddWithValue("result", JsonSerializer.Serialize(replay.Profile));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
    private void RequireScope()
    {
        if (!connections.HasIdentityCommandScope)
            throw new InvalidOperationException("Profile retries require an owning global identity transaction.");
    }
}
