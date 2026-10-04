using Npgsql;
using StrataAI.Application.Identity;
using StrataAI.Domain.WorkManagement;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Identity;

public sealed class PostgresUserMentionHandleStore(PostgresConnectionFactory connections) : IUserMentionHandleStore
{
    private const string Fields = "user_id,handle,created_at,updated_at,version";
    private void RequireScope(Guid user)
    {
        if (!connections.HasIdentityCommandScope)
            throw new InvalidOperationException("Mention handles require an owning identity transaction.");
        if (user == Guid.Empty) throw new ArgumentException("Mention account is required.");
    }
    public async Task<UserMentionHandle?> FindAsync(Guid userId, CancellationToken cancellationToken)
    {
        RequireScope(userId);
        await using var session = await connections.OpenGlobalSessionAsync(cancellationToken);
        await using var query = new NpgsqlCommand($"SELECT {Fields} FROM user_mention_handles WHERE user_id=@user;", session.Connection, session.Transaction);
        query.Parameters.AddWithValue("user", userId);
        await using var rows = await query.ExecuteReaderAsync(cancellationToken);
        return await rows.ReadAsync(cancellationToken) ? Read(rows) : null;
    }
    public async Task<IdentityOperation<UserMentionHandleChange>> ClaimAsync(Guid userId, string handle,
        long expectedVersion, DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        RequireScope(userId);
        string normalized;
        try
        {
            normalized = MentionHandle.Normalize(handle);
            if (normalized.StartsWith("u_", StringComparison.Ordinal) && normalized != MentionHandle.DefaultForUser(userId))
                return IdentityOperation<UserMentionHandleChange>.Failure("mention_handle_invalid");
        }
        catch (ArgumentException) { return IdentityOperation<UserMentionHandleChange>.Failure("mention_handle_invalid"); }
        if (expectedVersion < 1) return IdentityOperation<UserMentionHandleChange>.Failure("invalid_version");
        var utc = updatedAt.ToUniversalTime();
        var at = new DateTimeOffset(utc.Ticks - utc.Ticks % 10, TimeSpan.Zero);
        await using var session = await connections.OpenGlobalSessionAsync(cancellationToken);
        var transaction = session.Transaction ?? throw new InvalidOperationException("Mention handle transaction is unavailable.");
        await using (var subject = new NpgsqlCommand("SELECT set_config('app.identity_subject',@subject,true);", session.Connection, transaction))
        {
            subject.Parameters.AddWithValue("subject", userId.ToString("D"));
            await subject.ExecuteNonQueryAsync(cancellationToken);
        }
        UserMentionHandle? current;
        await using (var query = new NpgsqlCommand($"SELECT {Fields} FROM user_mention_handles WHERE user_id=@user FOR UPDATE;", session.Connection, transaction))
        {
            query.Parameters.AddWithValue("user", userId);
            await using var rows = await query.ExecuteReaderAsync(cancellationToken);
            current = await rows.ReadAsync(cancellationToken) ? Read(rows) : null;
        }
        if (current is null) return IdentityOperation<UserMentionHandleChange>.Failure("mention_handle_unavailable");
        if (current.Version != expectedVersion) return IdentityOperation<UserMentionHandleChange>.Failure("version_conflict");
        if (current.Handle == normalized) return IdentityOperation<UserMentionHandleChange>.Success(new(current, false));
        if (current.Version == long.MaxValue || current.UpdatedAt > at)
            return IdentityOperation<UserMentionHandleChange>.Failure("version_conflict");
        // Expected uniqueness/policy refusals must not poison the owning
        // transaction: restore the statement before returning a stable result.
        await transaction.SaveAsync("mention_handle_claim", cancellationToken);
        IdentityOperation<UserMentionHandleChange> result;
        try
        {
            await using var query = new NpgsqlCommand($"UPDATE user_mention_handles SET handle=@handle,updated_at=@at,version=version+1 WHERE user_id=@user AND version=@version RETURNING {Fields};", session.Connection, transaction);
            query.Parameters.AddWithValue("user", userId); query.Parameters.AddWithValue("handle", normalized);
            query.Parameters.AddWithValue("at", at); query.Parameters.AddWithValue("version", expectedVersion);
            await using var rows = await query.ExecuteReaderAsync(cancellationToken);
            result = await rows.ReadAsync(cancellationToken)
                ? IdentityOperation<UserMentionHandleChange>.Success(new(Read(rows), true))
                : IdentityOperation<UserMentionHandleChange>.Failure("version_conflict");
        }
        catch (PostgresException exception) when (exception.SqlState is PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.CheckViolation)
        {
            await transaction.RollbackAsync("mention_handle_claim", cancellationToken);
            result = IdentityOperation<UserMentionHandleChange>.Failure(exception.SqlState == PostgresErrorCodes.UniqueViolation
                ? "mention_handle_unavailable" : "mention_handle_claim_refused");
        }
        await transaction.ReleaseAsync("mention_handle_claim", cancellationToken);
        return result;
    }
    private static UserMentionHandle Read(NpgsqlDataReader row) => new(row.GetGuid(0), row.GetString(1),
        row.GetFieldValue<DateTimeOffset>(2), row.GetFieldValue<DateTimeOffset>(3), row.GetInt64(4));
}
