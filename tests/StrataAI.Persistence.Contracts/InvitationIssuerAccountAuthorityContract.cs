using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using System.Security.Cryptography;
using System.Text.Json;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Onboarding;
using StrataAI.Application.Runtime;
using StrataAI.Infrastructure.BackgroundJobs;
using StrataAI.Infrastructure.Identity;
using StrataAI.Infrastructure.Onboarding;
using StrataAI.Infrastructure.Organizations;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Infrastructure.WorkManagement;

// Actual production account adapters/UOW and restricted Worker capabilities.
// Initial account/invitation rows and admission are fixtures, not HTTP sessions.
internal static class InvitationIssuerAccountAuthorityContract
{
    public static async Task RunAsync(NpgsqlConnection admin, string apiConnection, string workerConnection, CancellationToken ct)
    {
        var actor = Guid.NewGuid(); var owner = Guid.NewGuid(); var earlier = Guid.NewGuid();
        var exhausted = Guid.NewGuid(); var following = Guid.NewGuid();
        var email = $"ISSUER-{actor:N}@EXAMPLE.TEST".ToUpperInvariant();
        var other = $"ISSUER-OTHER-{actor:N}@EXAMPLE.TEST".ToUpperInvariant();
        var future = $"ISSUER-FUTURE-{actor:N}@EXAMPLE.TEST".ToUpperInvariant();
        void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        await using (var seed = new NpgsqlCommand("""
            INSERT INTO users(id,email,email_normalized,display_name,status,email_verified,password_hash,created_at,updated_at)
             SELECT id,id::text||'@example.test',upper(id::text||'@example.test'),'Issuer authority fixture','ACTIVE',true,'unused',now(),now()
             FROM unnest(ARRAY[@actor,@owner,@earlier,@exhausted,@following]::uuid[]) id;
            WITH tenants AS (
             INSERT INTO organizations(id,name,owner_user_id,created_at,updated_at)
             SELECT gen_random_uuid(),'Issuer authority fixture',@owner,now(),now() FROM generate_series(1,205) RETURNING id
            ) INSERT INTO invitations(id,tenant_id,invited_email,email_normalized,token_hash,target_surface,target_role,created_by_user_id,created_at,expires_at)
             SELECT gen_random_uuid(),id,lower(@email),@email,encode(sha256(id::text::bytea),'hex'),'PORTAL','OWNER',@actor,
              clock_timestamp()-interval '1 second',clock_timestamp()+interval '7 days' FROM tenants;
            WITH first_tenant AS (SELECT tenant_id FROM invitations WHERE created_by_user_id=@actor ORDER BY tenant_id LIMIT 1)
            INSERT INTO invitations(id,tenant_id,invited_email,email_normalized,token_hash,target_surface,target_role,created_by_user_id,created_at,expires_at)
             SELECT gen_random_uuid(),tenant_id,lower(CASE WHEN n=204 THEN @other ELSE @email END),
              CASE WHEN n=204 THEN @other ELSE @email END,encode(sha256((tenant_id::text||'/'||n)::bytea),'hex'),
              'PORTAL','OWNER',@actor,clock_timestamp()-interval '1 second',clock_timestamp()+interval '7 days'
             FROM first_tenant CROSS JOIN generate_series(1,204) n;
            WITH future_tenant AS (
             INSERT INTO organizations(id,name,owner_user_id,created_at,updated_at)
             VALUES(gen_random_uuid(),'Issuer future fixture',@owner,now(),now()) RETURNING id
            ) INSERT INTO invitations(id,tenant_id,invited_email,email_normalized,token_hash,target_surface,target_role,created_by_user_id,created_at,expires_at)
             SELECT gen_random_uuid(),id,lower(@future),@future,encode(sha256(@future::bytea),'hex'),'PORTAL','OWNER',@actor,
              clock_timestamp()+interval '1 day',clock_timestamp()+interval '7 days' FROM future_tenant;
            """, admin))
        {
            seed.Parameters.AddWithValue("actor", actor); seed.Parameters.AddWithValue("owner", owner); seed.Parameters.AddWithValue("earlier", earlier);
            seed.Parameters.AddWithValue("exhausted", exhausted); seed.Parameters.AddWithValue("following", following);
            seed.Parameters.AddWithValue("email", email); seed.Parameters.AddWithValue("other", other); seed.Parameters.AddWithValue("future", future);
            await seed.ExecuteNonQueryAsync(ct);
        }
        var services = new ServiceCollection(); services.AddLogging(); services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ICommandActorAuthorization>(new AdapterAdmissionFixture(actor,earlier,exhausted,following));
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddSingleton(new PostgresConnectionFactory(apiConnection)); services.AddSingleton<PostgresBackgroundJobStore>();
        var runtime = new RuntimeDescriptor(RuntimeMode.Production, "contract", "contract");
        var settings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
            ["STRATAAI_AUTH_RETRY_CURRENT_KEY"]="contract",["STRATAAI_AUTH_RETRY_KEYS"]=JsonSerializer.Serialize(
                new Dictionary<string,string>{["contract"]=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))}) }).Build();
        services.AddStrataAiIdentity(settings,runtime); services.AddStrataAiOrganizations(runtime); services.AddStrataAiWorkManagement(runtime);
        await using var provider = services.BuildServiceProvider();
        var identity = provider.GetRequiredService<IdentityService>(); var identities = provider.GetRequiredService<IIdentityStore>();
        var unit = provider.GetRequiredService<IIdentityUnitOfWork>();
        Require(!(await unit.ExecuteAsync(actor,async()=>{
            await identities.AppendDomainEventAsync(actor,"USER_DEACTIVATED","unproven-issuer",ct);
            return IdentityOperation<bool>.Success(true);
        },ct)).Succeeded,"Unproven issuer event was accepted.");
        Require(!(await unit.ExecuteDeactivationAsync(actor,async()=>{
            Require((await identity.DeactivateAsync(actor,"refused-issuer",ct)).Succeeded,"Tentative account deactivation failed.");
            return IdentityOperation<bool>.Failure("late-refusal");
        },ct)).Succeeded,"Late issuer source refusal committed.");
        Require((await identities.FindUserByIdAsync(actor,ct)) is {Status:AccountStatus.Active,Version:1},"Late refusal changed account.");
        Require((await unit.ExecuteAsync(earlier,async()=>{
            Require(await identities.DeactivateUserAsync(earlier,DateTimeOffset.UtcNow,ct),"Earlier transition failed.");
            return IdentityOperation<bool>.Success(true);
        },ct)).Succeeded,"Earlier transition did not commit.");
        Require(!(await unit.ExecuteAsync(earlier,async()=>{
            await identities.AppendDomainEventAsync(earlier,"USER_DEACTIVATED","earlier-issuer",ct);
            return IdentityOperation<bool>.Success(true);
        },ct)).Succeeded,"Earlier-transaction issuer proof was accepted.");
        // Canonical Identity event correlation supports 120 characters. Preserve
        // that original contract instead of narrowing it to Board command rules.
        var correlation = new string('x',120);
        Require((await unit.ExecuteDeactivationAsync(actor,()=>identity.DeactivateAsync(actor,correlation,ct),ct)).Succeeded,
            "Actual canonical deactivation failed.");
        await using var worker = new PostgresConnectionFactory(workerConnection);
        var global = new PostgresInvitationIssuerAuthorityDeliveryStore(worker);
        var claim = await global.ClaimAsync(Guid.NewGuid(),ct) ?? throw new InvalidOperationException("Issuer source root missing.");
        Require(claim.ActorId==actor && claim.CorrelationId==correlation,"Issuer source attribution changed.");
        foreach(var bad in new[]{claim with{JobId=Guid.NewGuid()},claim with{EventId=Guid.NewGuid()},
            claim with{ActorId=Guid.NewGuid()},claim with{WorkerId=Guid.NewGuid()},claim with{LeaseId=Guid.NewGuid()}})
            Require(!await global.DeliverAsync(bad,100,ct),"Issuer claim substitution was accepted.");
        Require(!await global.DeliverAsync(claim,99,ct),"Issuer page bound was weakened.");
        var source=claim.EventId;
        // Privileged fault injection only: expire the route lease after a real
        // tentative tenant source publication, without weakening its capability.
        await using(var install=new NpgsqlCommand($"""
            CREATE FUNCTION issuer_contract_expire_route_lease() RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
            BEGIN UPDATE public.invitation_issuer_authority_jobs SET lease_expires_at=clock_timestamp()-interval '1 second'
             WHERE id='{claim.JobId:D}'; RETURN NEW; END $$;
            CREATE TRIGGER issuer_contract_expire_route_lease AFTER INSERT ON invitation_recipient_authority_sources
             FOR EACH ROW WHEN (NEW.issuer_source_event_id='{source:D}') EXECUTE FUNCTION issuer_contract_expire_route_lease();
            """,admin)) await install.ExecuteNonQueryAsync(ct);
        try {
            try {await global.DeliverAsync(claim,100,ct);throw new InvalidOperationException("Expired issuer lease committed routing.");}
            catch(PostgresException e) when(e.SqlState==PostgresErrorCodes.CheckViolation) { }
        } finally {
            await using var remove=new NpgsqlCommand("DROP TRIGGER issuer_contract_expire_route_lease ON invitation_recipient_authority_sources; DROP FUNCTION issuer_contract_expire_route_lease();",admin);
            await remove.ExecuteNonQueryAsync(ct);
        }
        await using(var rollback=new NpgsqlCommand("""
            SELECT NOT EXISTS(SELECT 1 FROM invitation_recipient_authority_sources WHERE issuer_source_event_id=@source)
              AND (SELECT count(*)=1 AND bool_and(state='RUNNING' AND lease_expires_at>clock_timestamp())
                FROM invitation_issuer_authority_jobs WHERE event_id=@source);
            """,admin))
        { rollback.Parameters.AddWithValue("source",source);Require(await rollback.ExecuteScalarAsync(ct) is true,"Late issuer lease failure retained tentative roots or changed its checkpoint."); }
        await using(var expire=new NpgsqlCommand("UPDATE invitation_issuer_authority_jobs SET lease_expires_at=clock_timestamp()-interval '1 second' WHERE id=@job",admin))
        {expire.Parameters.AddWithValue("job",claim.JobId);await expire.ExecuteNonQueryAsync(ct);}
        var reclaimed=await global.ClaimAsync(Guid.NewGuid(),ct)??throw new InvalidOperationException("Expired issuer claim did not recover.");
        Require(reclaimed.JobId==claim.JobId&&reclaimed.EventId==source&&reclaimed.LeaseId!=claim.LeaseId,
            "Issuer reclaim changed source/checkpoint or reused its old lease.");
        Require(!await global.DeliverAsync(claim,100,ct),"Expired old issuer lease regained authority.");
        claim=reclaimed;var globalPages=0;
        do {
            Require(await global.DeliverAsync(claim,100,ct),"Issuer scope projection failed.");
            Require(!await global.DeliverAsync(claim,100,ct),"Completed issuer claim replayed projection.");
            globalPages++; claim=await global.ClaimAsync(Guid.NewGuid(),ct);
        } while(claim is not null);
        Require(globalPages==3,"Issuer routing was not bounded 100/100/5.");
        var jobs=new PostgresBackgroundJobStore(worker,authorityJobsOnly:true);
        var handler=new InvitationRecipientAuthorityDeliveryHandler(new PostgresInvitationRecipientAuthorityDeliveryStore(worker));
        Guid[] tenants;
        await using(var read=new NpgsqlCommand("SELECT tenant_id FROM invitation_recipient_authority_sources WHERE issuer_source_event_id=@source ORDER BY tenant_id",admin))
        {
            read.Parameters.AddWithValue("source",source); var rows=new List<Guid>();
            await using var reader=await read.ExecuteReaderAsync(ct); while(await reader.ReadAsync(ct))rows.Add(reader.GetGuid(0)); tenants=rows.ToArray();
        }
        Require(tenants.Length==205,"Issuer routing lost a tenant or admitted a future invitation.");
        var firstPage=await jobs.ClaimAsync(tenants[0],Guid.NewGuid(),ct)??throw new InvalidOperationException("First issuer recipient page missing.");
        await using(var install=new NpgsqlCommand($"""
            CREATE FUNCTION issuer_contract_expire_recipient_lease() RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
            BEGIN UPDATE public.background_jobs SET lease_expires_at=clock_timestamp()-interval '1 second'
             WHERE id='{firstPage.Id:D}'; RETURN NEW; END $$;
            CREATE TRIGGER issuer_contract_expire_recipient_lease AFTER INSERT ON invitation_issuer_authority_effects
             FOR EACH ROW WHEN (NEW.event_id='{source:D}') EXECUTE FUNCTION issuer_contract_expire_recipient_lease();
            """,admin)) await install.ExecuteNonQueryAsync(ct);
        try {
            try {await handler.ExecuteAsync(firstPage,ct);throw new InvalidOperationException("Expired recipient lease committed global issuer effects.");}
            catch(PostgresException e) when(e.SqlState==PostgresErrorCodes.CheckViolation) { }
        } finally {
            await using var remove=new NpgsqlCommand("DROP TRIGGER issuer_contract_expire_recipient_lease ON invitation_issuer_authority_effects; DROP FUNCTION issuer_contract_expire_recipient_lease();",admin);
            await remove.ExecuteNonQueryAsync(ct);
        }
        await using(var rollback=new NpgsqlCommand("""
            SELECT NOT EXISTS(SELECT 1 FROM invitation_issuer_authority_effects WHERE event_id=@source)
             AND NOT EXISTS(SELECT 1 FROM invitation_recipient_authority_effects WHERE source_event_id=@source)
             AND NOT EXISTS(SELECT 1 FROM invitation_recipient_authority_revisions WHERE email_normalized IN (@email,@other))
             AND (SELECT completed_at IS NULL AND scanned_count IS NULL FROM invitation_recipient_authority_pages WHERE job_id=@job)
             AND (SELECT state='RUNNING' AND lease_expires_at>clock_timestamp() FROM background_jobs WHERE id=@job);
            """,admin))
        {
            rollback.Parameters.AddWithValue("source",source);rollback.Parameters.AddWithValue("job",firstPage.Id);
            rollback.Parameters.AddWithValue("email",email);rollback.Parameters.AddWithValue("other",other);
            Require(await rollback.ExecuteScalarAsync(ct) is true,"Late recipient lease refusal retained issuer deduplication, counter or checkpoint effects.");
        }
        // Hold the recovered first page while four independently leased owning
        // scopes race the first global source/email effect. A barrier guarantees
        // all four have their actual restricted claims before delivery begins.
        var firstWave=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered=0;
        await Task.WhenAll(Enumerable.Range(0,4).Select(async lane=>
        {
            var workerId=Guid.NewGuid();var initial=true;
            foreach(var tenant in tenants.Skip(1).Where((_,index)=>index%4==lane))
            {
                var root=await jobs.ClaimAsync(tenant,workerId,ct)??throw new InvalidOperationException("Concurrent owning issuer root missing.");
                if(initial)
                {
                    initial=false;if(Interlocked.Increment(ref entered)==4)firstWave.TrySetResult();
                    await firstWave.Task.WaitAsync(TimeSpan.FromSeconds(20),ct);
                }
                await handler.ExecuteAsync(root,ct);await handler.ExecuteAsync(root,ct);
                Require(await jobs.CompleteAsync(tenant,root.Id,root.LeaseId,workerId,ct),"Concurrent issuer root acknowledgment failed.");
                while(await jobs.ClaimAsync(tenant,workerId,ct) is { } next)
                {
                    await handler.ExecuteAsync(next,ct);await handler.ExecuteAsync(next,ct);
                    Require(await jobs.CompleteAsync(tenant,next.Id,next.LeaseId,workerId,ct),"Concurrent issuer continuation acknowledgment failed.");
                }
            }
        }));
        Require(entered==4,"Issuer first-effect concurrency did not acquire four restricted Worker claims.");
        await handler.ExecuteAsync(firstPage,ct);await handler.ExecuteAsync(firstPage,ct);
        Require(await jobs.CompleteAsync(firstPage.OrganizationId,firstPage.Id,firstPage.LeaseId,firstPage.WorkerId,ct),"First recovered issuer recipient page did not acknowledge.");
        foreach(var tenant in tenants) while(await jobs.ClaimAsync(tenant,Guid.NewGuid(),ct) is { } page)
        {
            await handler.ExecuteAsync(page,ct); await handler.ExecuteAsync(page,ct);
            Require(await jobs.CompleteAsync(tenant,page.Id,page.LeaseId,page.WorkerId,ct),"Issuer recipient page acknowledgment failed.");
        }
        await using(var verify=new NpgsqlCommand("""
            SELECT
             (SELECT array_agg(scanned_count ORDER BY scanned_count)=ARRAY[5,100,100] AND bool_and(state='SUCCEEDED'
                AND attempt_count=CASE WHEN after_tenant='00000000-0000-0000-0000-000000000000' THEN 2 ELSE 1 END)
               FROM invitation_issuer_authority_jobs WHERE event_id=@source),
             (SELECT count(*)=1 AND bool_and(actor_id=@actor AND correlation_id=@correlation) FROM invitation_issuer_authority_sources WHERE event_id=@source),
             (SELECT count(*)=2 FROM invitation_issuer_authority_effects WHERE event_id=@source),
             (SELECT count(*)=2 AND bool_and(revision=1) FROM invitation_recipient_authority_revisions WHERE email_normalized IN (@email,@other)),
             NOT EXISTS(SELECT 1 FROM invitation_recipient_authority_revisions WHERE email_normalized=@future),
             (SELECT count(*)=207 AND bool_and(completed_at IS NOT NULL) FROM invitation_recipient_authority_pages WHERE source_event_id=@source),
             (SELECT count(*)=206 FROM invitation_recipient_authority_effects WHERE source_event_id=@source),
             (SELECT count(*)=207 AND bool_and(state='SUCCEEDED' AND attempt_count=1 AND actor_id=@actor AND correlation_id=@correlation)
               FROM background_jobs WHERE safe_metadata=jsonb_build_object('eventId',@source)),
             (SELECT count(*)=1 FROM identity_events WHERE user_id=@actor AND event_type='USER_DEACTIVATED'),
             (SELECT count(*)=1 FROM audit_events WHERE actor_id=@actor AND event_type='USER_DEACTIVATED'),
             (SELECT count(*)=2 AND bool_and(created_at IS NOT NULL AND updated_at IS NOT NULL
                AND isfinite(created_at) AND updated_at=created_at
                AND created_at>=(SELECT created_at FROM invitation_issuer_authority_sources WHERE event_id=@source))
               FROM invitation_recipient_authority_revisions WHERE email_normalized IN (@email,@other));
            """,admin))
        {
            verify.Parameters.AddWithValue("source",source); verify.Parameters.AddWithValue("actor",actor);verify.Parameters.AddWithValue("correlation",correlation);
            verify.Parameters.AddWithValue("email",email);verify.Parameters.AddWithValue("other",other);verify.Parameters.AddWithValue("future",future);
            await using var row=await verify.ExecuteReaderAsync(ct);
            Require(await row.ReadAsync(ct)&&Enumerable.Range(0,10).All(i=>!row.IsDBNull(i)&&row.GetBoolean(i))
                && !row.IsDBNull(10)&&row.GetBoolean(10),
                "Issuer canonical attribution, global/tenant paging, global recipient deduplication, cutoff, retry or revision clocks failed.");
        }
        foreach(var connection in new[]{apiConnection,workerConnection}) foreach(var table in new[]{"invitation_issuer_authority_proofs","invitation_issuer_authority_sources","invitation_issuer_authority_jobs","invitation_issuer_authority_effects"})
        {
            await using var restricted=new NpgsqlConnection(connection);await restricted.OpenAsync(ct);
            await using var query=new NpgsqlCommand($"SELECT * FROM {table} LIMIT 1",restricted);
            try {await query.ExecuteNonQueryAsync(ct);throw new InvalidOperationException("Runtime read private issuer authority state.");}
            catch(PostgresException e) when(e.SqlState==PostgresErrorCodes.InsufficientPrivilege) { }
        }
        await using(var api=new PostgresConnectionFactory(apiConnection))
        {
            try {await new PostgresInvitationIssuerAuthorityDeliveryStore(api).ClaimAsync(Guid.NewGuid(),ct);throw new InvalidOperationException("API claimed global issuer authority.");}
            catch(PostgresException e) when(e.SqlState==PostgresErrorCodes.InsufficientPrivilege) { }
            try {await new PostgresInvitationIssuerAuthorityDeliveryStore(api).DeliverAsync(reclaimed,100,ct);throw new InvalidOperationException("API delivered global issuer authority.");}
            catch(PostgresException e) when(e.SqlState==PostgresErrorCodes.InsufficientPrivilege) { }
        }
        foreach(var sql in new[]{"UPDATE invitation_issuer_authority_proofs SET changed_at=changed_at WHERE actor_id=@actor",
            "UPDATE invitation_issuer_authority_sources SET correlation_id=correlation_id WHERE event_id=@source",
            "UPDATE invitation_issuer_authority_jobs SET state=state WHERE event_id=@source",
            "UPDATE invitation_issuer_authority_effects SET actor_id=actor_id WHERE event_id=@source",
            "UPDATE identity_events SET correlation_id=correlation_id WHERE event_id=@source"})
        {
            await using var mutation=new NpgsqlCommand(sql,admin);mutation.Parameters.AddWithValue("actor",actor);mutation.Parameters.AddWithValue("source",source);
            try {await mutation.ExecuteNonQueryAsync(ct);throw new InvalidOperationException("Issuer source or completed history mutated.");}
            catch(PostgresException e) when(e.SqlState==PostgresErrorCodes.CheckViolation) { }
        }
        Require((await unit.ExecuteDeactivationAsync(exhausted,()=>identity.DeactivateAsync(exhausted,"exhaustion-original",ct),ct)).Succeeded,
            "Exhaustion fixture canonical command failed.");
        InvitationIssuerAuthorityClaim? final = null;
        for(var attempt=1;attempt<=5;attempt++)
        {
            var next=await global.ClaimAsync(Guid.NewGuid(),ct)??throw new InvalidOperationException("Allowed issuer attempt was lost.");
            Require(next.ActorId==exhausted&&(final is null||next.JobId==final.JobId&&next.EventId==final.EventId&&next.LeaseId!=final.LeaseId),
                "Issuer retry changed original source/job or reused a lease.");
            if(final is not null)Require(!await global.DeliverAsync(final,100,ct),"Prior attempt regained delivery authority.");
            final=next;
            if(attempt<5)
            {
                await using var expire=new NpgsqlCommand("UPDATE invitation_issuer_authority_jobs SET lease_expires_at=clock_timestamp()-interval '1 second' WHERE id=@job",admin);
                expire.Parameters.AddWithValue("job",final.JobId);await expire.ExecuteNonQueryAsync(ct);
            }
        }
        Require(await global.ClaimAsync(Guid.NewGuid(),ct) is null,"Live final lease was reclaimed.");
        Require((await unit.ExecuteDeactivationAsync(following,()=>identity.DeactivateAsync(following,"following-original",ct),ct)).Succeeded,
            "Following fixture canonical command failed.");
        await using(var expire=new NpgsqlCommand("UPDATE invitation_issuer_authority_jobs SET lease_expires_at=clock_timestamp()-interval '1 second' WHERE id=@job",admin))
        {expire.Parameters.AddWithValue("job",final!.JobId);await expire.ExecuteNonQueryAsync(ct);}
        Require(await global.ClaimAsync(Guid.NewGuid(),ct) is null,"Exhausted retirement granted a sixth lease or processed the following row.");
        DateTimeOffset failedAt;
        await using(var verify=new NpgsqlCommand("""
            SELECT j.failed_at,j.attempt_count=5 AND j.state='FAILED' AND j.failure_code='LEASE_EXHAUSTED'
             AND j.worker_id IS NULL AND j.lease_id IS NULL AND j.lease_expires_at IS NULL
             AND j.completed_at IS NULL AND j.scanned_count IS NULL AND isfinite(j.failed_at)
             AND j.event_id=@source AND j.actor_id=@actor
             AND NOT EXISTS(SELECT 1 FROM invitation_recipient_authority_sources WHERE issuer_source_event_id=@source)
             AND EXISTS(SELECT 1 FROM invitation_issuer_authority_jobs WHERE actor_id=@following AND state='PENDING' AND attempt_count=0)
            FROM invitation_issuer_authority_jobs j WHERE j.id=@job;
            """,admin))
        {
            verify.Parameters.AddWithValue("source",final!.EventId);verify.Parameters.AddWithValue("actor",exhausted);
            verify.Parameters.AddWithValue("following",following);verify.Parameters.AddWithValue("job",final.JobId);
            await using var row=await verify.ExecuteReaderAsync(ct);
            Require(await row.ReadAsync(ct)&&row.GetBoolean(1),"Final crash retirement lost source/history or exceeded its one-row bound.");
            failedAt=row.GetFieldValue<DateTimeOffset>(0);
        }
        Require(!await global.DeliverAsync(final!,100,ct),"Failed page retained expired delivery authority.");
        foreach(var sql in new[]{"UPDATE invitation_issuer_authority_jobs SET state='PENDING',failed_at=NULL,failure_code=NULL WHERE id=@job",
            "UPDATE invitation_issuer_authority_jobs SET failure_code=failure_code WHERE id=@job","DELETE FROM invitation_issuer_authority_jobs WHERE id=@job"})
        {
            await using var mutation=new NpgsqlCommand(sql,admin);mutation.Parameters.AddWithValue("job",final!.JobId);
            try{await mutation.ExecuteNonQueryAsync(ct);throw new InvalidOperationException("Failed issuer history was changed.");}
            catch(PostgresException e)when(e.SqlState==PostgresErrorCodes.CheckViolation){}
        }
        var remaining=await global.ClaimAsync(Guid.NewGuid(),ct)??throw new InvalidOperationException("Exhaustion stranded unrelated pending work.");
        Require(remaining.ActorId==following&&await global.DeliverAsync(remaining,100,ct),"Following issuer page did not complete.");
        Require(await global.ClaimAsync(Guid.NewGuid(),ct) is null,"Terminal issuer pages were claimed again.");
        await using(var stable=new NpgsqlCommand("SELECT failed_at=@at AND attempt_count=5 AND failure_code='LEASE_EXHAUSTED' FROM invitation_issuer_authority_jobs WHERE id=@job",admin))
        {stable.Parameters.AddWithValue("at",failedAt);stable.Parameters.AddWithValue("job",final!.JobId);Require(await stable.ExecuteScalarAsync(ct) is true,"Failed issuer history changed on subsequent routing.");}
        Console.WriteLine("Issuer account authority: actual restricted deactivation/source/queue atomicity, unproven/earlier/late refusal, 120-character attribution, exact lease fences, late route/recipient rollback and reclaimed root, 205-Organization routing 100/100/5, four independently claimed concurrent first-effect deliveries with committed replay and global cross-tenant deduplication, future cutoff, immutable history, private capability denial, five-attempt live-lease/final crash exhaustion and bounded immutable dead-letter retirement without stranding following work passed.");
    }
    private sealed class AdapterAdmissionFixture(Guid actor,Guid earlier,Guid exhausted,Guid following) : ICommandActorAuthorization
    {
        public Task<bool> VerifyAsync(Guid id,CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(id==actor||id==earlier||id==exhausted||id==following); }
    }
}
