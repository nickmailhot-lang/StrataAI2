using Npgsql;

// PRD-01 / PRD-18 / ARCH-04: warm the same admin backend before the unchanged
// 100,002-Card candidate graph. Keep original Npgsql and lease deadlines.
internal static class CardRouteClockPlanContract
{
    public static async Task WarmAndVerifyAsync(NpgsqlConnection admin, CancellationToken ct)
    {
        await using (var settings = new NpgsqlCommand("SELECT NOT prosecdef AND proconfig @> ARRAY['search_path=pg_catalog, public','enable_seqscan=off'] FROM pg_proc WHERE oid='public.maintain_entity_route_clocks()'::regprocedure;", admin))
            if (await settings.ExecuteScalarAsync(ct) is not true)
                throw new InvalidOperationException("Canonical route clock index preference or invoker boundary is absent.");
        string? previous;
        await using (var settings = new NpgsqlCommand("SELECT current_setting('enable_seqscan');", admin))
            previous = await settings.ExecuteScalarAsync(ct) as string;
        var warmTenant = Guid.NewGuid(); var warmActor = Guid.NewGuid(); var warmBoard = Guid.NewGuid(); var warmList = Guid.NewGuid();
        await using (var seed = new NpgsqlCommand("""
         INSERT INTO users(id,email,email_normalized,display_name,status,password_hash,created_at,updated_at)
          VALUES(@actor,'warm-'||@actor::text||'@example.test',upper('warm-'||@actor::text||'@example.test'),'Warm fixture','ACTIVE','fixture',now(),now());
         INSERT INTO organizations(id,name,owner_user_id,status,created_at,updated_at) VALUES(@tenant,'Warm fixture',@actor,'ACTIVE',now(),now());
         INSERT INTO organization_members(id,tenant_id,user_id,role,status) VALUES(gen_random_uuid(),@tenant,@actor,'OWNER','ACTIVE');
         INSERT INTO boards(id,tenant_id,name,created_at,updated_at) VALUES(@board,@tenant,'Warm fixture','2026-01-01','2026-01-01');
         INSERT INTO board_lists(id,tenant_id,board_id,name,rank,created_at,updated_at) VALUES(@list,@tenant,@board,'Warm fixture','500000000000000000000000000000','2026-01-01','2026-01-01');
         ANALYZE cards;
         """, admin)) {
            seed.Parameters.AddWithValue("actor",warmActor); seed.Parameters.AddWithValue("tenant",warmTenant);
            seed.Parameters.AddWithValue("board",warmBoard); seed.Parameters.AddWithValue("list",warmList);
            await seed.ExecuteNonQueryAsync(ct);
        }
        for (var i = 0; i < 8; i++) {
            await using var seed = new NpgsqlCommand("INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,created_at,updated_at) VALUES(@card,@tenant,@board,@list,'Warm fixture','500000000000000000000000000000','2026-01-01','2026-01-01');",admin);
            seed.Parameters.AddWithValue("card",Guid.NewGuid()); seed.Parameters.AddWithValue("tenant",warmTenant);
            seed.Parameters.AddWithValue("board",warmBoard); seed.Parameters.AddWithValue("list",warmList);
            await seed.ExecuteNonQueryAsync(ct);
        }
        await using (var settings = new NpgsqlCommand("SELECT current_setting('enable_seqscan');", admin))
            if (await settings.ExecuteScalarAsync(ct) as string != previous)
                throw new InvalidOperationException("Route clock planner preference escaped its owning function.");
        await using (var clocks = new NpgsqlCommand("SELECT count(*) FROM cards c JOIN card_routes r ON r.card_id=c.id AND r.tenant_id=c.tenant_id AND r.board_id=c.board_id AND r.list_id=c.list_id WHERE c.tenant_id=@tenant AND r.created_at=c.created_at AND r.updated_at=c.updated_at;", admin)) {
            clocks.Parameters.AddWithValue("tenant",warmTenant);
            if (await clocks.ExecuteScalarAsync(ct) is not long count || count != 8)
                throw new InvalidOperationException("Warm source projection lost canonical clocks or identity.");
        }
        Console.WriteLine("Eight small Card writes warmed canonical route lookup; function-local planning and exact clocks remain intact.");
    }
}
