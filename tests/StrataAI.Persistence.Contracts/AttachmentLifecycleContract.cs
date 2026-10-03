using Npgsql;
using StrataAI.Infrastructure.Persistence;

internal static class AttachmentLifecycleContract
{
    public static async Task RunAsync(NpgsqlConnection admin, PostgresConnectionFactory api,
        Guid tenant, Guid card, CancellationToken ct)
    {
        var file = Guid.NewGuid();
        Guid actor;
        await using (var lookup = new NpgsqlCommand("SELECT uploader_id FROM public.attachments WHERE tenant_id=@tenant AND card_id=@card LIMIT 1;", admin))
        {
            lookup.Parameters.AddWithValue("tenant", tenant); lookup.Parameters.AddWithValue("card", card);
            actor = (Guid)(await lookup.ExecuteScalarAsync(ct) ?? throw new InvalidOperationException("Lifecycle actor fixture is unavailable."));
        }
        async Task<object?> Run(string sql, Guid? scope = null)
        {
            await using var session = await api.OpenTenantSessionAsync(scope ?? tenant, ct);
            await using var query = new NpgsqlCommand(sql, session.Connection, session.Transaction);
            query.Parameters.AddWithValue("tenant", tenant); query.Parameters.AddWithValue("card", card);
            query.Parameters.AddWithValue("file", file); query.Parameters.AddWithValue("actor", actor);
            var value = await query.ExecuteScalarAsync(ct); await session.CommitAsync(ct); return value;
        }
        async Task Refuse(string assignments)
        {
            try
            {
                await Run("UPDATE public.attachments SET " + assignments + " WHERE tenant_id=@tenant AND id=@file RETURNING 1;");
                throw new InvalidOperationException("Invalid lifecycle mutation was admitted.");
            }
            catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.CheckViolation) { }
        }
        const string where = " WHERE tenant_id=@tenant AND id=@file";
        Require(await Run("""
            INSERT INTO public.attachments(id,tenant_id,card_id,uploader_id,kind,display_name,url,scan_status,created_at,updated_at)
            VALUES(@file,@tenant,@card,@actor,'URL','Lifecycle fixture','https://example.test/','NOT_APPLICABLE',statement_timestamp(),statement_timestamp())
            RETURNING lifecycle_state='ACTIVE' AND archived_at IS NULL AND deleted_by IS NULL AND deleted_at IS NULL AND version=1;
            """) is true, "New attachment lifecycle defaults were invalid.");
        Require(await Run("SELECT count(*) FROM public.attachments" + where, Guid.NewGuid()) is 0L, "Foreign tenant disclosed lifecycle metadata.");
        await Refuse("lifecycle_state='DELETED',deleted_by=@actor,deleted_at=statement_timestamp(),updated_at=statement_timestamp(),version=version+1");
        await Refuse("archived_at=statement_timestamp()");
        await Refuse("card_id=gen_random_uuid()");
        await Run("UPDATE public.attachments SET lifecycle_state='ARCHIVED',archived_at=statement_timestamp(),updated_at=statement_timestamp(),version=version+1" + where);
        Require(await Run("SELECT lifecycle_state='ARCHIVED' AND archived_at=updated_at AND version=2 AND deleted_at IS NULL AND deleted_by IS NULL FROM public.attachments" + where) is true,
            "Archive lifecycle/history was not persisted.");
        await Refuse("lifecycle_state='ACTIVE',archived_at=NULL,version=version+1");
        await Refuse("lifecycle_state='ACTIVE'");
        await Refuse("lifecycle_state='DELETED',deleted_at=statement_timestamp(),updated_at=statement_timestamp(),version=version+1");
        await Run("UPDATE public.attachments SET lifecycle_state='ACTIVE',updated_at=statement_timestamp(),version=version+1" + where);
        Require(await Run("SELECT lifecycle_state='ACTIVE' AND archived_at IS NOT NULL AND version=3 AND deleted_at IS NULL FROM public.attachments" + where) is true,
            "Restoration lost archive history or did not advance revision.");
        await Run("UPDATE public.attachments SET lifecycle_state='ARCHIVED',archived_at=statement_timestamp(),updated_at=statement_timestamp(),version=version+1" + where);
        await Run("UPDATE public.attachments SET lifecycle_state='DELETED',deleted_by=@actor,deleted_at=statement_timestamp(),updated_at=statement_timestamp(),version=version+1" + where);
        Require(await Run("SELECT lifecycle_state='DELETED' AND archived_at IS NOT NULL AND deleted_at=updated_at AND deleted_by=@actor AND version=5 FROM public.attachments" + where) is true,
            "Deletion did not retain archive/actor evidence.");
        await Refuse("lifecycle_state='ACTIVE',deleted_at=NULL,deleted_by=NULL,version=version+1");
        await Refuse("display_name='Reopened tombstone',version=version+1");
        Require(await Run("SELECT lifecycle_state='DELETED' AND version=5 AND display_name='Lifecycle fixture' FROM public.attachments" + where) is true,
            "Rejected restoration modified a tombstone.");
        Console.WriteLine("Restricted attachment lifecycle: tenant RLS, archive/history/restore/delete revisions, active-delete refusal, missing actor/history refusal and immutable tombstone passed.");
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
