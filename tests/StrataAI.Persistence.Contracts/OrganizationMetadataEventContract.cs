using Npgsql;
using StrataAI.Infrastructure.Persistence;

// PRD-03-WS-FR-001/003, TC-04/07/08: the canonical audit, event sequence
// and parent mutation share one transaction under a restricted API login.
internal static class OrganizationMetadataEventContract
{
    public static async Task RunAsync(NpgsqlConnection admin, string apiConnection, string workerConnection, CancellationToken ct)
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
            "INSERT INTO organization_metadata_events(tenant_id,sequence,event_id,event_type,actor_id,entity_type,entity_id,entity_version,correlation_id,metadata,created_at,ready_at) SELECT tenant_id,sequence,event_id,event_type,actor_id,entity_type,entity_id,entity_version,correlation_id,metadata,created_at,ready_at FROM organization_metadata_events WHERE tenant_id=@tenant",
            "UPDATE organization_metadata_events SET metadata='{}' WHERE tenant_id=@tenant",
            "DELETE FROM organization_metadata_events WHERE tenant_id=@tenant",
            "INSERT INTO organization_metadata_event_streams VALUES(@tenant,99)",
            "UPDATE organization_metadata_event_streams SET last_sequence=99 WHERE tenant_id=@tenant",
            "SELECT journal_organization_metadata_event()",
            "SELECT journal_organization_member_addition()",
            "SELECT capture_organization_membership_activation()",
            "SELECT journal_organization_member_removal()",
            "SELECT capture_organization_membership_removal()",
            "SELECT journal_organization_member_invitation()",
            "SELECT capture_organization_invitation_creation()",
            "SELECT advance_invitation_revision()",
            "SELECT journal_organization_invitation_revocation()",
            "SELECT capture_organization_invitation_revocation()",
            "SELECT journal_organization_invitation_acceptance()",
            "SELECT capture_organization_invitation_acceptance()",
            "SELECT * FROM organization_invitation_acceptances",
            "UPDATE organization_invitation_acceptances SET entity_version=99",
            "DELETE FROM organization_invitation_acceptances",
            "SELECT * FROM organization_invitation_revocations",
            "UPDATE organization_invitation_revocations SET entity_version=99",
            "DELETE FROM organization_invitation_revocations",
            "SELECT * FROM organization_invitation_creations",
            "UPDATE organization_invitation_creations SET entity_version=99",
            "DELETE FROM organization_invitation_creations",
            "SELECT * FROM organization_membership_removals",
            "UPDATE organization_membership_removals SET entity_version=99",
            "DELETE FROM organization_membership_removals",
            "SELECT * FROM organization_membership_activations",
            "UPDATE organization_membership_activations SET entity_version=99",
            "DELETE FROM organization_membership_activations" })
        {
            refused = false;
            try { await Execute(sql, tenant, Guid.NewGuid()); }
            catch (PostgresException error) when (error.SqlState == "42501") { refused = true; }
            Require(refused, "Restricted API received a private metadata history capability.");
        }
        refused = false;
        try { await Execute("""
            INSERT INTO audit_events(id,tenant_id,actor_id,event_type,entity_type,entity_id,correlation_id,safe_metadata)
            SELECT @event,@tenant,@actor,'ORGANIZATION_MEMBER_ADDED','OrganizationMembership',id,@correlation,'{}'
             FROM organization_members WHERE tenant_id=@tenant AND user_id=@actor;
            """, tenant, Guid.NewGuid()); }
        catch (PostgresException error) when (error.SqlState == "23514") { refused = true; }
        Require(refused && await Read("SELECT last_sequence FROM organization_metadata_event_streams WHERE tenant_id=@tenant", tenant) == 2,
            "Unproven member addition was published or advanced the journal.");
        foreach (var eventType in new[] { "ORGANIZATION_MEMBER_REMOVED", "ORGANIZATION_MEMBER_LEFT" })
        {
            refused = false;
            try { await Execute($$"""
                INSERT INTO audit_events(id,tenant_id,actor_id,event_type,entity_type,entity_id,correlation_id,safe_metadata)
                VALUES(@event,@tenant,@actor,'{{eventType}}','User',@actor,@correlation,'{}');
                """, tenant, Guid.NewGuid()); }
            catch (PostgresException error) when (error.SqlState == "23514") { refused = true; }
            Require(refused && await Read("SELECT last_sequence FROM organization_metadata_event_streams WHERE tenant_id=@tenant", tenant) == 2,
                "Unproven membership withdrawal was published or advanced the journal.");
        }
        refused = false;
        try { await Execute("""
            INSERT INTO audit_events(id,tenant_id,actor_id,event_type,entity_type,entity_id,correlation_id,safe_metadata)
            VALUES(@event,@tenant,@actor,'ORGANIZATION_MEMBER_INVITED','Invitation',@actor,@correlation,'{}');
            """, tenant, Guid.NewGuid()); }
        catch (PostgresException error) when (error.SqlState == "23514") { refused = true; }
        Require(refused && await Read("SELECT last_sequence FROM organization_metadata_event_streams WHERE tenant_id=@tenant", tenant) == 2,
            "Unproven invitation source was published or advanced the journal.");
        refused = false;
        await using (var edit = new NpgsqlCommand("UPDATE organization_metadata_events SET correlation_id='rewrite' WHERE tenant_id=@tenant", admin))
        {
            edit.Parameters.AddWithValue("tenant", tenant);
            try { await edit.ExecuteNonQueryAsync(ct); }
            catch (PostgresException error) when (error.SqlState == "23514") { refused = true; }
        }
        Require(refused, "Metadata history was mutable through administrative SQL.");
        await using (var install = new NpgsqlCommand($"""
            CREATE FUNCTION public.ci_metadata_outbox_failure() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
             IF NEW.tenant_id='{tenant:D}'::uuid AND NEW.job_type='ORGANIZATION_METADATA_EVENT_READY' THEN
              RAISE EXCEPTION 'Injected metadata outbox failure' USING ERRCODE='23514';
             END IF; RETURN NEW; END $$;
            CREATE TRIGGER ci_metadata_outbox_failure AFTER INSERT ON background_jobs
             FOR EACH ROW EXECUTE FUNCTION public.ci_metadata_outbox_failure();
            """, admin)) { await install.ExecuteNonQueryAsync(ct); }
        try
        {
            refused = false;
            try { await Execute("UPDATE organizations SET name='Outbox must roll back',version=3,updated_at=clock_timestamp() WHERE id=@tenant;" + audit,
                tenant, Guid.NewGuid()); }
            catch (PostgresException error) when (error.SqlState == "23514") { refused = true; }
            Require(refused && await Read("SELECT version FROM organizations WHERE id=@tenant", tenant) == 2
                && await Read("SELECT last_sequence FROM organization_metadata_event_streams WHERE tenant_id=@tenant", tenant) == 2,
                "Outbox publication failure retained the parent edit or sequence.");
            await using var state = new NpgsqlCommand("""
                SELECT (SELECT count(*) FROM audit_events WHERE tenant_id=@tenant)=2
                 AND (SELECT count(*) FROM organization_metadata_events WHERE tenant_id=@tenant)=2
                 AND (SELECT count(*) FROM background_jobs WHERE tenant_id=@tenant AND job_type='ORGANIZATION_METADATA_EVENT_READY')=2;
                """, admin);
            state.Parameters.AddWithValue("tenant", tenant);
            Require(await state.ExecuteScalarAsync(ct) is true, "Outbox failure retained audit, source event or extra job.");
        }
        finally
        {
            await using var cleanup = new NpgsqlCommand("DROP TRIGGER ci_metadata_outbox_failure ON background_jobs; DROP FUNCTION public.ci_metadata_outbox_failure();", admin);
            await cleanup.ExecuteNonQueryAsync(CancellationToken.None);
        }
        // PRD-03-TC-04/07/08: a revocation source requires a future actual
        // transition, and any downstream failure must roll back its private proof.
        await Execute("""
            INSERT INTO invitations(id,tenant_id,invited_email,email_normalized,token_hash,target_surface,target_role,
             created_by_user_id,created_at,expires_at)
            VALUES(@actor,@tenant,'revocation@example.test','REVOCATION@EXAMPLE.TEST',
             replace(@actor::text,'-','')||replace(@actor::text,'-',''),'INTERNAL','MEMBER',@actor,clock_timestamp(),clock_timestamp()+interval '1 day');
            """, tenant, Guid.NewGuid());
        const string revokeAudit = """
            INSERT INTO audit_events(id,tenant_id,actor_id,event_type,entity_type,entity_id,correlation_id,safe_metadata)
            VALUES(@event,@tenant,@actor,'INVITATION_REVOKED','Invitation',@actor,@correlation,'{}');
            """;
        refused = false;
        try { await Execute(revokeAudit, tenant, Guid.NewGuid()); }
        catch (PostgresException error) when (error.SqlState == "23514") { refused = true; }
        Require(refused, "An unrevoked invitation fabricated a revocation source.");
        const string revoke = "UPDATE invitations SET revoked_at=clock_timestamp() WHERE tenant_id=@tenant AND id=@actor;";
        refused = false;
        try { await Execute(revoke + revokeAudit, tenant, Guid.NewGuid(), new string('x', 65)); }
        catch (PostgresException error) when (error.SqlState == "23514") { refused = true; }
        Require(refused, "Invalid revocation event correlation committed.");
        await using (var unchanged = new NpgsqlCommand("""
            SELECT (SELECT version=1 AND revoked_at IS NULL FROM invitations WHERE tenant_id=@tenant AND id=@actor)
             AND NOT EXISTS(SELECT 1 FROM organization_invitation_revocations WHERE tenant_id=@tenant)
             AND (SELECT last_sequence=2 FROM organization_metadata_event_streams WHERE tenant_id=@tenant)
             AND NOT EXISTS(SELECT 1 FROM audit_events WHERE tenant_id=@tenant AND event_type='INVITATION_REVOKED');
            """, admin))
        {
            unchanged.Parameters.AddWithValue("tenant", tenant); unchanged.Parameters.AddWithValue("actor", actor);
            Require(await unchanged.ExecuteScalarAsync(ct) is true, "Failed revocation retained state, proof, audit or sequence gap.");
        }
        var revocationEvent = Guid.NewGuid();
        await Execute(revoke + revokeAudit, tenant, revocationEvent);
        await using (var proof = new NpgsqlCommand("""
            SELECT count(*) FROM organization_metadata_events e
            JOIN invitations i ON i.tenant_id=e.tenant_id AND i.id=e.entity_id
            JOIN organization_invitation_revocations p ON p.tenant_id=e.tenant_id AND p.invitation_id=e.entity_id AND p.entity_version=e.entity_version
            JOIN audit_events a ON a.id=e.event_id
            JOIN background_jobs j ON j.tenant_id=e.tenant_id AND j.idempotency_key='organization-metadata-event/'||replace(e.event_id::text,'-','')
            WHERE e.tenant_id=@tenant AND e.event_id=@event AND e.event_type='INVITATION_REVOKED'
             AND e.entity_type='Invitation' AND e.entity_version=2 AND e.sequence=3
             AND e.actor_id=@actor AND a.actor_id=e.actor_id AND e.created_at=i.updated_at AND p.updated_at=e.created_at
             AND p.revoked_at=i.revoked_at AND e.metadata='{}' AND e.ready_at IS NULL
             AND j.job_type='ORGANIZATION_METADATA_EVENT_READY' AND j.safe_metadata=jsonb_build_object('eventId',e.event_id);
            """, admin))
        {
            proof.Parameters.AddWithValue("tenant", tenant); proof.Parameters.AddWithValue("actor", actor); proof.Parameters.AddWithValue("event", revocationEvent);
            Require((long)(await proof.ExecuteScalarAsync(ct))! == 1, "Revocation lost original audit identity, persisted transition or atomic outbox.");
        }
        refused = false;
        try { await Execute(revokeAudit, tenant, Guid.NewGuid()); }
        catch (PostgresException error) when (error.SqlState == "23505") { refused = true; }
        Require(refused && await Read("SELECT last_sequence FROM organization_metadata_event_streams WHERE tenant_id=@tenant", tenant) == 3,
            "Repeated revocation audit duplicated a source or advanced the journal.");
        Require(await Read("SELECT count(*) FROM organization_metadata_events WHERE tenant_id=@tenant", foreign) == 0,
            "Revocation source crossed the forced-RLS tenant boundary.");
        // Acceptance uses its actual accepting actor and persisted transition,
        // including grants to an already active member, without inventing an addition.
        var acceptanceInvitation = Guid.NewGuid(); var acceptanceEvent = Guid.NewGuid();
        await Execute("""
            INSERT INTO invitations(id,tenant_id,invited_email,email_normalized,token_hash,target_surface,target_role,
             created_by_user_id,created_at,expires_at)
            SELECT @event,@tenant,email,email_normalized,
             replace(@event::text,'-','')||replace(@event::text,'-',''),'INTERNAL','OWNER',@actor,clock_timestamp(),clock_timestamp()+interval '1 day'
            FROM users WHERE id=@actor;
            """, tenant, acceptanceInvitation);
        const string acceptAudit = """
            INSERT INTO audit_events(id,tenant_id,actor_id,event_type,entity_type,entity_id,correlation_id,safe_metadata)
            SELECT @event,@tenant,@actor,'INVITATION_ACCEPTED','Invitation',id,@correlation,'{}'
            FROM invitations WHERE tenant_id=@tenant AND target_role='OWNER';
            """;
        const string accept = "UPDATE invitations SET accepted_at=clock_timestamp(),accepted_by_user_id=@actor WHERE tenant_id=@tenant AND target_role='OWNER';";
        refused = false;
        try { await Execute(acceptAudit, tenant, Guid.NewGuid()); }
        catch (PostgresException error) when (error.SqlState == "23514") { refused = true; }
        Require(refused, "Unaccepted invitation fabricated an acceptance source.");
        refused = false;
        try { await Execute(accept + acceptAudit, tenant, Guid.NewGuid(), new string('x', 65)); }
        catch (PostgresException error) when (error.SqlState == "23514") { refused = true; }
        Require(refused, "Invalid acceptance publication correlation committed.");
        await using (var unchanged = new NpgsqlCommand("""
            SELECT (SELECT version=1 AND accepted_at IS NULL AND accepted_by_user_id IS NULL FROM invitations WHERE id=@invitation)
             AND NOT EXISTS(SELECT 1 FROM organization_invitation_acceptances WHERE invitation_id=@invitation)
             AND (SELECT last_sequence=3 FROM organization_metadata_event_streams WHERE tenant_id=@tenant)
             AND NOT EXISTS(SELECT 1 FROM audit_events WHERE entity_id=@invitation);
            """, admin))
        {
            unchanged.Parameters.AddWithValue("tenant", tenant); unchanged.Parameters.AddWithValue("invitation", acceptanceInvitation);
            Require(await unchanged.ExecuteScalarAsync(ct) is true, "Failed acceptance retained transition, proof, audit or sequence gap.");
        }
        await Execute(accept + acceptAudit, tenant, acceptanceEvent);
        await using (var proof = new NpgsqlCommand("""
            SELECT count(*) FROM organization_metadata_events e
            JOIN invitations i ON i.tenant_id=e.tenant_id AND i.id=e.entity_id
            JOIN organization_invitation_acceptances p ON p.tenant_id=e.tenant_id AND p.invitation_id=e.entity_id AND p.entity_version=e.entity_version
            JOIN audit_events a ON a.id=e.event_id
            JOIN background_jobs j ON j.tenant_id=e.tenant_id AND j.idempotency_key='organization-metadata-event/'||replace(e.event_id::text,'-','')
            WHERE e.event_id=@event AND e.tenant_id=@tenant AND e.event_type='INVITATION_ACCEPTED'
             AND e.entity_type='Invitation' AND e.entity_version=2 AND e.sequence=4
             AND e.actor_id=@actor AND a.actor_id=e.actor_id AND p.actor_id=e.actor_id AND i.accepted_by_user_id=e.actor_id
             AND e.created_at=i.updated_at AND p.updated_at=e.created_at AND p.accepted_at=i.accepted_at AND e.metadata='{}' AND e.ready_at IS NULL
             AND j.job_type='ORGANIZATION_METADATA_EVENT_READY' AND j.safe_metadata=jsonb_build_object('eventId',e.event_id);
            """, admin))
        {
            proof.Parameters.AddWithValue("tenant", tenant); proof.Parameters.AddWithValue("actor", actor); proof.Parameters.AddWithValue("event", acceptanceEvent);
            Require((long)(await proof.ExecuteScalarAsync(ct))! == 1, "Acceptance lost actual actor, original source, revision/time or atomic job.");
        }
        refused = false;
        try { await Execute(acceptAudit, tenant, Guid.NewGuid()); }
        catch (PostgresException error) when (error.SqlState == "23505") { refused = true; }
        Require(refused && await Read("SELECT last_sequence FROM organization_metadata_event_streams WHERE tenant_id=@tenant", tenant) == 4,
            "Repeated acceptance audit fabricated a source or advanced the journal.");
        await OrganizationMetadataDeliveryContract.RunAsync(admin, apiConnection, workerConnection, tenant, actor, ct);
        // That delivery contract withdraws the actor's membership. Historical
        // invitation sources remain deliverable under the separate Worker's real lease.
        await using (var worker = new PostgresConnectionFactory(workerConnection))
        {
            var jobs = new StrataAI.Infrastructure.BackgroundJobs.PostgresBackgroundJobStore(worker);
            foreach (var expectedEvent in new[] { revocationEvent, acceptanceEvent })
            {
                var claim = await jobs.ClaimAsync(tenant, Guid.NewGuid(), ct)
                    ?? throw new InvalidOperationException("Invitation delivery job missing.");
                var eventId = StrataAI.Application.Organizations.OrganizationLifecycleDeliveryHandler.ParseEventId(claim.SafeMetadataJson);
                Require(eventId == expectedEvent, "Worker claimed a different invitation source.");
                var delivery = new StrataAI.Infrastructure.Organizations.PostgresOrganizationMetadataDeliveryStore(worker);
                Require(await delivery.MarkReadyAsync(claim, eventId, ct)
                    && await jobs.CompleteAsync(tenant, claim.Id, claim.LeaseId, claim.WorkerId, ct),
                    "Actor departure stranded the committed invitation source.");
            }
        }
        Console.WriteLine("Organization metadata events: canonical source/version/time, atomic rollback and gap-free retry, forced RLS, private capabilities and immutable history passed.");
    }
}
