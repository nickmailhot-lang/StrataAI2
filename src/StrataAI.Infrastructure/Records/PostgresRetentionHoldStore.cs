using Npgsql;
using NpgsqlTypes;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.Records;
using StrataAI.Domain.Records;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Records;

// Not registered until actual owning record authorization and Demo source parity
// exist. All IO borrows the already admitted Organization transaction.
internal sealed class PostgresRetentionHoldStore(PostgresConnectionFactory connections,
    IOrganizationStore organizations, IIdentityStore identities, ICommandActorAuthorization actors,
    IRetentionHoldScopeAuthorization scopes, IClock clock) : IRetentionHoldStore
{
    private const string Fields = "h.hold_id,h.tenant_id,h.version,h.record_class,h.category,h.record_id,h.status,h.reason,h.applied_by,h.applied_at,h.released_by,h.released_at";
    private const string Current = " FROM record_retention_holds p JOIN record_retention_hold_history h ON(h.tenant_id,h.hold_id,h.version)=(p.tenant_id,p.id,p.version) ";
    private readonly PostgresRetentionAuthorizationReferences references = new(connections);
    public Task<RetentionHoldReference?> ReadReferenceAsync(Guid organization, Guid hold, CancellationToken ct) => references.ReadHoldAsync(organization, hold, ct);
    public Task<RetentionHoldReference?> ReadReceiptReferenceAsync(Guid organization, Guid actor, Guid key, CancellationToken ct) => references.ReadReceiptAsync(organization, actor, key, ct);

    public async Task<RetentionHoldCandidates> ReadApplicableAsync(RetentionRecord record, CancellationToken ct)
    {
        RequireScope(record.OrganizationId, ct);
        var now = clock.UtcNow;
        if (RecordRetentionRules.Validate(record) is not null || now.Offset != TimeSpan.Zero
            || record.CreatedAt > now || record.ArchivedAt > now || record.ClosedAt > now || record.ApprovedAt > now)
            throw new ArgumentException("Invalid retention record projection.", nameof(record));
        await using var session = await connections.OpenTenantSessionAsync(record.OrganizationId, ct);
        await using var command = new NpgsqlCommand("SELECT " + Fields + Current + """
            WHERE p.tenant_id=@tenant AND (p.record_class IS NULL OR p.record_class=@class)
              AND (p.category_key IS NULL OR p.category_key=upper(@category COLLATE "C"))
              AND (p.record_id IS NULL OR p.record_id=@record)
            ORDER BY p.id LIMIT @limit;
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", record.OrganizationId);
        command.Parameters.AddWithValue("class", Class(record.RecordClass)!);
        command.Parameters.AddWithValue("category", record.Category);
        command.Parameters.AddWithValue("record", record.Id);
        command.Parameters.AddWithValue("limit", RetentionHoldCandidates.MaximumSynchronousCandidates + 1);
        var rows = new List<RetentionHold>(); await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) rows.Add(Decode(reader));
        return new(record, rows);
    }
    public async Task<RetentionHold?> ReadAsync(Guid organization, Guid hold, CancellationToken ct)
    {
        RequireScope(organization, ct);
        await using var session = await connections.OpenTenantSessionAsync(organization, ct);
        await using var command = new NpgsqlCommand("SELECT " + Fields + Current + "WHERE p.tenant_id=@tenant AND p.id=@hold;", session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", organization); command.Parameters.AddWithValue("hold", hold);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Decode(reader) : null;
    }
    public async Task<RetentionHoldReceipt?> ReadReceiptAsync(Guid organization, Guid actor, Guid key, CancellationToken ct)
    {
        RequireScope(organization, ct);
        await using var session = await connections.OpenTenantSessionAsync(organization, ct);
        await using var command = new NpgsqlCommand("SELECT " + Fields + ",r.fingerprint,r.expires_at " + """
            FROM record_retention_hold_receipts r JOIN record_retention_hold_history h
             ON(h.tenant_id,h.hold_id,h.version,h.actor_id,h.key_id)=(r.tenant_id,r.hold_id,r.version,r.actor_id,r.key_id)
            WHERE r.tenant_id=@tenant AND r.actor_id=@actor AND r.key_id=@key;
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", organization); command.Parameters.AddWithValue("actor", actor); command.Parameters.AddWithValue("key", key);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new(actor, key, reader.GetString(12), Decode(reader), reader.GetFieldValue<DateTimeOffset>(13)) : null;
    }
    public async Task<RetentionHoldWriteResult> WriteAsync(long expectedVersion, RetentionHoldWrite write, CancellationToken ct)
    {
        var hold = write.Hold; var receipt = write.Receipt;
        RequireScope(hold.OrganizationId, ct);
        var current = await ReadAsync(hold.OrganizationId, hold.Id, ct);
        if (expectedVersion < 0 || expectedVersion == long.MaxValue || (current?.Version ?? 0) != expectedVersion
            || hold.Version != expectedVersion + 1) return RetentionHoldWriteResult.VersionConflict;
        if (await ReadReceiptAsync(hold.OrganizationId, write.ActorId, receipt.Key, ct) is not null) return RetentionHoldWriteResult.KeyConflict;
        var at = hold.ReleasedAt ?? hold.AppliedAt;
        var holdScope = new RetentionHoldScope(hold.OrganizationId, hold.RecordClass, hold.Category, hold.RecordId);
        var transition = current is null
            ? RetentionHoldLifecycle.Apply(hold.Id, holdScope, write.ActorId, hold.Reason, at)
            : RetentionHoldLifecycle.Release(hold.OrganizationId, current, expectedVersion, write.ActorId, at);
        var parent = await organizations.FindOrganizationAsync(hold.OrganizationId, ct);
        var membership = await organizations.FindMembershipAsync(hold.OrganizationId, write.ActorId, ct);
        var subject = await identities.FindUserByIdAsync(write.ActorId, ct);
        if (!transition.Succeeded || transition.Hold != hold || !await actors.VerifyAsync(write.ActorId, ct)
            || !await scopes.VerifyAsync(holdScope, write.ActorId, ct)
            || parent is not { Status: OrganizationStatus.Active } || subject is not { Status: AccountStatus.Active }
            || membership is not { Active: true, Role: OrganizationRole.Owner or OrganizationRole.Admin }
            || membership.Id != write.MembershipId || membership.Version != write.MembershipVersion || membership.Role != write.ActingCapacity
            || write.EventId == Guid.Empty || string.IsNullOrWhiteSpace(write.CorrelationId)
            || write.CorrelationId.Length > 256 || write.CorrelationId.Any(char.IsControl)
            || at.Offset != TimeSpan.Zero || at.Ticks % 10 != 0 || hold.AppliedAt.Ticks % 10 != 0
            || at < parent.UpdatedAt || at < membership.UpdatedAt || at > clock.UtcNow
            || receipt.ActorId != write.ActorId || receipt.Key == Guid.Empty || receipt.Result != hold
            || at > DateTimeOffset.MaxValue.AddHours(-24) || receipt.ExpiresAt != at.AddHours(24)
            || receipt.Fingerprint is not { Length: 64 } || !receipt.Fingerprint.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F')
            || string.IsNullOrWhiteSpace(subject.DisplayName) || subject.DisplayName.Length > 160 || subject.DisplayName.Any(char.IsControl))
            return RetentionHoldWriteResult.SourceUnavailable;
        await using var session = await connections.OpenTenantSessionAsync(hold.OrganizationId, ct);
        await using var begin = new NpgsqlCommand("SAVEPOINT retention_hold_write;", session.Connection, session.Transaction);
        await begin.ExecuteNonQueryAsync(ct);
        try
        {
            await using var context = new NpgsqlCommand("SELECT set_config('app.identity_subject',@actor,true);", session.Connection, session.Transaction);
            context.Parameters.AddWithValue("actor", write.ActorId.ToString()); await context.ExecuteScalarAsync(ct);
            await using var header = new NpgsqlCommand("""
                INSERT INTO record_retention_holds(tenant_id,id,version,record_class,category,record_id,status,reason,applied_by,applied_at,released_by,released_at)
                SELECT @tenant,@hold,@version,@class,@category,@record,@status,@reason,@appliedBy,@appliedAt,@releasedBy,@releasedAt WHERE @expected=0
                ON CONFLICT(tenant_id,id) DO NOTHING;
                UPDATE record_retention_holds SET version=@version,status=@status,released_by=@releasedBy,released_at=@releasedAt
                WHERE tenant_id=@tenant AND id=@hold AND version=@expected AND @expected>0;
                """, session.Connection, session.Transaction);
            Parameters(header, hold); header.Parameters.AddWithValue("expected", expectedVersion);
            if (await header.ExecuteNonQueryAsync(ct) != 1)
            { await Rollback(session, ct); return RetentionHoldWriteResult.VersionConflict; }
            await using var journal = new NpgsqlCommand("""
                INSERT INTO record_retention_hold_history(tenant_id,hold_id,version,record_class,category,record_id,status,reason,
                 applied_by,applied_at,released_by,released_at,actor_id,membership_id,membership_version,actor_label,acting_capacity,event_id,key_id,correlation_id,changed_at)
                VALUES(@tenant,@hold,@version,@class,@category,@record,@status,@reason,@appliedBy,@appliedAt,@releasedBy,@releasedAt,
                 @actor,@membership,@membershipVersion,@label,@capacity,@event,@key,@correlation,@at);
                INSERT INTO record_retention_hold_receipts(tenant_id,actor_id,key_id,hold_id,version,fingerprint,created_at,expires_at)
                VALUES(@tenant,@actor,@key,@hold,@version,@fingerprint,@at,@expires);
                """, session.Connection, session.Transaction);
            Parameters(journal, hold);
            journal.Parameters.AddWithValue("actor", write.ActorId); journal.Parameters.AddWithValue("membership", write.MembershipId);
            journal.Parameters.AddWithValue("membershipVersion", write.MembershipVersion); journal.Parameters.AddWithValue("label", subject.DisplayName);
            journal.Parameters.AddWithValue("capacity", write.ActingCapacity == OrganizationRole.Owner ? "ORGANIZATION_OWNER" : "ORGANIZATION_ADMIN");
            journal.Parameters.AddWithValue("event", write.EventId); journal.Parameters.AddWithValue("key", receipt.Key);
            journal.Parameters.AddWithValue("correlation", write.CorrelationId); journal.Parameters.AddWithValue("at", at);
            journal.Parameters.AddWithValue("fingerprint", receipt.Fingerprint); journal.Parameters.AddWithValue("expires", receipt.ExpiresAt);
            await journal.ExecuteNonQueryAsync(ct);
            await using var release = new NpgsqlCommand("RELEASE SAVEPOINT retention_hold_write;", session.Connection, session.Transaction);
            await release.ExecuteNonQueryAsync(ct);
            return RetentionHoldWriteResult.Written;
        }
        catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await Rollback(session, ct);
            return error.TableName == "record_retention_hold_receipts" ? RetentionHoldWriteResult.KeyConflict : RetentionHoldWriteResult.VersionConflict;
        }
        catch (PostgresException error) when (error.SqlState is PostgresErrorCodes.CheckViolation or PostgresErrorCodes.ForeignKeyViolation or PostgresErrorCodes.InsufficientPrivilege)
        {
            await Rollback(session, ct);
            return RetentionHoldWriteResult.SourceUnavailable;
        }
    }
    private static string? Class(RetentionRecordClass? value) => value is null ? null
        : value == RetentionRecordClass.OperationalWork ? "OPERATIONAL_WORK" : value.Value.ToString().ToUpperInvariant();
    private static void Parameters(NpgsqlCommand command, RetentionHold hold)
    {
        command.Parameters.AddWithValue("tenant", hold.OrganizationId); command.Parameters.AddWithValue("hold", hold.Id);
        command.Parameters.AddWithValue("version", hold.Version);
        command.Parameters.AddWithValue("class", NpgsqlDbType.Text, (object?)Class(hold.RecordClass) ?? DBNull.Value);
        command.Parameters.AddWithValue("category", NpgsqlDbType.Text, (object?)hold.Category ?? DBNull.Value);
        command.Parameters.AddWithValue("record", NpgsqlDbType.Uuid, (object?)hold.RecordId ?? DBNull.Value);
        command.Parameters.AddWithValue("status", hold.Status.ToString().ToUpperInvariant()); command.Parameters.AddWithValue("reason", hold.Reason);
        command.Parameters.AddWithValue("appliedBy", hold.AppliedBy); command.Parameters.AddWithValue("appliedAt", hold.AppliedAt);
        command.Parameters.AddWithValue("releasedBy", NpgsqlDbType.Uuid, (object?)hold.ReleasedBy ?? DBNull.Value);
        command.Parameters.AddWithValue("releasedAt", NpgsqlDbType.TimestampTz, (object?)hold.ReleasedAt ?? DBNull.Value);
    }
    private static RetentionHold Decode(NpgsqlDataReader row) => new(row.GetGuid(0), row.GetGuid(1), row.GetInt64(2),
        row.IsDBNull(3) ? null : row.GetString(3) == "OPERATIONAL_WORK" ? RetentionRecordClass.OperationalWork : Enum.Parse<RetentionRecordClass>(row.GetString(3), true),
        row.IsDBNull(4) ? null : row.GetString(4), row.IsDBNull(5) ? null : row.GetGuid(5), Enum.Parse<RetentionHoldStatus>(row.GetString(6), true),
        row.GetString(7), row.GetGuid(8), row.GetFieldValue<DateTimeOffset>(9), row.IsDBNull(10) ? null : row.GetGuid(10),
        row.IsDBNull(11) ? null : row.GetFieldValue<DateTimeOffset>(11));
    private static async Task Rollback(TenantDbSession session, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("ROLLBACK TO SAVEPOINT retention_hold_write; RELEASE SAVEPOINT retention_hold_write;", session.Connection, session.Transaction);
        await command.ExecuteNonQueryAsync(ct);
    }
    private void RequireScope(Guid organization, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (organization == Guid.Empty || !connections.HasCommandScope(organization))
            throw new InvalidOperationException("Retention hold requires an owning transaction.");
    }
}
