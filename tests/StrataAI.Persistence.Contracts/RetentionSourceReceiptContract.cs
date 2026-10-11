using Npgsql;

// PRD-70-TC-05/07/15: canonical minimum capabilities and immutable receipt graph.
internal static class RetentionSourceReceiptContract
{
    private const string SourceSql = """
        INSERT INTO record_retention_sources(tenant_id,record_class,record_id,source_kind,category,registered_at,
          actor_id,membership_id,membership_version,actor_label,acting_capacity,event_id,correlation_id,command_key)
        VALUES(@tenant,'OPERATIONAL_WORK',@card,'CARD','native.receipt',@at,@actor,@member,1,
          'Receipt owner','ORGANIZATION_OWNER',@event,'native-source-receipt',@key);
        """;
    private const string ReceiptSql = """
        INSERT INTO record_retention_source_receipts(tenant_id,actor_id,key_id,record_class,record_id,event_id,fingerprint,created_at,expires_at)
        VALUES(@tenant,@actor,@key,'OPERATIONAL_WORK',@card,@event,@fingerprint,@receiptAt,@expires);
        """;

    public static async Task RunAsync(NpgsqlConnection admin, string apiConnection, CancellationToken ct)
    {
        var tenant = Guid.NewGuid(); var other = Guid.NewGuid(); var actor = Guid.NewGuid(); var stranger = Guid.NewGuid();
        var member = Guid.NewGuid(); var board = Guid.NewGuid(); var list = Guid.NewGuid();
        var card = Guid.NewGuid(); var legacy = Guid.NewGuid(); var key = Guid.NewGuid(); var eventId = Guid.NewGuid();
        var at = DateTimeOffset.UtcNow.AddMinutes(-1); at = at.AddTicks(-(at.Ticks % 10));
        var fingerprint = new string('A', 64);
        await using (var capabilities = new NpgsqlCommand("""
            SELECT bool_and(has_table_privilege('strataai_api_runtime',r.relation,'SELECT')
              AND has_table_privilege('strataai_api_runtime',r.relation,'INSERT')
              AND NOT has_table_privilege('strataai_api_runtime',r.relation,'UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
              AND NOT has_any_column_privilege('strataai_api_runtime',r.relation,'UPDATE,REFERENCES')
              AND NOT has_table_privilege('strataai_worker_runtime',r.relation,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
              AND NOT has_any_column_privilege('strataai_worker_runtime',r.relation,'SELECT,INSERT,UPDATE,REFERENCES'))
            FROM (VALUES('record_retention_sources'),('record_retention_source_receipts')) r(relation);
            """, admin))
            Require(await capabilities.ExecuteScalarAsync(ct) is true, "Canonical classification runtime capabilities differ from API SELECT/INSERT only.");
        try
        {
            await using (var seed = new NpgsqlCommand("""
                GRANT SELECT,INSERT ON record_retention_sources TO strataai_api_runtime;
                GRANT UPDATE(command_key) ON record_retention_sources TO strataai_api_runtime;
                GRANT SELECT,INSERT,UPDATE,DELETE ON record_retention_source_receipts TO strataai_api_runtime;
                INSERT INTO users(id,email,email_normalized,display_name,status,password_hash,created_at,updated_at)
                VALUES(@actor,@email,upper(@email),'Receipt owner','ACTIVE','unusable-fixture-hash',@at,@at),
                 (@stranger,@otherEmail,upper(@otherEmail),'Receipt stranger','ACTIVE','unusable-fixture-hash',@at,@at);
                INSERT INTO organizations(id,name,owner_user_id,status,version,created_at,updated_at)
                VALUES(@tenant,'Receipt contract',@actor,'ACTIVE',1,@at,@at),(@other,'Foreign receipt contract',@stranger,'ACTIVE',1,@at,@at);
                INSERT INTO organization_members(id,tenant_id,user_id,role,status,created_at,updated_at)
                VALUES(@member,@tenant,@actor,'OWNER','ACTIVE',@at,@at);
                INSERT INTO boards(id,tenant_id,name,created_at,updated_at) VALUES(@board,@tenant,'Receipt Board',@at,@at);
                INSERT INTO board_lists(id,tenant_id,board_id,name,rank,created_at,updated_at)
                VALUES(@list,@tenant,@board,'Receipt List','a',@at,@at);
                INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,created_at,updated_at)
                VALUES(@card,@tenant,@board,@list,'Receipt Card','a',@at,@at),(@legacy,@tenant,@board,@list,'Historical classification','b',@at,@at);
                """, admin))
            {
                Parameters(seed); seed.Parameters.AddWithValue("stranger", stranger); seed.Parameters.AddWithValue("other", other);
                seed.Parameters.AddWithValue("board", board); seed.Parameters.AddWithValue("list", list); seed.Parameters.AddWithValue("legacy", legacy);
                seed.Parameters.AddWithValue("email", $"receipt-{actor:N}@example.test");
                seed.Parameters.AddWithValue("otherEmail", $"receipt-{stranger:N}@example.test");
                await seed.ExecuteNonQueryAsync(ct);
            }
            await using var connection = new NpgsqlConnection(apiConnection); await connection.OpenAsync(ct);
            await Rejected(async transaction => await Execute(SourceSql, transaction), PostgresErrorCodes.ForeignKeyViolation);
            await using (var absent = new NpgsqlCommand("SELECT count(*) FROM audit_events WHERE id=@event", admin))
            {
                absent.Parameters.AddWithValue("event", eventId);
                Require(Convert.ToInt64(await absent.ExecuteScalarAsync(ct)) == 0, "Incomplete receipt graph retained its audit.");
            }
            foreach (var bad in new[] { "actor", "record", "event", "clock", "expiry", "fingerprint" })
                await Rejected(async transaction =>
                {
                    await Execute(SourceSql, transaction);
                    await Execute(ReceiptSql, transaction, command =>
                    {
                        if (bad == "actor") command.Parameters["actor"].Value = stranger;
                        if (bad == "record") command.Parameters["card"].Value = legacy;
                        if (bad == "event") command.Parameters["event"].Value = Guid.NewGuid();
                        if (bad == "clock") command.Parameters["receiptAt"].Value = at.AddSeconds(1);
                        if (bad == "expiry") command.Parameters["expires"].Value = at.AddHours(23);
                        if (bad == "fingerprint") command.Parameters["fingerprint"].Value = new string('a', 64);
                    });
                }, bad == "actor" ? PostgresErrorCodes.InsufficientPrivilege : PostgresErrorCodes.CheckViolation);
            await using (var transaction = await connection.BeginTransactionAsync(ct))
            {
                await Context(transaction, tenant, actor); await Execute(SourceSql, transaction); await Execute(ReceiptSql, transaction);
                await transaction.CommitAsync(ct);
            }
            foreach (var sql in new[] {
                "UPDATE record_retention_source_receipts SET fingerprint=repeat('B',64) WHERE tenant_id=@tenant AND actor_id=@actor AND key_id=@key;",
                "DELETE FROM record_retention_source_receipts WHERE tenant_id=@tenant AND actor_id=@actor AND key_id=@key;",
                "UPDATE record_retention_sources SET command_key=NULL WHERE tenant_id=@tenant AND record_id=@card;" })
                await Rejected(async transaction => await Execute(sql, transaction), PostgresErrorCodes.CheckViolation);
            await Rejected(async transaction => await Execute(ReceiptSql, transaction), PostgresErrorCodes.UniqueViolation);
            await Rejected(async transaction => await Execute(SourceSql, transaction, command =>
            {
                command.Parameters["card"].Value = legacy; command.Parameters["event"].Value = Guid.NewGuid();
            }), PostgresErrorCodes.UniqueViolation);
            foreach (var scope in new[] { (tenant, actor, 1L), (other, actor, 0L), (tenant, stranger, 0L) })
            {
                await using var transaction = await connection.BeginTransactionAsync(ct); await Context(transaction, scope.Item1, scope.Item2);
                await using var read = new NpgsqlCommand("SELECT count(*) FROM record_retention_source_receipts", connection, transaction);
                Require(Convert.ToInt64(await read.ExecuteScalarAsync(ct)) == scope.Item3, "Receipt tenant/actor RLS disclosed another scope.");
                await transaction.RollbackAsync(ct);
            }
            // An explicit historical NULL key commits without fabricating a receipt.
            await using (var transaction = await connection.BeginTransactionAsync(ct))
            {
                await Context(transaction, tenant, actor);
                await Execute(SourceSql, transaction, command =>
                {
                    command.Parameters["card"].Value = legacy; command.Parameters["event"].Value = Guid.NewGuid();
                    command.Parameters["key"].Value = DBNull.Value;
                });
                await transaction.CommitAsync(ct);
            }
            Console.WriteLine("Classification receipt graph: real restricted persistence, incomplete graph/audit rollback, exact source binding, immutable receipt and actor/tenant RLS passed; runtime activation remains pending.");

            async Task Context(NpgsqlTransaction transaction, Guid scopedTenant, Guid scopedActor)
            {
                await using var command = new NpgsqlCommand("SELECT set_config('app.tenant_id',@tenant,true),set_config('app.identity_subject',@actor,true);", connection, transaction);
                command.Parameters.AddWithValue("tenant", scopedTenant.ToString()); command.Parameters.AddWithValue("actor", scopedActor.ToString());
                await command.ExecuteNonQueryAsync(ct);
            }
            async Task Execute(string sql, NpgsqlTransaction transaction, Action<NpgsqlCommand>? change = null)
            {
                await using var command = new NpgsqlCommand(sql, connection, transaction); Parameters(command); change?.Invoke(command);
                await command.ExecuteNonQueryAsync(ct);
            }
            async Task Rejected(Func<NpgsqlTransaction, Task> operation, string sqlState)
            {
                await using var transaction = await connection.BeginTransactionAsync(ct); await Context(transaction, tenant, actor);
                var rejected = false;
                try { await operation(transaction); await transaction.CommitAsync(ct); }
                catch (PostgresException error) when (error.SqlState == sqlState) { rejected = true; }
                // Disposal rolls back an unfinished transaction. A rejected
                // COMMIT has already completed the server transaction.
                Require(rejected, "Invalid classification receipt graph was admitted.");
            }
        }
        finally
        {
            await using var revoke = new NpgsqlCommand("REVOKE UPDATE(command_key) ON record_retention_sources FROM strataai_api_runtime; REVOKE UPDATE,DELETE ON record_retention_source_receipts FROM strataai_api_runtime;", admin);
            await revoke.ExecuteNonQueryAsync(CancellationToken.None);
        }
        void Parameters(NpgsqlCommand command)
        {
            command.Parameters.AddWithValue("tenant", tenant); command.Parameters.AddWithValue("actor", actor);
            command.Parameters.AddWithValue("member", member); command.Parameters.AddWithValue("card", card);
            command.Parameters.AddWithValue("event", eventId); command.Parameters.AddWithValue("key", key);
            command.Parameters.AddWithValue("at", at); command.Parameters.AddWithValue("receiptAt", at);
            command.Parameters.AddWithValue("expires", at.AddHours(24)); command.Parameters.AddWithValue("fingerprint", fingerprint);
        }
    }
    private static void Require(bool condition, string rule)
    { if (!condition) throw new InvalidOperationException(rule); }
}
