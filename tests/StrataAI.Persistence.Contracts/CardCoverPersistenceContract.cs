using Npgsql;
using StrataAI.Infrastructure.Persistence;

internal static class CardCoverPersistenceContract
{
    internal static async Task RunAsync(NpgsqlConnection admin, PostgresConnectionFactory api,
        Guid tenant, Guid card, Guid file, CancellationToken ct)
    {
        void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        async Task<T> Scalar<T>(string sql)
        {
            await using var query = new NpgsqlCommand(sql, admin);
            query.Parameters.AddWithValue("tenant", tenant); query.Parameters.AddWithValue("card", card); query.Parameters.AddWithValue("file", file);
            return (T)(await query.ExecuteScalarAsync(ct))!;
        }
        async Task<object?> Run(string sql, Guid? scope = null)
        {
            await using var session = await api.OpenTenantSessionAsync(scope ?? tenant, ct);
            await using var query = new NpgsqlCommand(sql, session.Connection, session.Transaction);
            query.Parameters.AddWithValue("tenant", tenant); query.Parameters.AddWithValue("card", card); query.Parameters.AddWithValue("file", file);
            var result = await query.ExecuteScalarAsync(ct); await session.CommitAsync(ct); return result;
        }
        async Task Refuse(string sql)
        {
            try { await Run(sql); throw new InvalidOperationException("Invalid persisted cover mutation was admitted."); }
            catch (PostgresException error) when (error.SqlState is PostgresErrorCodes.CheckViolation or PostgresErrorCodes.ForeignKeyViolation) { }
        }
        const string cardWhere = " WHERE id=@card AND tenant_id=@tenant";
        const string fileWhere = " WHERE id=@file AND tenant_id=@tenant";
        var initialCardVersion = await Scalar<long>("SELECT version FROM public.cards" + cardWhere);
        var initialFileVersion = await Scalar<long>("SELECT version FROM public.attachments" + fileWhere);
        Require(await Scalar<bool>("SELECT cover_attachment_id IS NULL FROM public.cards" + cardWhere), "Existing Card acquired an invented cover.");
        var link = (Guid)(await Run("""
            INSERT INTO public.attachments(id,tenant_id,card_id,uploader_id,kind,display_name,url,scan_status,created_at,updated_at)
            SELECT gen_random_uuid(),tenant_id,card_id,uploader_id,'URL','Cover-ineligible URL','https://example.test/cover','NOT_APPLICABLE',statement_timestamp(),statement_timestamp()
            FROM public.attachments WHERE id=@file AND tenant_id=@tenant RETURNING id;
            """) ?? throw new InvalidOperationException("URL cover refusal fixture is unavailable."));
        await Refuse($"UPDATE public.cards SET cover_attachment_id='{link:D}'::uuid,version=version+1,updated_at=GREATEST(updated_at,statement_timestamp())" + cardWhere);
        var sibling = (Guid)(await Run("""
            INSERT INTO public.cards(id,tenant_id,board_id,list_id,title,rank,created_at,updated_at)
            SELECT gen_random_uuid(),tenant_id,board_id,list_id,'Cover ownership sibling',rank||'-cover',statement_timestamp(),statement_timestamp()
            FROM public.cards WHERE id=@card AND tenant_id=@tenant RETURNING id;
            """) ?? throw new InvalidOperationException("Sibling cover refusal fixture is unavailable."));
        await Refuse($"UPDATE public.cards SET cover_attachment_id=@file,version=version+1,updated_at=GREATEST(updated_at,statement_timestamp()) WHERE id='{sibling:D}'::uuid AND tenant_id=@tenant");
        await Refuse("UPDATE public.cards SET cover_attachment_id=gen_random_uuid(),version=version+1,updated_at=GREATEST(updated_at,statement_timestamp())" + cardWhere);
        await Refuse("UPDATE public.cards SET cover_attachment_id=@file" + cardWhere);
        Require(await Scalar<long>("SELECT version FROM public.cards" + cardWhere) == initialCardVersion, "Rejected selection changed Card revision.");
        Require(await Run("UPDATE public.cards SET cover_attachment_id=@file,version=version+1,updated_at=GREATEST(updated_at,statement_timestamp())" + cardWhere + " RETURNING cover_attachment_id=@file") is true,
            "Restricted API could not select the same-Card committed image preview.");
        Require(await Run("SELECT count(*) FROM public.cards" + cardWhere, Guid.NewGuid()) is 0L, "Foreign tenant disclosed a selected cover.");
        Require(await Run("UPDATE public.cards SET cover_attachment_id=NULL,version=version+1" + cardWhere + " RETURNING 1", Guid.NewGuid()) is null,
            "Foreign tenant changed cover selection.");
        await Refuse("UPDATE public.cards SET cover_attachment_id=NULL" + cardWhere);
        await Refuse("UPDATE public.attachments SET lifecycle_state='ARCHIVED',archived_at=GREATEST(updated_at,statement_timestamp()),updated_at=GREATEST(updated_at,statement_timestamp()),version=version+1" + fileWhere);
        Require(await Scalar<long>("SELECT version FROM public.attachments" + fileWhere) == initialFileVersion,
            "Deferred cover fence retained a refused source archive.");
        Require(await Scalar<bool>("SELECT cover_attachment_id=@file FROM public.cards" + cardWhere), "Refused source archive lost cover selection.");
        // The schema admits either operation order only as one transaction.
        await Run("UPDATE public.attachments SET lifecycle_state='ARCHIVED',archived_at=GREATEST(updated_at,statement_timestamp()),updated_at=GREATEST(updated_at,statement_timestamp()),version=version+1" + fileWhere + ";"
            + "UPDATE public.cards SET cover_attachment_id=NULL,version=version+1,updated_at=GREATEST(updated_at,statement_timestamp())" + cardWhere);
        Require(await Scalar<bool>("SELECT cover_attachment_id IS NULL FROM public.cards" + cardWhere)
            && await Scalar<long>("SELECT version FROM public.cards" + cardWhere) == initialCardVersion + 2,
            "Atomic archive/clear did not retain exactly one further Card revision.");
        Require(await Scalar<bool>("SELECT lifecycle_state='ARCHIVED' AND version=" + (initialFileVersion + 1) + " FROM public.attachments" + fileWhere),
            "Atomic archive/clear lost source lifecycle or revision.");
        await Refuse("UPDATE public.cards SET cover_attachment_id=@file,version=version+1,updated_at=GREATEST(updated_at,statement_timestamp())" + cardWhere);
        await Run("UPDATE public.attachments SET lifecycle_state='ACTIVE',updated_at=GREATEST(updated_at,statement_timestamp()),version=version+1" + fileWhere);
        Require(await Scalar<bool>("SELECT cover_attachment_id IS NULL FROM public.cards" + cardWhere), "Restoration silently reselected the old cover.");
        Console.WriteLine("Restricted Card covers: committed same-Card image proof, tenant RLS, revision/clear/source withdrawal guards, deferred full rollback, atomic clear/archive and restoration without reselection passed.");
    }
}
