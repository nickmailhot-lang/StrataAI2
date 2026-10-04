using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StrataAI.Application.WorkManagement;

internal static class CommentMentionSnapshotStoreContract
{
    private static void Require(bool condition, string invariant) { if (!condition) throw new InvalidOperationException(invariant); }
    public static async Task RunAsync(NpgsqlConnection admin, IServiceProvider provider, Guid tenant, Guid foreignTenant,
        Guid card, Guid foreignCard, Guid author, Guid foreignAuthor, CancellationToken ct)
    {
        var snapshots = provider.GetRequiredService<ICommentMentionSnapshotStore>(); var comments = provider.GetRequiredService<ICardCommentStore>();
        var unit = provider.GetRequiredService<IWorkManagementUnitOfWork>(); var id = Guid.NewGuid(); var rolledBackId = Guid.NewGuid();
        async Task<T> Scope<T>(Guid organization, Func<Task<T>> callback)
        {
            var result = await unit.ExecuteReadAsync(organization, null, "fixture_denied", () => Task.FromResult(true),
                async () => WorkOperation<T>.Success(await callback()), ct);
            Require(result.Succeeded, "Snapshot owning fixture operation failed."); return result.Value!;
        }
        try
        {
            try { await snapshots.FindSnapshotAsync(tenant, card, id, 1, ct); throw new InvalidOperationException("Unowned snapshot read accepted."); }
            catch (InvalidOperationException e) when (e.Message == "Mention snapshots require the owning Work transaction.") { }
            var row = await Scope(tenant, () => comments.CreateAsync(id, tenant, card, author, "Private snapshot fixture", DateTimeOffset.UtcNow, ct));
            var snapshot = new CommentMentionSnapshot(tenant, card, id, 1, row.UpdatedAt, [author]);
            var foreign = await unit.ExecuteReadAsync(tenant, null, "fixture_denied", () => Task.FromResult(true), async () =>
            {
                await snapshots.AppendSnapshotAsync(new(tenant, card, id, 1, row.UpdatedAt, [author, foreignAuthor]), ct);
                return WorkOperation<bool>.Success(true);
            }, ct);
            Require(foreign.ErrorCode == "work_storage_unavailable", "Foreign membership snapshot was persisted.");
            Require(await Scope(tenant, () => snapshots.FindSnapshotAsync(tenant, card, id, 1, ct)) is null, "Foreign recipient failure retained snapshot header.");
            var refused = await unit.ExecuteReadAsync(tenant, null, "fixture_denied", () => Task.FromResult(true), async () =>
            {
                var created = await comments.CreateAsync(rolledBackId, tenant, card, author, "Rollback private body", DateTimeOffset.UtcNow, ct);
                await snapshots.AppendSnapshotAsync(new(tenant, card, rolledBackId, 1, created.UpdatedAt, [author]), ct);
                return WorkOperation<bool>.Failure("fixture_refused");
            }, ct);
            Require(refused.ErrorCode == "fixture_refused"
                && await Scope(tenant, () => comments.FindAsync(tenant, card, rolledBackId, ct)) is null
                && await Scope(tenant, () => snapshots.FindSnapshotAsync(tenant, card, rolledBackId, 1, ct)) is null,
                "Refused owning command retained comment or snapshot effects.");
            await Scope(tenant, async () => { await snapshots.AppendSnapshotAsync(snapshot, ct); await snapshots.AppendSnapshotAsync(snapshot, ct); return true; });
            var retained = await Scope(tenant, () => snapshots.FindSnapshotAsync(tenant, card, id, 1, ct));
            Require(retained is not null && retained.SameAs(snapshot), "Exact recipient snapshot did not survive duplicate append.");
            await Scope(tenant, async () =>
            {
                var edited = await comments.EditAsync(tenant, card, id, author, 1, "Edited", row.UpdatedAt.AddSeconds(1), ct);
                Require(edited is not null, "Snapshot fixture comment did not advance.");
                await snapshots.AppendSnapshotAsync(new(tenant, card, id, 2, edited!.UpdatedAt, []), ct);
                await snapshots.AppendSnapshotAsync(snapshot, ct); return true;
            });
            Require((await Scope(tenant, () => snapshots.FindSnapshotAsync(tenant, card, id, 2, ct))) is { Recipients.Count: 0 }
                && (await Scope(tenant, () => snapshots.FindSnapshotAsync(tenant, card, id, 1, ct)))!.SameAs(snapshot),
                "Empty current revision replaced former stable recipient identity.");
            Require(await Scope(tenant, () => snapshots.FindSnapshotAsync(tenant, foreignCard, id, 1, ct)) is null
                && await Scope(foreignTenant, () => snapshots.FindSnapshotAsync(foreignTenant, card, id, 1, ct)) is null,
                "Snapshot Card or tenant metadata widened.");
            try
            {
                await Scope(tenant, async () => { await snapshots.AppendSnapshotAsync(new(tenant, card, id, 1, row.UpdatedAt, []), ct); return true; });
                throw new InvalidOperationException("Different recipient snapshot replaced original revision.");
            }
            catch (InvalidOperationException e) when (e.Message == "Mention snapshot revision was reused.") { }
        }
        finally
        {
            await using var cleanupTransaction = await admin.BeginTransactionAsync(ct);
            await using var cleanup = new NpgsqlCommand("""
                DELETE FROM comment_mention_recipients WHERE tenant_id=@tenant AND comment_id=ANY(@ids);
                DELETE FROM comment_mention_snapshots WHERE tenant_id=@tenant AND comment_id=ANY(@ids);
                DELETE FROM card_comments WHERE tenant_id=@tenant AND id=ANY(@ids);
                """, admin, cleanupTransaction);
            cleanup.Parameters.AddWithValue("tenant", tenant); cleanup.Parameters.AddWithValue("ids", new[] { id, rolledBackId });
            await cleanup.ExecuteNonQueryAsync(ct);
            await cleanupTransaction.CommitAsync(ct);
        }
        Console.WriteLine("Restricted mention snapshots: owning transaction, exact immutable retries, foreign membership rollback, comment/snapshot rollback, retained prior IDs, empty current snapshot and tenant/Card isolation passed.");
    }
}
