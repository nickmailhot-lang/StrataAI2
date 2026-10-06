using Npgsql;
using StrataAI.Infrastructure.Persistence;

// PRD-03-WS-FR-001/003, TC-04/07/08: the canonical audit, event sequence
// and parent mutation share one transaction under a restricted API login.
internal static class OrganizationMetadataEventContract
{
    public static async Task RunAsync(NpgsqlConnection admin, string apiConnection, CancellationToken ct)
    {
        var tenant = Guid.NewGuid(); var foreign = Guid.NewGuid(); var actor = Guid.NewGuid();
        var created = Guid.NewGuid(); var updated = Guid.NewGuid();
        static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        await using (var seed = new NpgsqlCommand("""
            INSERT INTO users(id,email,email_normalized,display_name,status,password_hash,created_at,updated_at)
            VALUES(@actor,@email,upper(@email),'Metadata event Owner','ACTIVE','unused-contract-hash',now(),now());
            """, admin))
        {
            seed.Parameters.AddWithValue("actor", actor); seed.Parameters.AddWithValue("email", $"metadata-{actor:N}@example.test");
            await seed.ExecuteNonQueryAsync(ct);
        }
        await using var factory = new PostgresConnectionFactory(apiConnection);
        async Task Execute(string sql, Guid scope, Guid eventId, string correlation = "metadata-contract")
        {
            await using var session = await factory.OpenTenantSessionAsync(scope, ct);
            await using var command = new NpgsqlCommand(sql, session.Connection, session.Transaction);
            command.Parameters.AddWithValue("tenant", tenant); command.Parameters.AddWithValue("actor", actor);
            command.Parameters.AddWithValue("event", eventId); command.Parameters.AddWithValue("correlation", correlation);
            await command.ExecuteNonQueryAsync(ct); await session.CommitAsync(ct);
        }
        const string audit = """
            INSERT INTO audit_events(id,tenant_id,actor_id,event_type,entity_type,entity_id,correlation_id,safe_metadata)
             VALUES(@event,@tenant,@actor,'ORGANIZATION_UPDATED','Organization',@tenant,@correlation,'{}');
            """;
        await Execute("""
            INSERT INTO organizations(id,name,owner_user_id,status,version,created_at,updated_at)
             VALUES(@tenant,'Metadata source',@actor,'ACTIVE',1,clock_timestamp(),clock_timestamp());
            INSERT INTO organization_members(id,tenant_id,user_id,role,status)
             VALUES(gen_random_uuid(),@tenant,@actor,'OWNER','ACTIVE');
            INSERT INTO audit_events(id,tenant_id,actor_id,event_type,entity_type,entity_id,correlation_id,safe_metadata)
             VALUES(@event,@tenant,@actor,'ORGANIZATION_CREATED','Organization',@tenant,@correlation,'{}');
            """, tenant, created);
        async Task<long> Read(string sql, Guid scope)
        {
            await using var session = await factory.OpenTenantSessionAsync(scope, ct);
            await using var query = new NpgsqlCommand(sql, session.Connection, session.Transaction);
            query.Parameters.AddWithValue("tenant", tenant); query.Parameters.AddWithValue("event", created);
            return (long)(await query.ExecuteScalarAsync(ct))!;
        }
        Require(await Read("""
            SELECT count(*) FROM organization_metadata_events e JOIN organizations o ON o.id=e.tenant_id
             WHERE e.event_id=@event AND e.sequence=1 AND e.entity_version=1 AND e.created_at=o.updated_at
             AND e.event_type='ORGANIZATION_CREATED' AND e.entity_id=e.tenant_id AND e.metadata='{}';
            """, tenant) == 1, "Creation event did not retain canonical source version/time.");
        Require(await Read("SELECT count(*) FROM organization_metadata_events WHERE tenant_id=@tenant", foreign) == 0,
            "Metadata event crossed a forced-RLS tenant boundary.");
        // A late projection constraint failure must roll back the parent edit,
        // source audit and incremented sequence, leaving no gap to replay.
        var refused = false;
        try
        {
            await Execute("UPDATE organizations SET name='Must roll back',version=2,updated_at=clock_timestamp() WHERE id=@tenant;" + audit,
                tenant, Guid.NewGuid(), new string('x', 65));
        }
        catch (PostgresException error) when (error.SqlState == "23514") { refused = true; }
        Require(refused, "Invalid event correlation unexpectedly committed.");
        Require(await Read("SELECT version FROM organizations WHERE id=@tenant", tenant) == 1,
            "Failed event publication retained the parent edit.");
        Require(await Read("SELECT last_sequence FROM organization_metadata_event_streams WHERE tenant_id=@tenant", tenant) == 1,
            "Failed event publication retained a sequence gap.");
        Require(await Read("SELECT count(*) FROM organization_metadata_events WHERE tenant_id=@tenant", tenant) == 1,
            "Failed event publication retained an event.");
        await using (var sources = new NpgsqlCommand("SELECT count(*) FROM audit_events WHERE tenant_id=@tenant", admin))
        {
            sources.Parameters.AddWithValue("tenant", tenant);
            Require((long)(await sources.ExecuteScalarAsync(ct))! == 1,
                "Failed event publication retained its source audit.");
        }
        await Execute("UPDATE organizations SET name='Authoritative edit',version=2,updated_at=clock_timestamp() WHERE id=@tenant;" + audit,
            tenant, updated);
        Require(await Read("""
            SELECT count(*) FROM organization_metadata_events e JOIN organizations o ON o.id=e.tenant_id
             WHERE e.tenant_id=@tenant AND e.sequence=2 AND e.entity_version=o.version AND e.created_at=o.updated_at
             AND e.event_type='ORGANIZATION_UPDATED' AND e.metadata='{}';
            """, tenant) == 1, "Update event did not retain the committed canonical version/time.");
        // A second audit cannot fabricate another metadata event for the same
        // unchanged entity version. The source INSERT rolls back as well.
        refused = false;
        try { await Execute(audit, tenant, Guid.NewGuid()); }
        catch (PostgresException error) when (error.SqlState == "23505") { refused = true; }
        Require(refused && await Read("SELECT last_sequence FROM organization_metadata_event_streams WHERE tenant_id=@tenant", tenant) == 2,
            "Repeated unchanged source fabricated an event or advanced the counter.");
        foreach (var sql in new[] {
            "INSERT INTO organization_metadata_events SELECT * FROM organization_metadata_events WHERE tenant_id=@tenant",
            "UPDATE organization_metadata_events SET metadata='{}' WHERE tenant_id=@tenant",
            "DELETE FROM organization_metadata_events WHERE tenant_id=@tenant",
            "INSERT INTO organization_metadata_event_streams VALUES(@tenant,99)",
            "UPDATE organization_metadata_event_streams SET last_sequence=99 WHERE tenant_id=@tenant",
            "SELECT journal_organization_metadata_event()" })
        {
            refused = false;
            try { await Execute(sql, tenant, Guid.NewGuid()); }
            catch (PostgresException error) when (error.SqlState == "42501") { refused = true; }
            Require(refused, "Restricted API received a private metadata history capability.");
        }
        refused = false;
        await using (var edit = new NpgsqlCommand("UPDATE organization_metadata_events SET correlation_id='rewrite' WHERE tenant_id=@tenant", admin))
        {
            edit.Parameters.AddWithValue("tenant", tenant);
            try { await edit.ExecuteNonQueryAsync(ct); }
            catch (PostgresException error) when (error.SqlState == "23514") { refused = true; }
        }
        Require(refused, "Metadata history was mutable through administrative SQL.");
        Console.WriteLine("Organization metadata events: canonical source/version/time, atomic rollback and gap-free retry, forced RLS, private capabilities and immutable history passed.");
    }
}
