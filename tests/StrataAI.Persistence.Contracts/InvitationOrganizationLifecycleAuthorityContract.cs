using Npgsql;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.Runtime;
using StrataAI.Application.WorkManagement;
using StrataAI.Application.Onboarding;
using StrataAI.Infrastructure.BackgroundJobs;
using StrataAI.Infrastructure.Organizations;
using StrataAI.Infrastructure.Onboarding;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Infrastructure.WorkManagement;

// PRD-03/18/60: actual production mutation/source adapters and leased deletion
// and recipient delivery. Initial account/invitation rows and transaction admission
// are fixtures; native HTTP/session/browser acceptance is a separate requirement.
internal static class InvitationOrganizationLifecycleAuthorityContract
{
    public static async Task RunAsync(NpgsqlConnection admin, string apiConnection, string workerConnection, CancellationToken ct)
    {
        var tenant = Guid.NewGuid(); var actor = Guid.NewGuid(); var earlierTenant = Guid.NewGuid(); var request = Guid.NewGuid();
        var email = $"LIFECYCLE-{tenant:N}@EXAMPLE.TEST".ToUpperInvariant();
        var other = $"LIFECYCLE-OTHER-{tenant:N}@EXAMPLE.TEST".ToUpperInvariant();
        var future = $"LIFECYCLE-FUTURE-{tenant:N}@EXAMPLE.TEST".ToUpperInvariant();
        void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        await using (var seed = new NpgsqlCommand("""
            INSERT INTO users(id,email,email_normalized,display_name,status,email_verified,password_hash,created_at,updated_at)
             VALUES(@actor,lower(@email),@email,'Lifecycle authority fixture','ACTIVE',true,'unused-fixture-hash',now(),now());
            INSERT INTO organizations(id,name,owner_user_id,created_at,updated_at)
             VALUES(@tenant,'Lifecycle authority fixture',@actor,now(),now()),(@earlier,'Earlier command fixture',@actor,now(),now());
            INSERT INTO organization_members(id,tenant_id,user_id,role,status)
             VALUES(gen_random_uuid(),@tenant,@actor,'OWNER','ACTIVE'),(gen_random_uuid(),@earlier,@actor,'OWNER','ACTIVE');
            INSERT INTO invitations(id,tenant_id,invited_email,email_normalized,token_hash,target_surface,target_role,created_by_user_id,created_at,expires_at)
             SELECT gen_random_uuid(),@tenant,lower(CASE WHEN n=205 THEN @other ELSE @email END),
              CASE WHEN n=205 THEN @other ELSE @email END,encode(sha256((@tenant::text||'/'||n)::bytea),'hex'),
              'PORTAL','OWNER',@actor,clock_timestamp()-interval '1 second',clock_timestamp()+interval '7 days' FROM generate_series(1,205) n;
            INSERT INTO invitations(id,tenant_id,invited_email,email_normalized,token_hash,target_surface,target_role,created_by_user_id,created_at,expires_at)
             VALUES(gen_random_uuid(),@tenant,lower(@future),@future,encode(sha256(@future::bytea),'hex'),
              'PORTAL','OWNER',@actor,clock_timestamp()+interval '1 day',clock_timestamp()+interval '7 days');
            """, admin))
        {
            seed.Parameters.AddWithValue("tenant",tenant); seed.Parameters.AddWithValue("earlier",earlierTenant); seed.Parameters.AddWithValue("actor",actor);
            seed.Parameters.AddWithValue("email",email); seed.Parameters.AddWithValue("other",other); seed.Parameters.AddWithValue("future",future);
            await seed.ExecuteNonQueryAsync(ct);
        }
        var services = new ServiceCollection(); services.AddLogging();
        services.AddSingleton(new PostgresConnectionFactory(apiConnection)); services.AddSingleton<IClock,SystemClock>();
        services.AddSingleton<PostgresBackgroundJobStore>(); services.AddSingleton<ICommandActorAuthorization,NoActorFixture>();
        services.AddSingleton(new IdentityPolicy(true,true,12,TimeSpan.FromDays(1),TimeSpan.FromHours(1)));
        services.AddStrataAiOrganizations(new(RuntimeMode.Production,"contract","contract"));
        services.AddStrataAiWorkManagement(new(RuntimeMode.Production,"contract","contract"));
        await using var provider = services.BuildServiceProvider();
        var api = provider.GetRequiredService<PostgresConnectionFactory>();
        var unit = provider.GetRequiredService<IWorkManagementUnitOfWork>(); var organizations = provider.GetRequiredService<IOrganizationStore>();
        var publisher = new PostgresOrganizationDeletionJobPublisher(api,new PostgresBackgroundJobStore(api));
        Task<WorkOperation<bool>> Scope(Guid scope, Func<Task<WorkOperation<bool>>> action) =>
            unit.ExecuteReadAsync(scope,null,"fixture_scope",()=>Task.FromResult(true),action,ct);
        Task Audit(Guid scope, string correlation) => organizations.AppendAuditAsync(scope,actor,"ORGANIZATION_DELETION_REQUESTED","Organization",scope,correlation,ct);
        async Task AssertUnpublished()
        {
            await using var query = new NpgsqlCommand("""
                SELECT o.status='ACTIVE' AND o.version=1
                 AND NOT EXISTS(SELECT 1 FROM invitation_recipient_organization_lifecycle_proofs WHERE tenant_id=@tenant)
                 AND NOT EXISTS(SELECT 1 FROM invitation_recipient_organization_lifecycle_sources WHERE tenant_id=@tenant)
                 AND NOT EXISTS(SELECT 1 FROM invitation_recipient_authority_sources WHERE tenant_id=@tenant)
                 AND NOT EXISTS(SELECT 1 FROM background_jobs WHERE tenant_id=@tenant)
                 AND NOT EXISTS(SELECT 1 FROM audit_events WHERE tenant_id=@tenant)
                 FROM organizations o WHERE id=@tenant;
                """,admin);
            query.Parameters.AddWithValue("tenant",tenant); Require(await query.ExecuteScalarAsync(ct) is true,"Failed lifecycle publication retained state/proof/source/jobs.");
        }
        Require(!(await Scope(tenant,async()=>{await Audit(tenant,"unproven-request");return WorkOperation<bool>.Success(true);})).Succeeded,
            "Deletion request audit without a real transition was accepted.");
        await AssertUnpublished();
        Require(!(await Scope(tenant,async()=>{
            Require(await organizations.MarkDeletingAsync(tenant,1,DateTimeOffset.UtcNow,ct),"Tentative deletion mutation failed.");
            await Audit(tenant,new string('x',65)); return WorkOperation<bool>.Success(true);
        })).Succeeded,"Invalid lifecycle correlation was accepted.");
        await AssertUnpublished();
        var declined = await Scope(tenant,async()=>{
            Require(await organizations.MarkDeletingAsync(tenant,1,DateTimeOffset.UtcNow,ct),"Tentative publication mutation failed.");
            await Audit(tenant,"tentative-request");
            Require(await publisher.PublishAsync(tenant,actor,request,2,"tentative-request",ct),"Tentative deletion job publication failed.");
            return WorkOperation<bool>.Failure("declared_late_refusal");
        });
        Require(declined.ErrorCode=="declared_late_refusal","Late command refusal was lost."); await AssertUnpublished();
        Require((await Scope(earlierTenant,async()=>{
            Require(await organizations.MarkDeletingAsync(earlierTenant,1,DateTimeOffset.UtcNow,ct),"Earlier transition fixture failed.");
            return WorkOperation<bool>.Success(true);
        })).Succeeded,"Earlier transition command failed.");
        Require(!(await Scope(earlierTenant,async()=>{await Audit(earlierTenant,"earlier-request");return WorkOperation<bool>.Success(true);})).Succeeded,
            "Another transaction published an earlier unaudited transition.");
        Require((await Scope(tenant,async()=>{
            Require(await organizations.MarkDeletingAsync(tenant,1,DateTimeOffset.UtcNow,ct),"Committed deletion mutation failed.");
            await Audit(tenant,"committed-lifecycle");
            Require(await publisher.PublishAsync(tenant,actor,request,2,"committed-lifecycle",ct),"Committed deletion root failed.");
            return WorkOperation<bool>.Success(true);
        })).Succeeded,"Actual deletion/source/root command failed.");
        await using var worker = new PostgresConnectionFactory(workerConnection);
        var authorityJobs = new PostgresBackgroundJobStore(worker,authorityJobsOnly:true);
        var authority = new InvitationRecipientAuthorityDeliveryHandler(new PostgresInvitationRecipientAuthorityDeliveryStore(worker));
        var requestPages = 0;
        while (await authorityJobs.ClaimAsync(tenant,Guid.NewGuid(),ct) is { } claim)
        {
            await authority.ExecuteAsync(claim,ct); await authority.ExecuteAsync(claim,ct);
            Require(await authorityJobs.CompleteAsync(tenant,claim.Id,claim.LeaseId,claim.WorkerId,ct),"Request authority acknowledgment failed.");
            Require(++requestPages<=3,"Request authority repeated a page.");
        }
        Require(requestPages==3,"Request authority did not deliver 100/100/5 pages.");
        // Accepted source attribution survives later account retirement. The
        // original leased deletion capability remains authoritative for completion.
        await using(var retire = new NpgsqlCommand("UPDATE users SET status='DEACTIVATED',updated_at=clock_timestamp(),version=version+1 WHERE id=@actor",admin))
        { retire.Parameters.AddWithValue("actor",actor); await retire.ExecuteNonQueryAsync(ct); }
        var processor = new BackgroundJobProcessor(new PostgresBackgroundJobStore(worker),new SystemClock(),[
            authority,new OrganizationDeletionPageHandler(new PostgresOrganizationDeletionPageStore(worker)),
            new WorkEventDeliveryHandler(new PostgresWorkEventDeliveryStore(worker)),
            new OrganizationLifecycleDeliveryHandler(new PostgresOrganizationLifecycleDeliveryStore(worker))]);
        var completed = 0;
        while(true)
        {
            var outcome = await processor.ProcessOneAsync(tenant,Guid.NewGuid(),ct); if(outcome==JobProcessingResult.Empty)break;
            Require(outcome==JobProcessingResult.Completed && ++completed<=20,"Lifecycle graph or authority delivery did not converge.");
        }
        await using(var verify = new NpgsqlCommand("""
            SELECT
             (SELECT status='DELETED' AND version=3 AND deleted_by=@actor FROM organizations WHERE id=@tenant),
             (SELECT count(*)=2 AND bool_and(actor_id=@actor AND correlation_id='committed-lifecycle') FROM invitation_recipient_organization_lifecycle_sources WHERE tenant_id=@tenant),
             (SELECT count(*)=2 AND bool_and(organization_lifecycle_source_id=event_id AND metadata_event_id IS NULL AND work_event_id IS NULL)
               FROM invitation_recipient_authority_sources WHERE tenant_id=@tenant),
             (SELECT array_agg(scanned_count ORDER BY scanned_count)=ARRAY[5,5,100,100,100,100] AND bool_and(completed_at IS NOT NULL)
               FROM invitation_recipient_authority_pages WHERE tenant_id=@tenant),
             (SELECT count(*)=6 AND bool_and(state='SUCCEEDED' AND attempt_count=1) FROM background_jobs
               WHERE tenant_id=@tenant AND job_type='INVITATION_RECIPIENT_AUTHORITY_PAGE'),
             (SELECT count(*)=4 FROM invitation_recipient_authority_effects WHERE tenant_id=@tenant),
             (SELECT count(*)=2 AND bool_and(revision=2) FROM invitation_recipient_authority_revisions WHERE email_normalized IN (@email,@other)),
             NOT EXISTS(SELECT 1 FROM invitation_recipient_authority_revisions WHERE email_normalized=@future),
             (SELECT count(*)=1 FROM audit_events WHERE tenant_id=@tenant AND event_type='ORGANIZATION_DELETION_REQUESTED'),
             (SELECT count(*)=1 FROM organization_lifecycle_events WHERE tenant_id=@tenant AND actor_id=@actor AND ready_at IS NOT NULL),
             NOT EXISTS(SELECT 1 FROM audit_events WHERE tenant_id=@tenant AND entity_type='Invitation');
            """,admin))
        {
            verify.Parameters.AddWithValue("tenant",tenant); verify.Parameters.AddWithValue("actor",actor);
            verify.Parameters.AddWithValue("email",email); verify.Parameters.AddWithValue("other",other); verify.Parameters.AddWithValue("future",future);
            await using var row=await verify.ExecuteReaderAsync(ct);
            Require(await row.ReadAsync(ct)&&Enumerable.Range(0,11).All(index=>!row.IsDBNull(index)&&row.GetBoolean(index)),
                "Lifecycle authority lost canonical sources, retained attribution, bounded pages, cutoff, deduplication or terminal delivery.");
        }
        foreach(var factory in new[]{api,worker})
        {
            foreach(var table in new[]{"invitation_recipient_organization_lifecycle_proofs","invitation_recipient_organization_lifecycle_sources"})
            {
                await using var scope=await factory.OpenTenantSessionAsync(tenant,ct);
                await using var query=new NpgsqlCommand($"SELECT * FROM {table} LIMIT 1",scope.Connection,scope.Transaction);
                try { await query.ExecuteNonQueryAsync(ct); throw new InvalidOperationException("Runtime read private lifecycle history."); }
                catch(PostgresException error) when(error.SqlState==PostgresErrorCodes.InsufficientPrivilege) { }
                // Dispose each refused transaction before the next private table.
            }
        }
        Console.WriteLine("Organization recipient lifecycle: actual restricted request/source/root atomicity, unproven/earlier-transaction/correlation/late-refusal rollback, leased 100/100/5 request and terminal delivery, retired accepted actor, recipient deduplication/cutoff, private capability denial and ready canonical completion passed.");
    }
}
