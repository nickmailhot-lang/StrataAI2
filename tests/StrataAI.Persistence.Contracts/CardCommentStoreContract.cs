using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StrataAI.Application.WorkManagement;

// Actual restricted API adapter/transactions. Current session/Board admission
// is not supplied by this metadata fixture and remains Application work.
internal static class CardCommentStoreContract
{
    public static async Task RunAsync(NpgsqlConnection admin, IServiceProvider provider,
        Guid tenant, Guid foreignTenant, Guid card, Guid author, CancellationToken ct)
    {
        void Require(bool condition, string invariant) { if (!condition) throw new InvalidOperationException(invariant); }
        var store = provider.GetRequiredService<ICardCommentStore>(); var unit = provider.GetRequiredService<IWorkManagementUnitOfWork>();
        async Task<T> Scoped<T>(Func<Task<T>> operation, Guid? scope = null)
        {
            var result = await unit.ExecuteReadAsync(scope ?? tenant, null, "fixture_scope_unavailable", () => Task.FromResult(true),
                async () => WorkOperation<T>.Success(await operation()), ct);
            Require(result.Succeeded, "Comment metadata owning transaction failed."); return result.Value!;
        }
        try { await store.FindAsync(tenant, card, Guid.NewGuid(), ct); throw new InvalidOperationException("Comment metadata accepted an unowned scope."); }
        catch (InvalidOperationException error) when (error.Message == "Comment storage requires the owning scope.") { }
        var ids = new List<Guid>(); var now = DateTimeOffset.UtcNow;
        try
        {
            var id = Guid.NewGuid(); ids.Add(id);
            var created = await Scoped(() => store.CreateAsync(id, tenant, card, author, "  Comment\r\n🙂  ", now, ct));
            Require(created is { Version: 1, Content: "Comment\n🙂", EditedAt: null, DeletedAt: null }
                && created.AuthorId == author && created.CardId == card && created.OrganizationId == tenant,
                "Comment adapter lost normalized body or canonical identity.");
            Require(await Scoped(() => store.FindAsync(foreignTenant, card, id, ct), foreignTenant) is null,
                "Comment adapter disclosed another tenant's row.");
            Require(await Scoped(() => store.EditAsync(tenant, card, id, Guid.NewGuid(), 1, "Foreign edit", now.AddSeconds(1), ct)) is null,
                "Comment adapter allowed a different author.");
            var edited = await Scoped(() => store.EditAsync(tenant, card, id, author, 1, "Edited", now.AddSeconds(1), ct));
            Require(edited is { Version: 2, Content: "Edited" } && edited.EditedAt == edited.UpdatedAt && edited.CreatedAt == created.CreatedAt,
                "Comment adapter lost one-revision edit history.");
            Require(await Scoped(() => store.EditAsync(tenant, card, id, author, 1, "Stale", now.AddSeconds(2), ct)) is null
                && await Scoped(() => store.EditAsync(tenant, card, id, author, 2, " Edited ", now.AddSeconds(2), ct)) is null
                && await Scoped(() => store.DeleteAsync(tenant, card, id, author, 1, now.AddSeconds(2), ct)) is null,
                "Comment adapter repeated equal content or accepted stale edit/deletion.");
            var deleted = await Scoped(() => store.DeleteAsync(tenant, card, id, author, 2, now.AddSeconds(2), ct));
            Require(deleted is { Version: 3, Content: null } && deleted.DeletedAt == deleted.UpdatedAt
                && deleted.DeletedBy == author && deleted.EditedAt == edited!.EditedAt,
                "Comment adapter lost redaction or retained attribution.");
            Require(await Scoped(() => store.EditAsync(tenant, card, id, author, 3, "Revive", now.AddSeconds(3), ct)) is null
                && await Scoped(() => store.DeleteAsync(tenant, card, id, author, 3, now.AddSeconds(3), ct)) is null,
                "Comment adapter revived or revised a tombstone.");
            var rolledBack = Guid.NewGuid(); ids.Add(rolledBack);
            var refusal = await unit.ExecuteReadAsync<CardCommentRecord>(tenant, null, "fixture_scope_unavailable", () => Task.FromResult(true), async () =>
            {
                await store.CreateAsync(rolledBack, tenant, card, author, "Must roll back", now, ct);
                return WorkOperation<CardCommentRecord>.Failure("fixture_refused");
            }, ct);
            Require(refusal.ErrorCode == "fixture_refused" && await Scoped(() => store.FindAsync(tenant, card, rolledBack, ct)) is null,
                "Comment insert survived its owning transaction refusal.");
            var tied = now.AddMinutes(1);
            await Scoped(async () =>
            {
                for (var index = 0; index < 63; index++)
                { var next = Guid.NewGuid(); ids.Add(next); await store.CreateAsync(next, tenant, card, author, "Paged comment", tied, ct); }
                return true;
            });
            var first = await Scoped(() => store.ListAsync(tenant, card, null, null, ct));
            Require(first.Count == 51, "Comment store did not retain the bounded lookahead page.");
            var boundary = first[49]; var tail = await Scoped(() => store.ListAsync(tenant, card, boundary.CreatedAt, boundary.Id, ct));
            var delivered = first.Take(50).Concat(tail).ToArray();
            Require(tail.Count == 14 && delivered.Select(x => x.Id).Distinct().Count() == 64 && delivered.Any(x => x.Id == id && x.Content is null),
                "Comment seek lost/repeated tied rows or omitted its redacted tombstone.");
            Require(await Scoped(() => store.FindAsync(tenant, card, id, ct)) == deleted,
                "Comment paging changed canonical history.");
        }
        finally
        {
            // Admin removes only disposable metadata; the runtime has no hard
            // DELETE and this is not production retention/purge behavior.
            await using var cleanup = new NpgsqlCommand("DELETE FROM card_comments WHERE tenant_id=@tenant AND card_id=@card AND id=ANY(@ids);", admin);
            cleanup.Parameters.AddWithValue("tenant", tenant); cleanup.Parameters.AddWithValue("card", card); cleanup.Parameters.AddWithValue("ids", ids.ToArray());
            await cleanup.ExecuteNonQueryAsync(ct);
        }
        Console.WriteLine("Restricted Comment adapter: owning scope, normalized text, author/revision CAS, rollback, redaction, retained attribution and bounded tied seek paging passed.");
    }
}
