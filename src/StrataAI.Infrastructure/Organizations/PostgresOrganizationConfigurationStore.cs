using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Domain.Organizations;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Organizations;

// Registration follows the configuration schema and restricted-login contract.
// Every operation borrows its authorized Organization command; none commits independently.
internal sealed class PostgresOrganizationConfigurationStore(PostgresConnectionFactory connections,
    IOrganizationStore organizations, ICommandActorAuthorization actors) : IOrganizationConfigurationStore
{
    public async Task<bool> IntakeAvailableAsync(Guid organization, Guid? board, Guid? list, CancellationToken ct)
    {
        RequireScope(organization, ct);
        if (board is null) return list is null;
        if (board == Guid.Empty || list == Guid.Empty) return false;
        // Organization Owner/Admin admission grants canonical Board administration.
        // Keep these relationship reads in the owning tenant instead of resolving
        // a guessed ID globally and attempting a different tenant session.
        await using var session = await connections.OpenTenantSessionAsync(organization, ct);
        await using var selected = new NpgsqlCommand("""
            SELECT id FROM boards WHERE tenant_id=@tenant AND id=@board AND lifecycle_state='ACTIVE'
            AND visibility IN ('PRIVATE','ORGANIZATION','PUBLIC') FOR SHARE;
            """, session.Connection, session.Transaction);
        selected.Parameters.AddWithValue("tenant", organization); selected.Parameters.AddWithValue("board", board.Value);
        if (await selected.ExecuteScalarAsync(ct) is null) return false;
        if (list is null) return true;
        await using var target = new NpgsqlCommand("""
            SELECT id FROM board_lists WHERE tenant_id=@tenant AND board_id=@board AND id=@list
            AND lifecycle_state='ACTIVE' FOR SHARE;
            """, session.Connection, session.Transaction);
        target.Parameters.AddWithValue("tenant", organization); target.Parameters.AddWithValue("board", board.Value);
        target.Parameters.AddWithValue("list", list.Value);
        return await target.ExecuteScalarAsync(ct) is not null;
    }

    public async Task<OrganizationConfigurationRecord?> ReadAsync(Guid organization, CancellationToken ct)
    {
        RequireScope(organization, ct);
        await using var session = await connections.OpenTenantSessionAsync(organization, ct);
        await using var command = new NpgsqlCommand("""
            SELECT record_json::text FROM organization_configurations WHERE tenant_id=@tenant;
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", organization);
        return await command.ExecuteScalarAsync(ct) is string json ? Decode<OrganizationConfigurationRecord>(json) : null;
    }

    public async Task<OrganizationConfigurationReceipt?> ReadReceiptAsync(Guid organization, Guid actor, Guid key, CancellationToken ct)
    {
        RequireScope(organization, ct);
        await using var session = await connections.OpenTenantSessionAsync(organization, ct);
        await using var command = new NpgsqlCommand("""
            SELECT receipt_json::text FROM organization_configuration_receipts
            WHERE tenant_id=@tenant AND actor_id=@actor AND key_id=@key;
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", organization);
        command.Parameters.AddWithValue("actor", actor); command.Parameters.AddWithValue("key", key);
        return await command.ExecuteScalarAsync(ct) is string json ? Decode<OrganizationConfigurationReceipt>(json) : null;
    }

    public async Task<IReadOnlyList<OrganizationConfigurationRecord>> ReadHistoryAsync(Guid organization, long? beforeVersion, CancellationToken ct)
    {
        RequireScope(organization, ct);
        if (beforeVersion is <= 0) throw new ArgumentOutOfRangeException(nameof(beforeVersion));
        await using var session = await connections.OpenTenantSessionAsync(organization, ct);
        await using var command = new NpgsqlCommand("""
            SELECT record_json::text FROM organization_configuration_history
            WHERE tenant_id=@tenant AND (@before IS NULL OR version<@before) ORDER BY version DESC LIMIT 51;
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", organization);
        command.Parameters.AddWithValue("before", NpgsqlDbType.Bigint, (object?)beforeVersion ?? DBNull.Value);
        var result = new List<OrganizationConfigurationRecord>();
        await using var rows = await command.ExecuteReaderAsync(ct);
        while (await rows.ReadAsync(ct)) result.Add(Decode<OrganizationConfigurationRecord>(rows.GetString(0)));
        return result;
    }

    public async Task<IReadOnlyList<OrganizationConfigurationEvent>> ReadEventsAsync(Guid organization, long afterVersion, CancellationToken ct)
    {
        RequireScope(organization, ct);
        if (afterVersion < 0) throw new ArgumentOutOfRangeException(nameof(afterVersion));
        await using var session = await connections.OpenTenantSessionAsync(organization, ct);
        await using var command = new NpgsqlCommand("""
            SELECT event_json::text FROM organization_configuration_events
            WHERE tenant_id=@tenant AND version>@after ORDER BY version LIMIT 51;
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", organization); command.Parameters.AddWithValue("after", afterVersion);
        var result = new List<OrganizationConfigurationEvent>();
        await using var rows = await command.ExecuteReaderAsync(ct);
        while (await rows.ReadAsync(ct)) result.Add(Decode<OrganizationConfigurationEvent>(rows.GetString(0)));
        return result;
    }

    public async Task<OrganizationConfigurationWriteResult> WriteAsync(long expectedVersion,
        OrganizationConfigurationRecord record, OrganizationConfigurationReceipt receipt, CancellationToken ct)
    {
        RequireScope(record.OrganizationId, ct);
        var current = await ReadAsync(record.OrganizationId, ct);
        if (expectedVersion < 0 || expectedVersion == long.MaxValue || (current?.Version ?? 0) != expectedVersion)
            return OrganizationConfigurationWriteResult.VersionConflict;
        if (await ReadReceiptAsync(record.OrganizationId, receipt.ActorId, receipt.Key, ct) is not null)
            return OrganizationConfigurationWriteResult.KeyConflict;
        var parent = await organizations.FindOrganizationAsync(record.OrganizationId, ct);
        var membership = await organizations.FindMembershipAsync(record.OrganizationId, record.ActorId, ct);
        if (!await actors.VerifyAsync(record.ActorId, ct) || parent?.Status != OrganizationStatus.Active
            || membership is not { Active: true, Role: OrganizationRole.Owner or OrganizationRole.Admin }
            || record.OrganizationName != parent.Name || record.OrganizationType != parent.Type || record.OrganizationVersion != parent.Version
            || record.Version != expectedVersion + 1 || record.ActorId == Guid.Empty || record.EventId == Guid.Empty
            || string.IsNullOrWhiteSpace(record.CorrelationId) || record.CorrelationId.Length > 256
            || record.CorrelationId.Any(char.IsControl) || OrganizationConfigurationRules.Validate(record.Configuration) is not null
            || record.CreatedAt != (current?.CreatedAt ?? record.UpdatedAt) || record.UpdatedAt < (current?.UpdatedAt ?? parent.UpdatedAt)
            || record.UpdatedAt < record.CreatedAt || receipt.ActorId != record.ActorId || receipt.Key == Guid.Empty
            || receipt.ExpiresAt <= record.UpdatedAt || receipt.Fingerprint.Length != 64
            || !receipt.Fingerprint.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F')
            || JsonSerializer.Serialize(receipt.Result) != JsonSerializer.Serialize(record))
            return OrganizationConfigurationWriteResult.InvalidSource;

        await using var session = await connections.OpenTenantSessionAsync(record.OrganizationId, ct);
        await using var savepoint = new NpgsqlCommand("SAVEPOINT organization_configuration_write;", session.Connection, session.Transaction);
        await savepoint.ExecuteNonQueryAsync(ct);
        try
        {
            await using var write = new NpgsqlCommand("""
                INSERT INTO organization_configurations(tenant_id,version,record_json)
                SELECT @tenant,@version,@record::jsonb WHERE @expected=0
                ON CONFLICT(tenant_id) DO NOTHING;
                UPDATE organization_configurations SET version=@version,record_json=@record::jsonb
                WHERE tenant_id=@tenant AND version=@expected AND @expected>0;
                """, session.Connection, session.Transaction);
            write.Parameters.AddWithValue("tenant", record.OrganizationId); write.Parameters.AddWithValue("version", record.Version);
            write.Parameters.AddWithValue("expected", expectedVersion); write.Parameters.AddWithValue("record", JsonSerializer.Serialize(record));
            if (await write.ExecuteNonQueryAsync(ct) != 1)
            {
                await RollbackSavepoint(session, ct);
                return OrganizationConfigurationWriteResult.VersionConflict;
            }
            // The history source trigger produces the authoritative body-free audit/event.
            // A deferred current->history constraint rejects a current-only write at commit.
            await using var history = new NpgsqlCommand("""
                INSERT INTO organization_configuration_history(tenant_id,version,record_json)
                VALUES(@tenant,@version,@record::jsonb);
                INSERT INTO organization_configuration_receipts(tenant_id,actor_id,key_id,version,receipt_json)
                VALUES(@tenant,@actor,@key,@version,@receipt::jsonb);
                """, session.Connection, session.Transaction);
            history.Parameters.AddWithValue("tenant", record.OrganizationId); history.Parameters.AddWithValue("version", record.Version);
            history.Parameters.AddWithValue("record", JsonSerializer.Serialize(record)); history.Parameters.AddWithValue("actor", receipt.ActorId);
            history.Parameters.AddWithValue("key", receipt.Key); history.Parameters.AddWithValue("receipt", JsonSerializer.Serialize(receipt));
            await history.ExecuteNonQueryAsync(ct);
            await using var release = new NpgsqlCommand("RELEASE SAVEPOINT organization_configuration_write;", session.Connection, session.Transaction);
            await release.ExecuteNonQueryAsync(ct);
            return OrganizationConfigurationWriteResult.Written;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await RollbackSavepoint(session, ct);
            return exception.ConstraintName switch
            {
                "organization_configuration_registration_unique" => OrganizationConfigurationWriteResult.IdentifierConflict,
                "organization_configuration_receipts_pkey" => OrganizationConfigurationWriteResult.KeyConflict,
                "organization_configuration_history_pkey" => OrganizationConfigurationWriteResult.VersionConflict,
                _ => OrganizationConfigurationWriteResult.InvalidSource,
            };
        }
    }

    private static async Task RollbackSavepoint(TenantDbSession session, CancellationToken ct)
    {
        await using var rollback = new NpgsqlCommand("""
            ROLLBACK TO SAVEPOINT organization_configuration_write;
            RELEASE SAVEPOINT organization_configuration_write;
            """, session.Connection, session.Transaction);
        await rollback.ExecuteNonQueryAsync(ct);
    }

    private static T Decode<T>(string json) => JsonSerializer.Deserialize<T>(json)
        ?? throw new InvalidOperationException("Invalid private Organization configuration snapshot.");
    private void RequireScope(Guid organization, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (organization == Guid.Empty || !connections.HasCommandScope(organization))
            throw new InvalidOperationException("Organization configuration requires an owning transaction.");
    }
}
