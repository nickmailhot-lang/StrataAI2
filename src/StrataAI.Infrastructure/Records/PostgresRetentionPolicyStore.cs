using Npgsql;
using NpgsqlTypes;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.Records;
using StrataAI.Domain.Records;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Records;

// Every call borrows the already admitted Organization transaction.
internal sealed class PostgresRetentionPolicyStore(PostgresConnectionFactory connections,
    IOrganizationStore organizations, IIdentityStore identities, ICommandActorAuthorization actors)
    : IRetentionPolicyStore
{
    private const string Fields = "h.policy_id,h.tenant_id,h.version,h.record_class,h.category,h.jurisdiction,h.anchor,h.retention_days,h.action,h.source,h.notes,h.created_at,h.updated_at";

    public async Task<IReadOnlyList<RecordRetentionPolicy>> ReadApplicableAsync(RetentionPolicyApplicability scope, CancellationToken ct)
    {
        RequireScope(scope.OrganizationId, ct);
        if (RecordRetentionRules.Validate(scope) is not null) throw new ArgumentException("Invalid retention applicability.", nameof(scope));
        await using var session = await connections.OpenTenantSessionAsync(scope.OrganizationId, ct);
        await using var command = new NpgsqlCommand("SELECT " + Fields + " " + """
            FROM record_retention_policies p JOIN record_retention_policy_history h
            ON (h.tenant_id,h.policy_id,h.version)=(p.tenant_id,p.id,p.version)
            WHERE p.tenant_id=@tenant AND p.record_class=@class AND p.category_key=@category
            AND (p.jurisdiction_key='' OR p.jurisdiction_key=@jurisdiction)
            ORDER BY p.id LIMIT 3;
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", scope.OrganizationId);
        command.Parameters.AddWithValue("class", scope.RecordClass == RetentionRecordClass.OperationalWork ? "OPERATIONAL_WORK" : scope.RecordClass.ToString().ToUpperInvariant());
        command.Parameters.AddWithValue("category", scope.Category.ToUpperInvariant());
        command.Parameters.AddWithValue("jurisdiction", scope.Jurisdiction?.ToUpperInvariant() ?? "");
        var rows = new List<RecordRetentionPolicy>(); await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) rows.Add(Decode(reader));
        return rows;
    }

    public async Task<IReadOnlyList<RecordRetentionPolicy>> ReadPoliciesAsync(Guid organization, Guid? after, CancellationToken ct)
    {
        RequireScope(organization, ct);
        if (after == Guid.Empty) throw new ArgumentOutOfRangeException(nameof(after));
        await using var session = await connections.OpenTenantSessionAsync(organization, ct);
        await using var command = new NpgsqlCommand("SELECT " + Fields + " " + """
            FROM record_retention_policies p JOIN record_retention_policy_history h
            ON (h.tenant_id,h.policy_id,h.version)=(p.tenant_id,p.id,p.version)
            WHERE p.tenant_id=@tenant AND (@after IS NULL OR p.id>@after) ORDER BY p.id LIMIT 51;
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", organization);
        command.Parameters.AddWithValue("after", NpgsqlDbType.Uuid, (object?)after ?? DBNull.Value);
        var rows = new List<RecordRetentionPolicy>(); await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) rows.Add(Decode(reader));
        return rows;
    }

    public async Task<IReadOnlyList<RetentionPolicyRevision>> ReadHistoryAsync(Guid organization, Guid policy, long? beforeVersion, CancellationToken ct)
    {
        RequireScope(organization, ct);
        if (beforeVersion is <= 0) throw new ArgumentOutOfRangeException(nameof(beforeVersion));
        await using var session = await connections.OpenTenantSessionAsync(organization, ct);
        await using var command = new NpgsqlCommand("SELECT " + Fields + """
            ,h.actor_id,h.actor_label,h.membership_id,h.membership_version,h.acting_capacity,h.event_id
            FROM record_retention_policy_history h WHERE h.tenant_id=@tenant AND h.policy_id=@policy
            AND (@before IS NULL OR h.version<@before) ORDER BY h.version DESC LIMIT 51;
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", organization); command.Parameters.AddWithValue("policy", policy);
        command.Parameters.AddWithValue("before", NpgsqlDbType.Bigint, (object?)beforeVersion ?? DBNull.Value);
        var rows = new List<RetentionPolicyRevision>(); await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) rows.Add(new(Decode(reader), reader.GetGuid(13), reader.GetString(14), reader.GetGuid(15),
            reader.GetInt64(16), reader.GetString(17) == "ORGANIZATION_OWNER" ? OrganizationRole.Owner
                : reader.GetString(17) == "ORGANIZATION_ADMIN" ? OrganizationRole.Admin : throw new InvalidOperationException("Invalid retention attribution."), reader.GetGuid(18)));
        return rows;
    }

    public async Task<RecordRetentionPolicy?> ReadAsync(Guid organization, Guid policy, CancellationToken ct)
    {
        RequireScope(organization, ct);
        await using var session = await connections.OpenTenantSessionAsync(organization, ct);
        await using var command = new NpgsqlCommand("SELECT " + Fields + """
             FROM record_retention_policies p JOIN record_retention_policy_history h
             ON (h.tenant_id,h.policy_id,h.version)=(p.tenant_id,p.id,p.version)
             WHERE p.tenant_id=@tenant AND p.id=@policy;
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", organization); command.Parameters.AddWithValue("policy", policy);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Decode(reader) : null;
    }

    public async Task<RetentionPolicyReceipt?> ReadReceiptAsync(Guid organization, Guid actor, Guid key, CancellationToken ct)
    {
        RequireScope(organization, ct);
        await using var session = await connections.OpenTenantSessionAsync(organization, ct);
        await using var command = new NpgsqlCommand("SELECT " + Fields + """
            ,r.actor_id,r.key_id,r.fingerprint,r.expires_at
            FROM record_retention_policy_receipts r JOIN record_retention_policy_history h
            ON (h.tenant_id,h.policy_id,h.version,h.actor_id,h.key_id)=(r.tenant_id,r.policy_id,r.version,r.actor_id,r.key_id)
            WHERE r.tenant_id=@tenant AND r.actor_id=@actor AND r.key_id=@key;
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", organization); command.Parameters.AddWithValue("actor", actor);
        command.Parameters.AddWithValue("key", key);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new(reader.GetGuid(13), reader.GetGuid(14), reader.GetString(15),
            Decode(reader), reader.GetFieldValue<DateTimeOffset>(16)) : null;
    }

    public async Task<RetentionPolicyWriteResult> WriteAsync(long expectedVersion, RetentionPolicyWrite write, CancellationToken ct)
    {
        var policy = write.Policy; var receipt = write.Receipt;
        RequireScope(policy.OrganizationId, ct);
        if (expectedVersion < 0 || expectedVersion == long.MaxValue || policy.Version != expectedVersion + 1)
            return RetentionPolicyWriteResult.VersionConflict;
        var current = await ReadAsync(policy.OrganizationId, policy.Id, ct);
        if ((current?.Version ?? 0) != expectedVersion) return RetentionPolicyWriteResult.VersionConflict;
        if (await ReadReceiptAsync(policy.OrganizationId, write.ActorId, receipt.Key, ct) is not null)
            return RetentionPolicyWriteResult.KeyConflict;
        var parent = await organizations.FindOrganizationAsync(policy.OrganizationId, ct);
        var membership = await organizations.FindMembershipAsync(policy.OrganizationId, write.ActorId, ct);
        var subject = await identities.FindUserByIdAsync(write.ActorId, ct);
        if (!await actors.VerifyAsync(write.ActorId, ct) || parent is not { Status: OrganizationStatus.Active }
            || subject is not { Status: AccountStatus.Active }
            || membership is not { Active: true, Role: OrganizationRole.Owner or OrganizationRole.Admin }
            || membership.Id != write.MembershipId || membership.Version != write.MembershipVersion || membership.Role != write.ActingCapacity
            || RecordRetentionRules.Validate(policy) is not null || write.EventId == Guid.Empty
            || string.IsNullOrWhiteSpace(write.CorrelationId) || write.CorrelationId.Length > 256 || write.CorrelationId.Any(char.IsControl)
            || policy.CreatedAt != (current?.CreatedAt ?? policy.UpdatedAt) || policy.UpdatedAt < parent.UpdatedAt
            || policy.UpdatedAt < membership.UpdatedAt || policy.UpdatedAt < current?.UpdatedAt
            || policy.UpdatedAt.Ticks % 10 != 0 || policy.CreatedAt.Ticks % 10 != 0
            || current is not null && (current.RecordClass != policy.RecordClass || current.Category != policy.Category || current.Jurisdiction != policy.Jurisdiction)
            || receipt.ActorId != write.ActorId || receipt.Key == Guid.Empty || receipt.Result != policy
            || policy.UpdatedAt > DateTimeOffset.MaxValue.AddHours(-24) || receipt.ExpiresAt != policy.UpdatedAt.AddHours(24)
            || receipt.Fingerprint.Length != 64 || !receipt.Fingerprint.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F'))
            return RetentionPolicyWriteResult.SourceUnavailable;

        await using var session = await connections.OpenTenantSessionAsync(policy.OrganizationId, ct);
        await using var begin = new NpgsqlCommand("SAVEPOINT retention_policy_write;", session.Connection, session.Transaction);
        await begin.ExecuteNonQueryAsync(ct);
        try
        {
            await using var context = new NpgsqlCommand("SELECT set_config('app.identity_subject',@actor,true);", session.Connection, session.Transaction);
            context.Parameters.AddWithValue("actor", write.ActorId.ToString()); await context.ExecuteScalarAsync(ct);
            await using var header = new NpgsqlCommand("""
                INSERT INTO record_retention_policies(tenant_id,id,version,record_class,category,jurisdiction,created_at,updated_at)
                SELECT @tenant,@policy,@version,@class,@category,@jurisdiction,@created,@updated WHERE @expected=0
                ON CONFLICT(tenant_id,id) DO NOTHING;
                UPDATE record_retention_policies SET version=@version,updated_at=@updated
                WHERE tenant_id=@tenant AND id=@policy AND version=@expected AND @expected>0;
                """, session.Connection, session.Transaction);
            Parameters(header, policy); header.Parameters.AddWithValue("expected", expectedVersion);
            if (await header.ExecuteNonQueryAsync(ct) != 1)
            { await Rollback(session, ct); return RetentionPolicyWriteResult.VersionConflict; }
            await using var journal = new NpgsqlCommand("""
                INSERT INTO record_retention_policy_history(tenant_id,policy_id,version,record_class,category,jurisdiction,
                 anchor,retention_days,action,source,notes,created_at,updated_at,actor_id,membership_id,membership_version,
                 actor_label,acting_capacity,event_id,key_id,correlation_id)
                VALUES(@tenant,@policy,@version,@class,@category,@jurisdiction,@anchor,@days,@action,@source,@notes,
                 @created,@updated,@actor,@membership,@membershipVersion,@label,@capacity,@event,@key,@correlation);
                INSERT INTO record_retention_policy_receipts(tenant_id,actor_id,key_id,policy_id,version,fingerprint,created_at,expires_at)
                VALUES(@tenant,@actor,@key,@policy,@version,@fingerprint,@updated,@expires);
                """, session.Connection, session.Transaction);
            Parameters(journal, policy);
            journal.Parameters.AddWithValue("anchor", policy.Anchor.ToString().ToUpperInvariant());
            journal.Parameters.AddWithValue("days", NpgsqlDbType.Integer, (object?)policy.RetentionDays ?? DBNull.Value);
            journal.Parameters.AddWithValue("action", policy.Action.ToString().ToUpperInvariant());
            journal.Parameters.AddWithValue("source", policy.Source);
            journal.Parameters.AddWithValue("notes", NpgsqlDbType.Text, (object?)policy.Notes ?? DBNull.Value);
            journal.Parameters.AddWithValue("actor", write.ActorId); journal.Parameters.AddWithValue("membership", write.MembershipId);
            journal.Parameters.AddWithValue("membershipVersion", write.MembershipVersion); journal.Parameters.AddWithValue("label", subject.DisplayName);
            journal.Parameters.AddWithValue("capacity", write.ActingCapacity == OrganizationRole.Owner ? "ORGANIZATION_OWNER" : "ORGANIZATION_ADMIN");
            journal.Parameters.AddWithValue("event", write.EventId); journal.Parameters.AddWithValue("key", receipt.Key);
            journal.Parameters.AddWithValue("correlation", write.CorrelationId); journal.Parameters.AddWithValue("fingerprint", receipt.Fingerprint);
            journal.Parameters.AddWithValue("expires", receipt.ExpiresAt); await journal.ExecuteNonQueryAsync(ct);
            await using var release = new NpgsqlCommand("RELEASE SAVEPOINT retention_policy_write;", session.Connection, session.Transaction);
            await release.ExecuteNonQueryAsync(ct);
            return RetentionPolicyWriteResult.Written;
        }
        catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await Rollback(session, ct);
            if (error.TableName == "record_retention_policy_receipts") return RetentionPolicyWriteResult.KeyConflict;
            if (error.TableName == "record_retention_policies" && error.ConstraintName != "record_retention_policies_pkey")
                return RetentionPolicyWriteResult.NamespaceConflict;
            return RetentionPolicyWriteResult.VersionConflict;
        }
    }

    private static void Parameters(NpgsqlCommand command, RecordRetentionPolicy policy)
    {
        command.Parameters.AddWithValue("tenant", policy.OrganizationId); command.Parameters.AddWithValue("policy", policy.Id);
        command.Parameters.AddWithValue("version", policy.Version);
        command.Parameters.AddWithValue("class", policy.RecordClass == RetentionRecordClass.OperationalWork ? "OPERATIONAL_WORK" : policy.RecordClass.ToString().ToUpperInvariant());
        command.Parameters.AddWithValue("category", policy.Category);
        command.Parameters.AddWithValue("jurisdiction", NpgsqlDbType.Text, (object?)policy.Jurisdiction ?? DBNull.Value);
        command.Parameters.AddWithValue("created", policy.CreatedAt); command.Parameters.AddWithValue("updated", policy.UpdatedAt);
    }

    private static RecordRetentionPolicy Decode(NpgsqlDataReader row) => new(row.GetGuid(0), row.GetGuid(1), row.GetInt64(2),
        row.GetString(3) == "OPERATIONAL_WORK" ? RetentionRecordClass.OperationalWork : Enum.Parse<RetentionRecordClass>(row.GetString(3), true),
        row.GetString(4), row.IsDBNull(5) ? null : row.GetString(5), Enum.Parse<RetentionAnchor>(row.GetString(6), true),
        row.IsDBNull(7) ? null : row.GetInt32(7), Enum.Parse<RecordDispositionAction>(row.GetString(8), true),
        row.GetString(9), row.IsDBNull(10) ? null : row.GetString(10), row.GetFieldValue<DateTimeOffset>(11), row.GetFieldValue<DateTimeOffset>(12));

    private static async Task Rollback(TenantDbSession session, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("ROLLBACK TO SAVEPOINT retention_policy_write; RELEASE SAVEPOINT retention_policy_write;", session.Connection, session.Transaction);
        await command.ExecuteNonQueryAsync(ct);
    }
    private void RequireScope(Guid organization, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (organization == Guid.Empty || !connections.HasCommandScope(organization))
            throw new InvalidOperationException("Retention policy requires an owning transaction.");
    }
}
