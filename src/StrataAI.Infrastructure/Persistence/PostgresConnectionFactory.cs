using Npgsql;
using StrataAI.Application.Runtime;

namespace StrataAI.Infrastructure.Persistence;

/// <summary>
/// Owns the production PostgreSQL data source. Tenant-owned operations should use
/// OpenTenantSessionAsync so PostgreSQL RLS receives the Organization context.
/// </summary>
public sealed class PostgresConnectionFactory : IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly AsyncLocal<TenantDbSession?> _commandSession = new();
    private readonly AsyncLocal<RoutingDbSession?> _identityCommandSession = new();

    public PostgresConnectionFactory(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException(
                "A PostgreSQL connection string is required.",
                nameof(connectionString));
        }

        _dataSource = NpgsqlDataSource.Create(connectionString);
    }

    public async ValueTask<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = new NpgsqlCommand("SELECT public.runtime_database_role_is_safe();", connection);
            try
            {
                if (await command.ExecuteScalarAsync(cancellationToken) is not true) throw new RuntimeDatabaseRoleException();
            }
            catch (PostgresException exception) when (exception.SqlState is PostgresErrorCodes.UndefinedFunction or PostgresErrorCodes.InsufficientPrivilege)
            {
                throw new RuntimeDatabaseRoleException();
            }
            await using var schema = new NpgsqlCommand("""
                SELECT count(*) = 50 FROM public.schema_migrations WHERE version = ANY(ARRAY[
                  '001_foundation','002_audit_runtime','003_identity','004_organization_access_routing',
                  '005_invitation_routing','006_work_management','007_background_jobs',
                  '008_identity_delivery','009_runtime_role_guard','010_work_command_replays','011_work_events','012_identity_events',
                  '013_identity_profile_replays','014_identity_retry_retention','015_identity_revocation_replays','016_invitation_discovery','017_identity_login_replays','018_identity_registration_replays','019_identity_recovery_request_replays','020_identity_token_consumption_replays','021_organization_access_integrity','022_invitation_creation_replays','023_invitation_mail_intents','024_invitation_history','025_board_invitation_targets','026_board_invitation_mail','027_routing_isolation','028_board_labels','029_label_routing','030_card_members','031_card_assignment_notifications','032_notification_inbox','033_watch_subscriptions','034_watch_events','035_watch_activity_notifications','036_card_dates','037_card_reminders','038_card_reminder_delivery','039_board_date_policy','040_checklists','041_attachments','042_attachment_integrity','043_attachment_upload_intents','044_attachment_scan_delivery','045_attachment_preview_intents','046_attachment_preview_publication','047_attachment_preview_activation','048_attachment_preview_reads','049_attachment_preview_backfill','050_attachment_scan_recovery']);
                """, connection);
            try
            {
                if (await schema.ExecuteScalarAsync(cancellationToken) is not true) throw new RuntimeDatabaseSchemaException();
            }
            catch (PostgresException exception) when (exception.SqlState is PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.UndefinedColumn or PostgresErrorCodes.InsufficientPrivilege)
            {
                throw new RuntimeDatabaseSchemaException();
            }
            return connection;
        }
        catch { await connection.DisposeAsync(); throw; }
    }

    public async Task<TenantDbSession> OpenTenantSessionAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Organization ID cannot be empty.",
                nameof(organizationId));
        }

        if (_commandSession.Value is { } commandSession)
        {
            if (commandSession.OrganizationId != organizationId)
                throw new InvalidOperationException("A command cannot change its Organization transaction scope.");
            return commandSession.Borrow();
        }

        if (_identityCommandSession.Value is not null)
            throw new InvalidOperationException("An Identity command cannot acquire an Organization session.");

        var connection = await OpenConnectionAsync(cancellationToken);
        NpgsqlTransaction? transaction = null;

        try
        {
            transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using var command = new NpgsqlCommand(
                "SELECT set_config('app.tenant_id', @tenant_id, true);",
                connection,
                transaction);
            command.Parameters.AddWithValue(
                "tenant_id",
                organizationId.ToString());
            await command.ExecuteScalarAsync(cancellationToken);

            return new TenantDbSession(connection, transaction, organizationId);
        }
        catch
        {
            if (transaction is not null) await transaction.DisposeAsync();
            await connection.DisposeAsync();
            throw;
        }
    }

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();

    internal bool HasCommandScope(Guid organizationId) => _commandSession.Value?.OrganizationId == organizationId;
    internal bool HasIdentityCommandScope => _identityCommandSession.Value is not null;

    // Only lifecycle cleanup of previously locked Organization parents borrows
    // this scope. It uses the same identity transaction/connection and restores
    // RLS context; it neither creates nor commits a nested tenant transaction.
    internal async Task ExecuteIdentityOrganizationCleanupAsync(Guid organizationId, Func<Task> operation,
        CancellationToken cancellationToken)
    {
        if (organizationId == Guid.Empty || _commandSession.Value is not null || _identityCommandSession.Value is not { } identity
            || identity.Transaction is null) throw new InvalidOperationException("Organization cleanup requires its owning identity transaction.");
        async Task SetTenant(string value, CancellationToken ct)
        {
            await using var command = new NpgsqlCommand("SELECT set_config('app.tenant_id',@tenant,true);", identity.Connection, identity.Transaction);
            command.Parameters.AddWithValue("tenant", value); await command.ExecuteScalarAsync(ct);
        }
        await using var readScope = new NpgsqlCommand("SELECT current_setting('app.tenant_id',true);", identity.Connection, identity.Transaction);
        var previous = await readScope.ExecuteScalarAsync(cancellationToken) as string ?? "";
        await SetTenant(organizationId.ToString(), cancellationToken);
        _commandSession.Value = new TenantDbSession(identity.Connection, identity.Transaction, organizationId, ownsResources: false);
        try { await operation(); }
        finally
        {
            _commandSession.Value = null;
            await SetTenant(previous, CancellationToken.None);
        }
    }

    // Global identity reads borrow command locks only when a command owns the transaction.
    // Creating a discovery transaction here would also change FOR SHARE admission and
    // suppress the identity store's explicitly owned write transaction/commit.
    internal async Task<RoutingDbSession> OpenGlobalSessionAsync(CancellationToken cancellationToken)
    {
        if (_commandSession.Value is { } session)
            return new RoutingDbSession(session.Connection, session.Transaction, ownsConnection: false);
        if (_identityCommandSession.Value is { } identity)
            return new RoutingDbSession(identity.Connection, identity.Transaction, ownsConnection: false);
        return new RoutingDbSession(await OpenConnectionAsync(cancellationToken), null, ownsConnection: true);
    }

    internal async Task<RoutingDbSession> OpenRoutingSessionAsync(CancellationToken cancellationToken)
    {
        if (_commandSession.Value is { } session)
            return new RoutingDbSession(session.Connection, session.Transaction, ownsConnection: false);
        if (_identityCommandSession.Value is { } identity)
            return new RoutingDbSession(identity.Connection, identity.Transaction, ownsConnection: false);
        var connection = await OpenConnectionAsync(cancellationToken);
        try { return new RoutingDbSession(connection, await connection.BeginTransactionAsync(cancellationToken), ownsConnection: true); }
        catch { await connection.DisposeAsync(); throw; }
    }

    internal async Task<T> ExecuteTenantCommandAsync<T>(
        Guid organizationId,
        Func<Task<T>> operation,
        Func<T, bool> succeeded,
        CancellationToken cancellationToken)
    {
        if (_commandSession.Value is not null || _identityCommandSession.Value is not null)
            throw new InvalidOperationException("Nested command transactions are not supported.");
        await using var session = await OpenTenantSessionAsync(organizationId, cancellationToken);
        // Set within the owning async scope so descendants share this transaction;
        // independent concurrent requests retain distinct AsyncLocal values.
        _commandSession.Value = session;
        try
        {
            var result = await operation();
            if (succeeded(result)) await session.CommitAsync(cancellationToken);
            return result;
        }
        finally { _commandSession.Value = null; }
    }

    internal async Task<T> ExecuteIdentityCommandAsync<T>(Func<Task<T>> operation,
        Func<T, bool> succeeded, CancellationToken cancellationToken)
    {
        if (_commandSession.Value is not null || _identityCommandSession.Value is not null)
            throw new InvalidOperationException("Nested command transactions are not supported.");
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        // Identity is global account state: no fabricated Organization or RLS bypass.
        _identityCommandSession.Value = new RoutingDbSession(connection, transaction, ownsConnection: false);
        try
        {
            var result = await operation();
            if (succeeded(result)) await transaction.CommitAsync(cancellationToken);
            return result;
        }
        finally { _identityCommandSession.Value = null; }
    }
}
