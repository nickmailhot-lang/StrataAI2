using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StrataAI.Application.WorkManagement;

// PRD-01-VS-05-T-01/AC-02/04: real restricted admission while a current parent
// is busy. Raw fixture withdrawal is controlled scheduling, not an HTTP logout,
// account-admin/lifecycle command or production deployment certification.
internal static class NavigationParentWithdrawalContract
{
    public static async Task RunAsync(NpgsqlConnection admin, string apiConnection, CancellationToken ct)
    {
        var failures = new List<string>();
        foreach (var mode in new[] { "session", "session-replay", "account", "archived-board", "membership-replay", "cross-organization", "mismatched-organization" })
        {
            try { await RunCase(admin, apiConnection, mode, ct); Console.WriteLine($"PASS current parent/navigation withdrawal: {mode}."); }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                failures.Add(mode);
                var reason = error is PostgresException database ? database.SqlState : error is InvalidOperationException ? error.Message : error.GetType().Name;
                Console.WriteLine($"FAIL current parent/navigation withdrawal: {mode}: {reason}");
            }
        }
        Require(failures.Count == 0, $"Current parent/navigation withdrawal failed in {failures.Count}/7 cases: {string.Join(", ", failures)}.");
    }

    private static async Task RunCase(NpgsqlConnection admin, string apiConnection, string mode, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(30)); var token = deadline.Token;
        var actor = Guid.NewGuid(); var owner = Guid.NewGuid(); var org = Guid.NewGuid(); var foreignOrg = Guid.NewGuid();
        var board = Guid.NewGuid(); var foreignBoard = Guid.NewGuid(); var list = Guid.NewGuid(); var card = Guid.NewGuid();
        var session = Guid.NewGuid(); var hash = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)); var request = Guid.NewGuid();
        await using (var seed = new NpgsqlCommand("""
            INSERT INTO users(id,email,email_normalized,display_name,status,email_verified,password_hash,created_at,updated_at)
             VALUES(@actor,@email,upper(@email),'Current Member','ACTIVE',true,'unused-contract-hash',now(),now()),
              (@owner,@owner_email,upper(@owner_email),'Retained Owner','ACTIVE',true,'unused-contract-hash',now(),now());
            INSERT INTO sessions(id,user_id,token_hash,created_at,expires_at)
             VALUES(@session,@actor,@hash,clock_timestamp(),clock_timestamp()+interval '30 minutes');
            INSERT INTO organizations(id,name,owner_user_id,organization_type,created_at,updated_at)
             VALUES(@org,'Current authorized Organization',@owner,'STRATA',now(),now()),
              (@foreign_org,'Protected foreign Organization',@owner,'STRATA',now(),now());
            INSERT INTO organization_members(id,tenant_id,user_id,role,status,created_at,updated_at)
             VALUES(@owner_member,@org,@owner,'OWNER','ACTIVE',now(),now()),
              (@member,@org,@actor,'MEMBER','ACTIVE',now(),now()),
              (@foreign_member,@foreign_org,@owner,'OWNER','ACTIVE',now(),now());
            INSERT INTO boards(id,tenant_id,name,visibility,created_at,updated_at)
             VALUES(@board,@org,'Current Board','PRIVATE',now(),now()),
              (@foreign_board,@foreign_org,'Protected foreign Board','PRIVATE',now(),now());
            INSERT INTO board_members(id,tenant_id,board_id,user_id,role,status,created_at,updated_at)
             VALUES(@board_member,@org,@board,@actor,'ADMIN','ACTIVE',now(),now());
            INSERT INTO board_lists(id,tenant_id,board_id,name,rank,created_at,updated_at)
             VALUES(@list,@org,@board,'Retained List','500000000000000000000000000000',now(),now());
            INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,created_at,updated_at)
             VALUES(@card,@org,@board,@list,'Retained Card','500000000000000000000000000000',now(),now());
            """, admin))
        {
            seed.Parameters.AddWithValue("actor", actor); seed.Parameters.AddWithValue("owner", owner);
            seed.Parameters.AddWithValue("email", $"withdrawal-{actor:N}@example.test"); seed.Parameters.AddWithValue("owner_email", $"owner-{owner:N}@example.test");
            seed.Parameters.AddWithValue("session", session); seed.Parameters.AddWithValue("hash", hash);
            seed.Parameters.AddWithValue("org", org); seed.Parameters.AddWithValue("foreign_org", foreignOrg);
            foreach (var parameter in new[] { "owner_member", "member", "foreign_member", "board_member" }) seed.Parameters.AddWithValue(parameter, Guid.NewGuid());
            seed.Parameters.AddWithValue("board", board); seed.Parameters.AddWithValue("foreign_board", foreignBoard); seed.Parameters.AddWithValue("list", list); seed.Parameters.AddWithValue("card", card);
            await seed.ExecuteNonQueryAsync(token);
        }
        var name = $"nav-withdrawal-{actor:N}";
        await using var provider = NavigationActorBoardLockContract.Provider(apiConnection, name,
            new NavigationActorBoardLockContract.Context(actor, hash), new NavigationActorBoardLockContract.DatabaseCodes());
        var navigation = provider.GetRequiredService<NavigationInteractionReplayProducer>();
        if (mode.EndsWith("-replay", StringComparison.Ordinal))
        {
            var original = await navigation.BoardAsync(actor, org, board, 1, request, new string('b', 64), token);
            Require(original.Succeeded, "Original currently authorized Member navigation was unavailable.");
            var repeated = await navigation.BoardAsync(actor, org, board, 1, request, new string('b', 64), token);
            Require(repeated.Succeeded && repeated.Value == original.Value, "Initial original acknowledgment identity/clock changed.");
        }
        var target = mode is "cross-organization" or "mismatched-organization" ? foreignBoard : board;
        var targetOrg = mode == "cross-organization" ? foreignOrg : org;
        await using var gate = await admin.BeginTransactionAsync(token);
        await using (var parent = new NpgsqlCommand("SELECT id FROM boards WHERE id=@board FOR UPDATE;", admin, gate))
        { parent.Parameters.AddWithValue("board", target); Require(await parent.ExecuteScalarAsync(token) is Guid, "Controlled current parent missing."); }
        var observed = Task.Run(() => navigation.BoardAsync(actor, targetOrg, target, 1, request, new string('b', 64), token), token);
        var released = false;
        try
        {
            if (mode != "mismatched-organization") await Waiting();
            var change = mode switch {
                "session" or "session-replay" => "UPDATE sessions SET revoked_at=clock_timestamp(),updated_at=clock_timestamp() WHERE id=@session;",
                "account" => "UPDATE users SET status='SUSPENDED',updated_at=clock_timestamp(),version=version+1 WHERE id=@actor;",
                "archived-board" => "UPDATE boards SET lifecycle_state='ARCHIVED',archived_at=clock_timestamp(),updated_at=clock_timestamp(),version=version+1 WHERE id=@board;",
                "membership-replay" => "UPDATE organization_members SET status='REMOVED',updated_at=clock_timestamp(),version=version+1 WHERE tenant_id=@org AND user_id=@actor;",
                _ => null
            };
            if (change is not null)
            {
                await using var withdrawal = new NpgsqlCommand(change, admin, gate);
                withdrawal.Parameters.AddWithValue("session", session); withdrawal.Parameters.AddWithValue("actor", actor);
                withdrawal.Parameters.AddWithValue("board", board); withdrawal.Parameters.AddWithValue("org", org);
                Require(await withdrawal.ExecuteNonQueryAsync(token) == 1, "Controlled withdrawal did not update its exact current entity.");
            }
            var expected = await Snapshot(gate);
            await gate.CommitAsync(token); released = true;
            var result = await observed.WaitAsync(TimeSpan.FromSeconds(10), token);
            var code = mode is "session" or "session-replay" or "account" ? "session_unavailable" : "navigation_unavailable";
            Require(!result.Succeeded && result.Value is null && result.ErrorCode == code,
                "Withdrawn/mismatched/private target disclosed or acknowledged protected navigation.");
            Require(await Snapshot(null) == expected, "Denied navigation altered canonical fixture state, original receipts, clocks or private history.");
        }
        finally
        {
            if (!released) await gate.RollbackAsync(CancellationToken.None);
            try { await observed.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None); }
            catch { deadline.Cancel(); }
        }

        async Task Waiting()
        {
            var until = DateTimeOffset.UtcNow.AddSeconds(10);
            while (DateTimeOffset.UtcNow < until)
            {
                Require(!observed.IsCompleted, "Navigation completed before its actual parent admission wait.");
                await using (var clear = new NpgsqlCommand("SELECT pg_stat_clear_snapshot();", admin, gate)) await clear.ExecuteNonQueryAsync(token);
                await using var query = new NpgsqlCommand("""
                    SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE application_name=@name
                     AND state='active' AND wait_event_type='Lock' AND cardinality(pg_blocking_pids(pid))>0);
                    """, admin, gate);
                query.Parameters.AddWithValue("name", name);
                if (await query.ExecuteScalarAsync(token) is true) return;
                await Task.Delay(20, token);
            }
            throw new InvalidOperationException("Current navigation parent wait was not observed.");
        }
        async Task<string> Snapshot(NpgsqlTransaction? transaction)
        {
            await using var query = new NpgsqlCommand("""
                SELECT jsonb_build_object(
                 'user',(SELECT to_jsonb(u) FROM users u WHERE id=@actor),
                 'sessions',(SELECT jsonb_agg(to_jsonb(s) ORDER BY id) FROM sessions s WHERE user_id=@actor),
                 'organizations',(SELECT jsonb_agg(to_jsonb(o) ORDER BY id) FROM organizations o WHERE id=ANY(@orgs)),
                 'members',(SELECT jsonb_agg(to_jsonb(m) ORDER BY id) FROM organization_members m WHERE tenant_id=ANY(@orgs)),
                 'access',(SELECT jsonb_agg(to_jsonb(a) ORDER BY tenant_id,user_id) FROM user_organization_access a WHERE tenant_id=ANY(@orgs)),
                 'boards',(SELECT jsonb_agg(to_jsonb(b) ORDER BY id) FROM boards b WHERE tenant_id=ANY(@orgs)),
                 'board_members',(SELECT jsonb_agg(to_jsonb(m) ORDER BY id) FROM board_members m WHERE tenant_id=ANY(@orgs)),
                 'lists',(SELECT jsonb_agg(to_jsonb(l) ORDER BY id) FROM board_lists l WHERE tenant_id=ANY(@orgs)),
                 'cards',(SELECT jsonb_agg(to_jsonb(c) ORDER BY id) FROM cards c WHERE tenant_id=ANY(@orgs)),
                 'navigation',(SELECT jsonb_agg(to_jsonb(n) ORDER BY event_id) FROM navigation_interaction_events n WHERE actor_id=@actor),
                 'receipts',(SELECT jsonb_agg(to_jsonb(r) ORDER BY request_id) FROM navigation_interaction_replays r WHERE actor_id=@actor),
                 'audit',(SELECT jsonb_agg(to_jsonb(a) ORDER BY id) FROM audit_events a WHERE tenant_id=ANY(@orgs)),
                 'work',(SELECT jsonb_agg(to_jsonb(e) ORDER BY sequence) FROM work_events e WHERE tenant_id=ANY(@orgs)))::text;
                """, admin, transaction);
            query.Parameters.AddWithValue("actor", actor); query.Parameters.AddWithValue("orgs", new[] { org, foreignOrg });
            return (string)(await query.ExecuteScalarAsync(token))!;
        }
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
