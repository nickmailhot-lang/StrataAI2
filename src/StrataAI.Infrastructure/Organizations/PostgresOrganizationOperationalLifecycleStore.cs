using System.Text.Json;
using Npgsql;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Organizations;

internal sealed class PostgresOrganizationOperationalLifecycleStore(PostgresConnectionFactory connections,
    IOrganizationStore organizations, ICommandActorAuthorization actors, IClock clock,
    OrganizationOperationalLifecycleCommandScope lifecycle) : IOrganizationOperationalLifecycleStore
{
    public async Task<OrganizationOperationalLifecycleScope?> ReadScopeAsync(Guid organization, Guid actor, CancellationToken ct)
    {
        Require(organization, actor, ct);
        var parent = await organizations.FindOrganizationAsync(organization, ct);
        var member = await organizations.FindMembershipAsync(organization, actor, ct);
        await using var session = await connections.OpenTenantSessionAsync(organization, ct);
        await using var account = new NpgsqlCommand("SELECT display_name FROM users WHERE id=@actor AND status='ACTIVE' FOR SHARE;", session.Connection, session.Transaction);
        account.Parameters.AddWithValue("actor", actor);
        var caption = await account.ExecuteScalarAsync(ct) as string;
        if (parent is null || member is null || caption is null) return null;
        if (string.IsNullOrWhiteSpace(caption) || caption.Length > 160 || caption.Any(char.IsControl)) caption = "Member " + actor;
        return new(organization, parent.Status, parent.Version, parent.UpdatedAt, actor, caption,
            member.Id, member.Version, member.Role, member.Active);
    }

    public async Task<OrganizationOperationalLifecycleReceipt?> ReadReceiptAsync(Guid organization, Guid actor, Guid key, CancellationToken ct)
    {
        Require(organization, actor, ct);
        await using var session = await connections.OpenTenantSessionAsync(organization, ct);
        await using var command = new NpgsqlCommand("""
            SELECT receipt_json::text FROM organization_operational_lifecycle_receipts
            WHERE tenant_id=@tenant AND actor_id=@actor AND key_id=@key;
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", organization); command.Parameters.AddWithValue("actor", actor); command.Parameters.AddWithValue("key", key);
        var json = await command.ExecuteScalarAsync(ct) as string;
        return json is null ? null : JsonSerializer.Deserialize<OrganizationOperationalLifecycleReceipt>(json)
            ?? throw new InvalidOperationException("Organization lifecycle receipt is invalid.");
    }

    public async Task<OrganizationOperationalLifecycleWriteResult> WriteAsync(long expectedVersion,
        OrganizationOperationalLifecycleTransition source, OrganizationOperationalLifecycleReceipt receipt, CancellationToken ct)
    {
        Require(source.OrganizationId, source.ActorId, ct);
        var scope = await ReadScopeAsync(source.OrganizationId, source.ActorId, ct);
        if (scope is null || scope.Version != expectedVersion) return OrganizationOperationalLifecycleWriteResult.VersionConflict;
        if (await ReadReceiptAsync(source.OrganizationId, receipt.ActorId, receipt.Key, ct) is not null)
            return OrganizationOperationalLifecycleWriteResult.KeyConflict;
        if (!await actors.VerifyAsync(source.ActorId, ct) || !OrganizationOperationalLifecycleRules.ValidWrite(scope, expectedVersion, source, receipt, clock.UtcNow))
            return OrganizationOperationalLifecycleWriteResult.InvalidSource;
        await using var session = await connections.OpenTenantSessionAsync(source.OrganizationId, ct);
        await using (var begin = new NpgsqlCommand("SAVEPOINT organization_operational_lifecycle_write;", session.Connection, session.Transaction))
            await begin.ExecuteNonQueryAsync(ct);
        try
        {
            await using var parent = new NpgsqlCommand("""
                UPDATE organizations SET status=@status,version=@version,updated_at=@at,operational_lifecycle_version=@version
                WHERE id=@tenant AND version=@expected AND status=@previous;
                """, session.Connection, session.Transaction);
            parent.Parameters.AddWithValue("tenant", source.OrganizationId); parent.Parameters.AddWithValue("version", source.Version);
            parent.Parameters.AddWithValue("expected", expectedVersion); parent.Parameters.AddWithValue("at", source.CreatedAt);
            parent.Parameters.AddWithValue("status", State(source.Status)); parent.Parameters.AddWithValue("previous", State(source.PreviousStatus));
            if (await parent.ExecuteNonQueryAsync(ct) != 1)
            {
                await Rollback(session, ct); return OrganizationOperationalLifecycleWriteResult.VersionConflict;
            }
            await using var journal = new NpgsqlCommand("""
                INSERT INTO organization_operational_lifecycle_history(tenant_id,version,source_json,source_at)
                VALUES(@tenant,@version,@source::jsonb,@at);
                INSERT INTO organization_operational_lifecycle_receipts(tenant_id,actor_id,key_id,version,receipt_json)
                VALUES(@tenant,@actor,@key,@version,@receipt::jsonb);
                """, session.Connection, session.Transaction);
            journal.Parameters.AddWithValue("tenant", source.OrganizationId); journal.Parameters.AddWithValue("version", source.Version);
            journal.Parameters.AddWithValue("at", source.CreatedAt); journal.Parameters.AddWithValue("source", JsonSerializer.Serialize(source));
            journal.Parameters.AddWithValue("actor", receipt.ActorId); journal.Parameters.AddWithValue("key", receipt.Key);
            journal.Parameters.AddWithValue("receipt", JsonSerializer.Serialize(receipt));
            await journal.ExecuteNonQueryAsync(ct);
            await using var release = new NpgsqlCommand("RELEASE SAVEPOINT organization_operational_lifecycle_write;", session.Connection, session.Transaction);
            await release.ExecuteNonQueryAsync(ct); return OrganizationOperationalLifecycleWriteResult.Written;
        }
        catch (PostgresException exception) when (exception.SqlState is PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.CheckViolation)
        {
            await Rollback(session, ct);
            return exception.ConstraintName switch
            {
                "organization_operational_lifecycle_receipts_pkey" => OrganizationOperationalLifecycleWriteResult.KeyConflict,
                "organization_operational_lifecycle_history_pkey" => OrganizationOperationalLifecycleWriteResult.VersionConflict,
                _ => OrganizationOperationalLifecycleWriteResult.InvalidSource,
            };
        }
    }
    private static string State(OrganizationStatus status) => status switch
    {
        OrganizationStatus.Active => "ACTIVE", OrganizationStatus.Suspended => "SUSPENDED", OrganizationStatus.Deactivated => "DEACTIVATED",
        _ => throw new InvalidOperationException("Invalid Organization lifecycle state."),
    };
    private static async Task Rollback(TenantDbSession session, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("ROLLBACK TO SAVEPOINT organization_operational_lifecycle_write; RELEASE SAVEPOINT organization_operational_lifecycle_write;",
            session.Connection, session.Transaction);
        await command.ExecuteNonQueryAsync(ct);
    }
    private void Require(Guid organization, Guid actor, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!connections.HasCommandScope(organization) || !lifecycle.Owns(organization, actor))
            throw new InvalidOperationException("An owning lifecycle transaction is required.");
    }
}
