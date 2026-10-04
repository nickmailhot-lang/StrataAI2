using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StrataAI.Application.WorkManagement;

// Real PostgreSQL, restricted API store and synthetic null-actor admission.
// This proves query semantics/scope, not HTTP session authorization.
internal static class SearchContentStoreContract
{
    public static async Task RunAsync(NpgsqlConnection admin, IServiceProvider provider, Guid tenant, Guid board, Guid user, CancellationToken ct)
    {
        var list = Guid.NewGuid(); var label = Guid.NewGuid(); var membership = Guid.NewGuid();
        var ids = Enumerable.Range(1, 52).Select(n => Guid.Parse($"90000000-0000-4000-8000-{n:000000000000}")).ToArray();
        var early = Guid.Parse("10000000-0000-4000-8000-000000000001");
        var all = ids.Append(early).ToArray();
        var store = provider.GetRequiredService<IWorkManagementStore>(); var unit = provider.GetRequiredService<IWorkManagementUnitOfWork>();
        void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        async Task<IReadOnlyList<CardRecord>> Read(GlobalSearchBinding binding, Guid? after = null)
        {
            var result = await unit.ExecuteReadAsync(tenant, null, "fixture_denied", () => Task.FromResult(true),
                async () => WorkOperation<IReadOnlyList<CardRecord>>.Success(await store.SearchBoardCardsAsync(board, binding, false, after, ct)), ct);
            Require(result.Succeeded, "Search content fixture scope failed."); return result.Value!;
        }
        async Task Change(string sql)
        {
            await using var command = new NpgsqlCommand(sql, admin);
            command.Parameters.AddWithValue("membership", membership); command.Parameters.AddWithValue("label", label);
            command.Parameters.AddWithValue("list", list); await command.ExecuteNonQueryAsync(ct);
        }
        try
        {
            await using (var seed = new NpgsqlCommand("""
                INSERT INTO board_lists(id,tenant_id,board_id,name,rank,created_at,updated_at)
                  VALUES(@list,@tenant,@board,'Search content','600000000000000000000000000000',now(),now());
                INSERT INTO cards(id,tenant_id,board_id,list_id,title,description,rank,created_at,updated_at)
                  SELECT id,@tenant,@board,@list,'Search needle','Literal 100%_',lpad(n::text,30,'0'),now(),now()
                  FROM unnest(@ids) WITH ORDINALITY t(id,n);
                INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,created_at,updated_at)
                  VALUES(@early,@tenant,@board,@list,'Earlier unmatched','700000000000000000000000000000',now(),now());
                INSERT INTO board_labels(id,tenant_id,board_id,name,color,rank)
                  VALUES(@label,@tenant,@board,'Priority','red','500000000000000000000000000000');
                INSERT INTO card_labels(tenant_id,board_id,card_id,label_id)
                  SELECT @tenant,@board,id,@label FROM unnest(@ids) id;
                INSERT INTO board_members(id,tenant_id,board_id,user_id,role,status,created_at,updated_at)
                  VALUES(@membership,@tenant,@board,@user,'MEMBER','ACTIVE',now(),now());
                INSERT INTO card_members(tenant_id,board_id,card_id,user_id,assigned_by)
                  SELECT @tenant,@board,id,@user,@user FROM unnest(@ids) id;
                """, admin))
            {
                seed.Parameters.AddWithValue("tenant", tenant); seed.Parameters.AddWithValue("board", board);
                seed.Parameters.AddWithValue("user", user); seed.Parameters.AddWithValue("list", list);
                seed.Parameters.AddWithValue("ids", ids); seed.Parameters.AddWithValue("early", early);
                seed.Parameters.AddWithValue("label", label); seed.Parameters.AddWithValue("membership", membership);
                await seed.ExecuteNonQueryAsync(ct);
            }
            var binding = new GlobalSearchBinding(user, "100%_", "PRIOR", "Persistence contract", true, GlobalSearchLifecycleScope.Active);
            var first = await Read(binding); Require(first.Count == 51, "Search predicates did not precede lookahead cap.");
            var tail = await Read(binding, first[49].Id);
            Require(first.Take(50).Concat(tail).Select(c => c.Id).SequenceEqual(ids.Order()), "Search seek lost or duplicated matching Cards.");
            Require((await Read(binding with { Keyword = "absent" })).Count == 0, "Search ALL widened matching.");
            Require((await Read(binding with { Keyword = "unmatched", MatchAll = false })).Count == 51, "Search ANY omitted selected dimensions.");
            await Change("UPDATE board_members SET status='REMOVED' WHERE id=@membership;");
            Require((await Read(binding)).Count == 0, "Search retained an ineligible assignee match.");
            await Change("UPDATE board_members SET status='ACTIVE' WHERE id=@membership; UPDATE board_labels SET status='DELETED',deleted_at=now() WHERE id=@label;");
            Require((await Read(binding)).Count == 0, "Search retained a deleted label match.");
            await Change("UPDATE board_labels SET status='ACTIVE',deleted_at=NULL WHERE id=@label; UPDATE board_lists SET lifecycle_state='ARCHIVED' WHERE id=@list;");
            Require((await Read(binding)).Count == 0, "Active search disclosed archived-parent Cards.");
            Require((await Read(binding with { Scope = GlobalSearchLifecycleScope.Archived })).Count == 51, "Explicit archive search omitted archived-parent Cards.");
            var unscopedRejected = false;
            try { await store.SearchBoardCardsAsync(board, binding, false, null, ct); }
            catch (InvalidOperationException) { unscopedRejected = true; }
            Require(unscopedRejected, "Search content admitted a read without its owning transaction.");
            Console.WriteLine("Restricted search content: literal dimensions, pre-limit predicates, seek, eligible assignees, label lifecycle and archive scope passed.");
        }
        finally
        {
            await using var cleanup = new NpgsqlCommand("""
                DELETE FROM card_members WHERE tenant_id=@tenant AND card_id=ANY(@cards);
                DELETE FROM card_labels WHERE tenant_id=@tenant AND card_id=ANY(@cards);
                DELETE FROM cards WHERE tenant_id=@tenant AND id=ANY(@cards);
                DELETE FROM board_labels WHERE tenant_id=@tenant AND id=@label;
                DELETE FROM board_lists WHERE tenant_id=@tenant AND id=@list;
                DELETE FROM board_members WHERE tenant_id=@tenant AND id=@membership;
                """, admin);
            cleanup.Parameters.AddWithValue("tenant", tenant); cleanup.Parameters.AddWithValue("cards", all);
            cleanup.Parameters.AddWithValue("label", label); cleanup.Parameters.AddWithValue("list", list);
            cleanup.Parameters.AddWithValue("membership", membership); await cleanup.ExecuteNonQueryAsync(ct);
        }
    }
}
