using System.Text.Json;
using Npgsql;
using StrataAI.Application.Organizations;
using StrataAI.Infrastructure.BackgroundJobs;
using StrataAI.Infrastructure.Organizations;
using StrataAI.Infrastructure.Persistence;

// Restricted reference traversal only. Checkpoints are admin-staged, not advanced
// by a Worker mutation; no graph tombstone or product completion claim is made.
internal static class OrganizationDeletionCandidatesContract
{
    public static async Task RunAsync(NpgsqlConnection admin, string apiConnection, string workerConnection, CancellationToken ct)
    {
        var tenant = Guid.NewGuid(); var actor = Guid.NewGuid(); var request = Guid.NewGuid(); var board = Guid.NewGuid();
        void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        async Task Admin(string sql, Guid? cursor = null)
        {
            await using var command = new NpgsqlCommand(sql, admin);
            command.Parameters.AddWithValue("tenant", tenant); command.Parameters.AddWithValue("actor", actor);
            command.Parameters.AddWithValue("board", board); command.Parameters.AddWithValue("request", request);
            command.Parameters.AddWithValue("email", $"candidate-{actor:N}@example.test");
            command.Parameters.AddWithValue("cursor", NpgsqlTypes.NpgsqlDbType.Uuid, (object?)cursor ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(ct);
        }
        await Admin("""
            INSERT INTO users(id,email,email_normalized,display_name,status,password_hash,created_at,updated_at)
             VALUES(@actor,@email,upper(@email),'Private candidate fixture','ACTIVE','unused',now(),now());
            INSERT INTO organizations(id,name,owner_user_id,status,version,created_at,updated_at)
             VALUES(@tenant,'Private candidate fixture',@actor,'DELETING',2,now(),now());
            INSERT INTO organization_members(id,tenant_id,user_id,role,status) VALUES(gen_random_uuid(),@tenant,@actor,'OWNER','ACTIVE');
            INSERT INTO boards(id,tenant_id,name,created_at,updated_at) VALUES(@board,@tenant,'Private archived Board',now(),now()),
             (gen_random_uuid(),@tenant,'Private active Board',now(),now());
            UPDATE boards SET lifecycle_state='ARCHIVED',archived_at=now(),updated_at=now(),version=2 WHERE id=@board;
            INSERT INTO board_lists(id,tenant_id,board_id,name,rank,lifecycle_state,archived_at,created_at,updated_at)
             SELECT md5(@tenant::text||':list:'||n)::uuid,@tenant,@board,'Private List','500000000000000000000000000000',
              CASE WHEN n%2=0 THEN 'ARCHIVED' ELSE 'ACTIVE' END,CASE WHEN n%2=0 THEN now() END,now(),now()
             FROM generate_series(1,200) n;
            INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,lifecycle_state,archived_at,created_at,updated_at)
             SELECT md5(@tenant::text||':card:'||n)::uuid,@tenant,@board,md5(@tenant::text||':list:'||(1+n%200))::uuid,
              'Private Card','500000000000000000000000000000',CASE WHEN n<=2 THEN 'ACTIVE' ELSE 'ARCHIVED' END,
              CASE WHEN n>2 THEN now() END,now(),now() FROM generate_series(1,100002) n;
            INSERT INTO attachments(id,tenant_id,card_id,uploader_id,kind,display_name,url,scan_status,created_at,updated_at)
             SELECT md5(@tenant::text||':attachment:'||n)::uuid,@tenant,md5(@tenant::text||':card:1')::uuid,@actor,
              'URL','Private attachment','https://example.test/private','NOT_APPLICABLE',now(),now() FROM generate_series(1,3) n;
            UPDATE attachments SET lifecycle_state='ARCHIVED',archived_at=now(),updated_at=now(),version=2
             WHERE tenant_id=@tenant AND id<>md5(@tenant::text||':attachment:1')::uuid;
            UPDATE attachments SET lifecycle_state='DELETED',deleted_at=now(),deleted_by=@actor,updated_at=now(),version=3
             WHERE tenant_id=@tenant AND id=md5(@tenant::text||':attachment:3')::uuid;
            """);
        await using var api = new PostgresConnectionFactory(apiConnection);
        await using var worker = new PostgresConnectionFactory(workerConnection);
        var job = OrganizationDeletionJobs.Create(tenant, actor, new(request,request,2), "candidate-read");
        await using (var publish = await api.OpenTenantSessionAsync(tenant, ct))
        {
            await using var command = new NpgsqlCommand("""
                INSERT INTO organization_deletion_requests(tenant_id,request_id,actor_id,accepted_version,correlation_id)
                 VALUES(@tenant,@request,@actor,2,'candidate-read');
                INSERT INTO organization_deletion_progress(tenant_id,request_id,step_id,phase) VALUES(@tenant,@request,@request,'ATTACHMENTS');
                """, publish.Connection, publish.Transaction);
            command.Parameters.AddWithValue("tenant", tenant); command.Parameters.AddWithValue("request", request); command.Parameters.AddWithValue("actor", actor);
            await command.ExecuteNonQueryAsync(ct);
            Require(await new PostgresBackgroundJobStore(api).PublishAsync(publish, job, ct), "Candidate fixture publication failed.");
            await publish.CommitAsync(ct);
        }
        var claim = await new PostgresBackgroundJobStore(worker).ClaimAsync(tenant, Guid.NewGuid(), ct)
            ?? throw new InvalidOperationException("Candidate fixture claim failed.");
        var reader = new PostgresOrganizationDeletionCandidateReader(worker); var attempt = new OrganizationDeletionAttempt(request,request,2);
        var attachments = await reader.ReadAsync(claim, attempt, 128, ct);
        Require(attachments is { Phase: OrganizationDeletionPhase.Attachments, NextCursor: null } && attachments.Items.Count == 2
            && attachments.Items.All(x => x.BoardId == board && x.CardId is not null)
            && attachments.Items.Select(x => x.State).Order().SequenceEqual(new[] { "ACTIVE", "ARCHIVED" }),
            "Candidate traversal omitted archived attachments, leaked deleted history or lost parent references.");
        var serialized = JsonSerializer.Serialize(attachments);
        Require(!serialized.Contains("Private", StringComparison.Ordinal) && !serialized.Contains("https://", StringComparison.Ordinal),
            "Deletion candidates disclosed names/content/provider references.");
        foreach (var bad in new[] { claim with { OrganizationId = Guid.NewGuid() }, claim with { LeaseId = Guid.NewGuid() },
            claim with { ActorId = Guid.NewGuid() }, claim with { WorkerId = Guid.NewGuid() } })
            Require(await reader.ReadAsync(bad, attempt, 128, ct) is null, "Candidate scope/lease fence failed.");
        Require(await reader.ReadAsync(claim, attempt, 129, ct) is null && await reader.ReadAsync(claim, attempt, 0, ct) is null,
            "Candidate page bound was bypassed.");
        foreach (var mismatch in new[] { attempt with { RequestId = Guid.NewGuid() }, attempt with { StepId = Guid.NewGuid() }, attempt with { AcceptedVersion = 3 } })
            Require(await reader.ReadAsync(claim with { SafeMetadataJson = OrganizationDeletionJobs.Create(tenant,actor,mismatch,"candidate-read").SafeMetadataJson }, mismatch,128,ct) is null,
                "Candidate request/step/version fence failed.");
        await using (var bounded = await worker.OpenTenantSessionAsync(tenant,ct))
        {
            await using var query = new NpgsqlCommand("SELECT public.load_organization_deletion_page(@tenant,@job,@actor,@worker,@lease,@request,@request,2::bigint,129)",bounded.Connection,bounded.Transaction);
            query.Parameters.AddWithValue("tenant",tenant); query.Parameters.AddWithValue("job",claim.Id); query.Parameters.AddWithValue("actor",actor);
            query.Parameters.AddWithValue("worker",claim.WorkerId); query.Parameters.AddWithValue("lease",claim.LeaseId); query.Parameters.AddWithValue("request",request);
            Require(await query.ExecuteScalarAsync(ct) is DBNull,"Database candidate page bound was bypassed.");
        }
        var denied = false;
        try { await new PostgresOrganizationDeletionCandidateReader(api).ReadAsync(claim, attempt, 128, ct); }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.InsufficientPrivilege) { denied = true; }
        Require(denied, "API acquired deletion candidate capability.");
        await Admin("UPDATE organization_deletion_progress SET phase='CARDS',after_id=NULL,version=version+1 WHERE tenant_id=@tenant;");
        // Explicitly extend this disposable fixture lease before scale traversal;
        // real page execution must remain within its normal two-minute claim.
        await Admin($"UPDATE background_jobs SET lease_expires_at=clock_timestamp()+interval '5 minutes' WHERE id='{claim.Id:D}'::uuid;");
        var traversal = System.Diagnostics.Stopwatch.StartNew();
        Guid? previous = null; var visited = 0; var archived = 0;
        while (true)
        {
            var page = await reader.ReadAsync(claim, attempt, 128, ct) ?? throw new InvalidOperationException("Bounded candidate scale read unavailable.");
            Require(page.Phase == OrganizationDeletionPhase.Cards && page.AfterId == previous && page.Items.Count <= 128, "Candidate checkpoint/page bound changed.");
            foreach (var item in page.Items)
            {
                Require(item.BoardId == board && item.CardId == item.Id && (previous is null ||
                    string.CompareOrdinal(item.Id.ToString("N"), previous.Value.ToString("N")) > 0), "Candidate UUID seek repeated, skipped order or crossed scope.");
                previous = item.Id; visited++; if (item.State == "ARCHIVED") archived++;
            }
            if (page.NextCursor is null) break;
            Require(page.NextCursor == previous, "Candidate continuation was not the final visited UUID.");
            await Admin("UPDATE organization_deletion_progress SET after_id=@cursor,version=version+1 WHERE tenant_id=@tenant;", page.NextCursor);
        }
        traversal.Stop();
        Console.WriteLine($"Deletion candidate scale: {visited} Cards ({archived} archived), UUID-seek pages of at most 128, elapsed {traversal.ElapsedMilliseconds}ms; includes admin checkpoint staging.");
        Require(visited == 100002 && archived == 100000, "Candidate traversal lost large archived/active graph rows.");
        foreach (var (phase, expected) in new[] { ("LISTS", 200), ("BOARDS", 2) })
        {
            await Admin($"UPDATE organization_deletion_progress SET phase='{phase}',after_id=NULL,version=version+1 WHERE tenant_id=@tenant;");
            var count = 0;
            while (true)
            {
                var page = await reader.ReadAsync(claim, attempt, 128, ct) ?? throw new InvalidOperationException("Parent candidate read unavailable.");
                Require(page.Items.All(x => x.CardId is null) && page.Items.Count <= 128, "Parent candidate shape or bound failed.");
                count += page.Items.Count; if (page.NextCursor is null) break;
                await Admin("UPDATE organization_deletion_progress SET after_id=@cursor,version=version+1 WHERE tenant_id=@tenant;", page.NextCursor);
            }
            Require(count == expected, "Candidate traversal omitted archived parent rows.");
        }
        await Admin("UPDATE organization_deletion_progress SET phase='FINALIZE',after_id=NULL,version=version+1 WHERE tenant_id=@tenant;");
        Require(await reader.ReadAsync(claim, attempt, 128, ct) is { Phase: OrganizationDeletionPhase.Finalize, Items.Count: 0 }, "Finalize checkpoint was not explicit.");
        await Admin($"UPDATE background_jobs SET lease_expires_at=clock_timestamp()-interval '1 second' WHERE id='{claim.Id:D}'::uuid;");
        Require(await reader.ReadAsync(claim, attempt, 128, ct) is null, "Expired candidate read disclosed graph references.");
        Console.WriteLine("Deletion candidates: restricted scoped UUID traversal of 100000 archived Cards, 200 Lists, archived parents/attachments and bounded private-free pages passed.");
    }
}
