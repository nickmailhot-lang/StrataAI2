using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using System.Text.Json;
using Npgsql;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Runtime;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.BackgroundJobs;
using StrataAI.Infrastructure.Identity;
using StrataAI.Infrastructure.Organizations;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Infrastructure.WorkManagement;

// PRD-05: real restricted Application commands, audit/journal/outbox and receipts.
// Initial accounts and session admission are fixtures, not HTTP or image proof.
internal static class BoardMemberEventContract
{
    private sealed class Context : IWorkCommandContext { public Guid? IdempotencyKey { get; set; } }
    private sealed class Actor : ICommandActorAuthorization
    {
        public Task<bool> VerifyAsync(Guid actor, CancellationToken ct = default)
        { ct.ThrowIfCancellationRequested(); return Task.FromResult(true); }
    }

    public static async Task RunAsync(NpgsqlConnection admin, string apiConnection, CancellationToken ct)
    {
        var tenant = Guid.NewGuid(); var owner = Guid.NewGuid(); var recipient = Guid.NewGuid(); var board = Guid.NewGuid();
        void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        await using (var seed = new NpgsqlCommand("""
            INSERT INTO users(id,email,email_normalized,display_name,status,email_verified,password_hash,created_at,updated_at)
             SELECT id, id::text||'@example.test',upper(id::text||'@example.test'),'Member event fixture','ACTIVE',true,'unused-contract-hash',now(),now()
             FROM unnest(ARRAY[@owner,@recipient]::uuid[]) id;
            INSERT INTO organizations(id,name,owner_user_id,created_at,updated_at)
             VALUES(@tenant,'Member event fixture',@owner,now(),now());
            INSERT INTO organization_members(id,tenant_id,user_id,role,status)
             VALUES(gen_random_uuid(),@tenant,@owner,'OWNER','ACTIVE'),(gen_random_uuid(),@tenant,@recipient,'MEMBER','ACTIVE');
            INSERT INTO boards(id,tenant_id,name,created_at,updated_at)
             VALUES(@board,@tenant,'Member event fixture',now(),now());
            """, admin))
        {
            seed.Parameters.AddWithValue("owner", owner); seed.Parameters.AddWithValue("recipient", recipient);
            seed.Parameters.AddWithValue("tenant", tenant); seed.Parameters.AddWithValue("board", board);
            await seed.ExecuteNonQueryAsync(ct);
        }
        var services = new ServiceCollection(); services.AddLogging();
        services.AddSingleton(new PostgresConnectionFactory(apiConnection)); services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ICommandActorAuthorization, Actor>(); services.AddSingleton<PostgresBackgroundJobStore>();
        var context = new Context(); services.AddSingleton<IWorkCommandContext>(context);
        var runtime = new RuntimeDescriptor(RuntimeMode.Production, "contract", "contract");
        var settings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["STRATAAI_AUTH_RETRY_CURRENT_KEY"] = "contract",
            ["STRATAAI_AUTH_RETRY_KEYS"] = JsonSerializer.Serialize(new Dictionary<string, string> {
                ["contract"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }) }).Build();
        services.AddStrataAiIdentity(settings, runtime);
        services.AddStrataAiOrganizations(runtime); services.AddStrataAiWorkManagement(runtime);
        await using var provider = services.BuildServiceProvider();
        var work = provider.GetRequiredService<IWorkManagementService>();
        async Task<T> Scalar<T>(string sql, string? correlation = null, string? type = null)
        {
            await using var query = new NpgsqlCommand(sql, admin);
            query.Parameters.AddWithValue("tenant", tenant); query.Parameters.AddWithValue("board", board);
            query.Parameters.AddWithValue("recipient", recipient); query.Parameters.AddWithValue("owner", owner);
            if (correlation is not null) query.Parameters.AddWithValue("correlation", correlation);
            if (type is not null) query.Parameters.AddWithValue("type", type);
            return (T)(await query.ExecuteScalarAsync(ct))!;
        }
        Task<string> Snapshot() => Scalar<string>("""
            SELECT jsonb_build_object(
             'members',(SELECT jsonb_agg(to_jsonb(m) ORDER BY user_id) FROM board_members m WHERE tenant_id=@tenant),
             'audit',(SELECT jsonb_agg(to_jsonb(a) ORDER BY id) FROM audit_events a WHERE tenant_id=@tenant),
             'events',(SELECT jsonb_agg(to_jsonb(e) ORDER BY event_id) FROM work_events e WHERE tenant_id=@tenant),
             'streams',(SELECT jsonb_agg(to_jsonb(s) ORDER BY board_id) FROM work_event_streams s WHERE tenant_id=@tenant),
             'jobs',(SELECT jsonb_agg(to_jsonb(j) ORDER BY id) FROM background_jobs j WHERE tenant_id=@tenant),
             'receipts',(SELECT jsonb_agg(to_jsonb(r) ORDER BY key_id) FROM work_command_replays r WHERE tenant_id=@tenant))::text;
            """);
        Task<WorkOperation<BoardMemberRecord>> Change(BoardRole role, string correlation, Guid key, long? version = null, Guid? actor = null)
        {
            context.IdempotencyKey = key;
            return work.SetBoardMemberAsync(board, actor ?? owner, recipient, role, correlation, ct, version);
        }
        async Task Verify(string correlation, string expected)
        {
            var count = await Scalar<long>("""
                SELECT count(*) FROM work_events e JOIN audit_events a
                 ON a.tenant_id=e.tenant_id AND a.correlation_id=e.correlation_id
                 AND a.event_type=e.event_type AND a.actor_id=e.actor_id AND a.entity_type=e.entity_type AND a.entity_id=e.entity_id
                WHERE e.tenant_id=@tenant AND e.board_id=@board AND e.actor_id=@owner AND e.entity_type='Board'
                 AND e.entity_id=@board AND e.entity_version=1 AND e.created_at IS NOT NULL
                 AND e.event_id<>'00000000-0000-0000-0000-000000000000' AND e.correlation_id=@correlation
                 AND e.event_type=@type;
                """, correlation, expected);
            Require(count == 1, "Member transition lost its canonical matching audit/journal identity.");
            Require(await Scalar<long>("""
                SELECT count(*) FROM background_jobs j JOIN work_events e ON j.tenant_id=e.tenant_id
                 AND j.safe_metadata->>'eventId'=e.event_id::text AND j.safe_metadata->>'boardId'=e.board_id::text
                 AND j.idempotency_key='work-event/'||replace(e.event_id::text,'-','')
                WHERE j.tenant_id=@tenant AND j.actor_id=@owner AND j.correlation_id=@correlation
                 AND e.correlation_id=@correlation AND j.job_type='WORK_EVENT_READY';
                """, correlation) == 1,
                "Member transition did not publish exactly one durable delivery job.");
        }
        long version = 0;
        foreach (var transition in new[] {
            (BoardRole.Member, "BOARD_MEMBER_ADDED"), (BoardRole.Admin, "BOARD_MEMBER_ROLE_CHANGED"),
            (BoardRole.Member, "BOARD_MEMBER_ROLE_CHANGED"), (BoardRole.Member, "BOARD_MEMBER_UPDATED") })
        {
            var correlation = Guid.NewGuid().ToString("N"); var key = Guid.NewGuid();
            var changed = await Change(transition.Item1, correlation, key, version == 0 ? null : version);
            Require(changed.Succeeded && changed.Value is { Active: true } && changed.Value.Version == version + 1,
                "Restricted member transition failed or changed its revision semantics.");
            version = changed.Value!.Version; await Verify(correlation, transition.Item2);
            var snapshot = await Snapshot(); var replay = await Change(transition.Item1, correlation, key, version - 1 == 0 ? null : version - 1);
            Require(replay.Succeeded && replay.Value == changed.Value && snapshot == await Snapshot(),
                "Original receipt replay changed canonical member/event/audit/outbox effects.");
        }
        var before = await Snapshot();
        Require((await Change(BoardRole.Admin, "stale", Guid.NewGuid(), version + 1)).ErrorCode == "version_conflict"
            && await Snapshot() == before, "Stale member command retained protected effects.");
        Require((await Change(BoardRole.Admin, "denied", Guid.NewGuid(), version, recipient)).ErrorCode == "board_not_found"
            && await Snapshot() == before, "Unauthorized member command retained protected effects.");
        var fault = "ci_member_event_" + board.ToString("N"); var failedKey = Guid.NewGuid(); var failedCorrelation = Guid.NewGuid().ToString("N");
        await using (var install = new NpgsqlCommand($"""
            CREATE FUNCTION public.{fault}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
             IF NEW.tenant_id='{tenant:D}'::uuid AND NEW.correlation_id='{failedCorrelation}' THEN
              RAISE EXCEPTION 'Injected member journal failure' USING ERRCODE='23514'; END IF; RETURN NEW; END $$;
            CREATE TRIGGER {fault} BEFORE INSERT ON work_events FOR EACH ROW EXECUTE FUNCTION public.{fault}();
            """, admin)) await install.ExecuteNonQueryAsync(ct);
        try
        {
            Require((await Change(BoardRole.Admin, failedCorrelation, failedKey, version)).ErrorCode == "work_storage_unavailable"
                && await Snapshot() == before, "Late journal failure retained membership, audit, stream, outbox or receipt effects.");
        }
        finally
        {
            await using var remove = new NpgsqlCommand($"DROP TRIGGER {fault} ON work_events; DROP FUNCTION public.{fault}();", admin);
            await remove.ExecuteNonQueryAsync(ct);
        }
        var recovered = await Change(BoardRole.Admin, failedCorrelation, failedKey, version);
        Require(recovered.Succeeded && recovered.Value!.Version == version + 1, "Rolled-back receipt prevented original-key recovery.");
        await Verify(failedCorrelation, "BOARD_MEMBER_ROLE_CHANGED");
        var committed = await Snapshot();
        Require((await Change(BoardRole.Admin, failedCorrelation, failedKey, version)).Value == recovered.Value && committed == await Snapshot(),
            "Recovered original receipt duplicated its effects.");
        context.IdempotencyKey = Guid.NewGuid();
        Require((await work.RemoveBoardMemberAsync(board, owner, recipient, "member-event-remove", ct, recovered.Value!.Version)).Succeeded,
            "Restricted member removal failed before re-grant.");
        var regrantCorrelation = Guid.NewGuid().ToString("N"); var regrantKey = Guid.NewGuid();
        var regrant = await Change(BoardRole.Member, regrantCorrelation, regrantKey);
        Require(regrant.Succeeded && regrant.Value is { Active: true, Role: BoardRole.Member }
            && regrant.Value.Version == recovered.Value.Version + 2, "Re-grant did not preserve the removed membership revision.");
        await Verify(regrantCorrelation, "BOARD_MEMBER_ADDED");
        var regranted = await Snapshot();
        Require((await Change(BoardRole.Member, regrantCorrelation, regrantKey)).Value == regrant.Value && await Snapshot() == regranted,
            "Re-grant receipt replay duplicated effects.");
        board = Guid.NewGuid();
        await using (var fresh = new NpgsqlCommand("INSERT INTO boards(id,tenant_id,name,created_at,updated_at) VALUES(@board,@tenant,'Initial Admin grant',now(),now());", admin))
        {
            fresh.Parameters.AddWithValue("board", board); fresh.Parameters.AddWithValue("tenant", tenant); await fresh.ExecuteNonQueryAsync(ct);
        }
        var initialAdminCorrelation = Guid.NewGuid().ToString("N"); var initialAdminKey = Guid.NewGuid();
        var initialAdmin = await Change(BoardRole.Admin, initialAdminCorrelation, initialAdminKey);
        Require(initialAdmin.Succeeded && initialAdmin.Value is { Active: true, Role: BoardRole.Admin, Version: 1 },
            "Initial Admin grant was not a first membership.");
        await Verify(initialAdminCorrelation, "BOARD_MEMBER_ADDED");
        var initialAdminState = await Snapshot();
        Require((await Change(BoardRole.Admin, initialAdminCorrelation, initialAdminKey)).Value == initialAdmin.Value
            && await Snapshot() == initialAdminState, "Initial Admin grant replay duplicated effects.");
        Console.WriteLine("Board member event contract passed restricted audit/journal/outbox identity, receipt replay, refusals and late rollback/recovery.");
    }
}
