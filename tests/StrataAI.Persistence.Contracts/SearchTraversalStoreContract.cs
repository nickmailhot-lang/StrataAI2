using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.Runtime;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.Organizations;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Infrastructure.WorkManagement;

// Restricted store contract, not HTTP actor admission or global search disclosure.
internal static class SearchTraversalStoreContract
{
    public static async Task RunAsync(NpgsqlConnection admin, string apiConnection, Guid tenant, Guid user, CancellationToken ct)
    {
        var tenants = Enumerable.Range(0, 52).Select(_ => Guid.NewGuid()).ToArray();
        var boards = Enumerable.Range(0, 52).Select(n => Guid.Parse($"80000000-0000-4000-8000-{n + 1:000000000000}")).ToArray();
        var hidden = Guid.Parse("00000000-0000-4000-8000-000000000001");
        var archivedBoards = Enumerable.Range(1, 51).Select(n => Guid.Parse($"30000000-0000-4000-8000-{n:000000000000}")).ToArray();
        var searchBoards = boards.Append(hidden).ToArray();
        var allBoards = searchBoards.Concat(archivedBoards).ToArray();
        void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        try
        {
            await using (var seed = new NpgsqlCommand("""
                INSERT INTO organizations(id,name,created_at,updated_at)
                  SELECT id,'Search traversal',now(),now() FROM unnest(@tenants) id;
                INSERT INTO organization_members(id,tenant_id,user_id,role,status)
                  SELECT gen_random_uuid(),id,@user,'MEMBER','ACTIVE' FROM unnest(@tenants) id;
                UPDATE organization_members SET status='REMOVED' WHERE tenant_id=@inactive AND user_id=@user;
                INSERT INTO boards(id,tenant_id,name,visibility,created_at,updated_at)
                  SELECT id,@tenant,'Search traversal','PRIVATE',now(),now() FROM unnest(@all_boards) id;
                INSERT INTO board_members(id,tenant_id,board_id,user_id,role,status,created_at,updated_at)
                  SELECT gen_random_uuid(),@tenant,id,@user,'MEMBER','ACTIVE',now(),now() FROM unnest(@boards) id;
                """, admin))
            {
                seed.Parameters.AddWithValue("tenants", tenants); seed.Parameters.AddWithValue("inactive", tenants[0]);
                seed.Parameters.AddWithValue("tenant", tenant); seed.Parameters.AddWithValue("user", user);
                seed.Parameters.AddWithValue("boards", boards); seed.Parameters.AddWithValue("all_boards", searchBoards);
                await seed.ExecuteNonQueryAsync(ct);
            }
            var services = new ServiceCollection(); services.AddLogging();
            services.AddSingleton(new PostgresConnectionFactory(apiConnection));
            services.AddSingleton(new IdentityPolicy(true, false, 12, TimeSpan.FromDays(1), TimeSpan.FromHours(1)));
            var runtime = new RuntimeDescriptor(RuntimeMode.Production, "contract", "contract");
            services.AddStrataAiOrganizations(runtime); services.AddStrataAiWorkManagement(runtime);
            await using var provider = services.BuildServiceProvider();
            var organizations = provider.GetRequiredService<IOrganizationStore>();
            var work = provider.GetRequiredService<IWorkManagementStore>();
            var first = await organizations.ListMembershipOrganizationIdsPageAsync(user, null, ct);
            Require(first.Count == 51, "Organization traversal did not enforce lookahead cap.");
            var tail = await organizations.ListMembershipOrganizationIdsPageAsync(user, first[49], ct);
            var routes = first.Take(50).Concat(tail).ToArray();
            Require(routes.SequenceEqual(tenants.Append(tenant).Order()), "Organization seek lost, duplicated or widened membership routes.");
            Require(routes.Contains(tenants[0]), "Internal traversal incorrectly omitted inactive membership hints.");
            Require((await organizations.ListMembershipOrganizationIdsPageAsync(Guid.NewGuid(), null, ct)).Count == 0,
                "Routing traversal disclosed another actor's memberships.");
            var visible = await work.ListVisibleBoardsPageAsync(tenant, user, false, null, ct);
            Require(visible.Count == 51 && visible.All(b => b.Id != hidden), "Private Board eligibility did not precede page cap.");
            var boardTail = await work.ListVisibleBoardsPageAsync(tenant, user, false, visible[49].Id, ct);
            Require(visible.Take(50).Concat(boardTail).Select(b => b.Id).SequenceEqual(boards.Order()),
                "Board seek lost, duplicated or disclosed private candidates.");
            Require((await work.ListVisibleBoardsPageAsync(tenant, Guid.NewGuid(), false, null, ct)).Count == 0,
                "Private Board traversal admitted an unknown actor.");
            Require((await work.ListVisibleBoardsPageAsync(tenants[1], user, true, null, ct)).Count == 0,
                "Board traversal crossed its tenant.");
            await using (var archiveSeed = new NpgsqlCommand("""
                INSERT INTO boards(id,tenant_id,name,visibility,lifecycle_state,archived_at,created_at,updated_at)
                  SELECT id,@tenant,'Archived home-directory exclusion','ORGANIZATION','ARCHIVED',now(),now(),now()
                  FROM unnest(@archived) id;
                """, admin))
            {
                archiveSeed.Parameters.AddWithValue("tenant", tenant);
                archiveSeed.Parameters.AddWithValue("archived", archivedBoards);
                await archiveSeed.ExecuteNonQueryAsync(ct);
            }
            var active = await work.ListActiveVisibleBoardsPageAsync(tenant, user, false, null, ct);
            Require(active.Count == 51 && active.All(row => boards.Contains(row.Id)),
                "Active Board directory did not apply lifecycle/private eligibility before its page cap.");
            var activeTail = await work.ListActiveVisibleBoardsPageAsync(tenant, user, false, active[49].Id, ct);
            Require(active.Take(50).Concat(activeTail).Select(row => row.Id).SequenceEqual(boards.Order()),
                "Active Board directory seek lost, duplicated or widened visible rows.");
            Require((await work.ListActiveVisibleBoardsPageAsync(tenant, Guid.NewGuid(), false, null, ct)).Count == 0,
                "Active Board directory exposed archived metadata to an unknown actor.");
            Require((await work.ListActiveVisibleBoardsPageAsync(tenants[1], user, true, null, ct)).Count == 0,
                "Active Board directory crossed its tenant.");
            Require((await work.ListVisibleBoardsPageAsync(tenant, user, false, null, ct))
                .Select(row => row.Id).SequenceEqual(archivedBoards),
                "Active-only home discovery accidentally changed archived search traversal.");
            Console.WriteLine("Restricted active Board directory: pre-limit lifecycle/visibility exclusion, bounded UUID seek and tenant isolation passed.");
            Console.WriteLine("Restricted search traversal: bounded routing, inactive hints, UUID seeks, private pre-limit eligibility and tenant isolation passed.");
        }
        finally
        {
            await using var cleanup = new NpgsqlCommand("""
                DELETE FROM board_members WHERE tenant_id=@tenant AND board_id=ANY(@boards);
                DELETE FROM boards WHERE tenant_id=@tenant AND id=ANY(@boards);
                DELETE FROM organization_members WHERE tenant_id=ANY(@tenants);
                DELETE FROM organizations WHERE id=ANY(@tenants);
                """, admin);
            cleanup.Parameters.AddWithValue("tenant", tenant); cleanup.Parameters.AddWithValue("boards", allBoards);
            cleanup.Parameters.AddWithValue("tenants", tenants); await cleanup.ExecuteNonQueryAsync(ct);
        }
    }
}
