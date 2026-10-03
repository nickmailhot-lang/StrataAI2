using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Npgsql;
using StrataAI.Application.Common;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Infrastructure.WorkManagement;

// Real restricted adapters and committed publication; only object storage/session
// proof are the already-declared fixture providers. Selection setup uses the
// restricted database boundary; Application commands have their own contract.
internal static class CardCoverDeliveryContract
{
    private sealed class Clock : IClock { public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow; }
    public static async Task RunAsync(NpgsqlConnection admin, IServiceProvider provider, PostgresConnectionFactory api,
        IAttachmentObjectStorage objects, Guid tenant, Guid card, Guid file, Guid actor, byte[] expected,
        Func<int> reads, Action<bool> corrupt, Action<Func<Task>?> afterRead, CancellationToken ct)
    {
        void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        var clock = new Clock();
        var admission = new CardCoverAdmissionService(provider.GetRequiredService<IWorkManagementStore>(),
            provider.GetRequiredService<IAttachmentMetadataStore>(), provider.GetRequiredService<ICardAttachmentCoverStore>(),
            provider.GetRequiredService<IOrganizationStore>(), provider.GetRequiredService<IWorkBoardAuthorization>(),
            provider.GetRequiredService<IWorkManagementUnitOfWork>(), clock);
        var service = new CardCoverReadService(admission, new PrivateAttachmentDownloadPreparer(objects));
        async Task<T> Scalar<T>(string sql)
        {
            await using var query = new NpgsqlCommand(sql, admin);
            query.Parameters.AddWithValue("tenant", tenant); query.Parameters.AddWithValue("card", card); query.Parameters.AddWithValue("file", file);
            return (T)(await query.ExecuteScalarAsync(ct))!;
        }
        async Task Select(bool selected)
        {
            await using var session = await api.OpenTenantSessionAsync(tenant, ct);
            await using var query = new NpgsqlCommand("UPDATE cards SET cover_attachment_id=" + (selected ? "@file" : "NULL")
                + ",version=version+1,updated_at=GREATEST(updated_at,statement_timestamp()) WHERE tenant_id=@tenant AND id=@card;", session.Connection, session.Transaction);
            query.Parameters.AddWithValue("tenant", tenant); query.Parameters.AddWithValue("card", card); query.Parameters.AddWithValue("file", file);
            Require(await query.ExecuteNonQueryAsync(ct) == 1, "Cover delivery fixture did not bind its restricted current selection.");
            await session.CommitAsync(ct);
        }
        Task Visibility(string visibility) => Scalar<int>("UPDATE boards SET visibility='" + visibility
            + "',version=version+1,updated_at=GREATEST(updated_at,statement_timestamp()) WHERE tenant_id=@tenant AND id=(SELECT board_id FROM cards WHERE id=@card AND tenant_id=@tenant) RETURNING 1;");
        async Task Projection(Guid? viewer, bool expectedCover)
        {
            var boardId = await Scalar<Guid>("SELECT board_id FROM cards WHERE tenant_id=@tenant AND id=@card;");
            var snapshot = (await provider.GetRequiredService<IWorkManagementService>().GetBoardAsync(boardId, viewer, ct)).Value;
            Require(snapshot is not null && snapshot.Lists.SelectMany(x => x.Cards).Single(x => x.Id == card).HasCover == expectedCover,
                "Authorized Board snapshot lost its current bounded cover display hint.");
            var json = JsonSerializer.Serialize(snapshot);
            Require(!json.Contains(file.ToString(), StringComparison.OrdinalIgnoreCase) && !json.Contains("cover_attachment_id", StringComparison.Ordinal),
                "Board cover projection exposed the private source identity.");
        }
        await Projection(actor, false);
        var before = reads();
        Require(!(await service.PrepareAsync(card, actor, ct)).Succeeded && !(await service.PrepareAsync(card, null, ct)).Succeeded && reads() == before,
            "An unselected cover reached private provider bytes.");
        await Select(true);
        try
        {
            before = reads();
            Require(!(await service.PrepareAsync(card, null, ct)).Succeeded && !(await service.PrepareAsync(card, Guid.NewGuid(), ct)).Succeeded
                && !(await service.PrepareAsync(Guid.NewGuid(), actor, ct)).Succeeded && reads() == before,
                "Private cover delivery widened current scope before provider admission.");
            var current = (await admission.AdmitAsync(card, actor, ct)).Value;
            Require(current is not null && current.ActorId == actor && current.File.Metadata.Id == file && current.Preview.Integrity.Reference.IsPreview,
                "Selected private cover lost server-owned source/publication binding.");
            Require(!(await service.PrepareAsync(card, actor, ct, current!.Card.Version - 1)).Succeeded && reads() == before,
                "Stale Card cover request reached provider bytes.");
            var privateContent = await service.PrepareAsync(card, actor, ct, current.Card.Version);
            Require(privateContent.Value is not null, "Current Internal cover refused its committed derivative.");
            await using (var content = privateContent.Value!)
            { using var output = new MemoryStream(); await content.Bytes.CopyToAsync(output, ct); Require(output.ToArray().SequenceEqual(expected), "Cover exposed original or different bytes."); }
            await Visibility("PUBLIC");
            await Projection(null, true);
            var publicGrant = (await admission.AdmitAsync(card, null, ct)).Value;
            Require(publicGrant is { ActorId: null } && publicGrant.File.Metadata.Id == file, "PUBLIC cover fabricated an actor or lost current selection.");
            before = reads();
            Require(!(await provider.GetRequiredService<CardAttachmentCoverService>().ReadAsync(card, Guid.NewGuid(), ct)).Succeeded
                && !(await provider.GetRequiredService<AttachmentDownloadAdmissionService>().AdmitAsync(card, file, Guid.NewGuid(), ct)).Succeeded && reads() == before,
                "Public cover projection widened private metadata/original admission.");
            var publicContent = await service.PrepareAsync(card, null, ct);
            Require(publicContent.Value is not null, "PUBLIC anonymous cover could not prepare its committed PNG.");
            await using (var content = publicContent.Value!)
            { using var output = new MemoryStream(); await content.Bytes.CopyToAsync(output, ct); Require(output.ToArray().SequenceEqual(expected), "Anonymous cover exposed unverified bytes."); }
            corrupt(true);
            try { Require((await service.PrepareAsync(card, null, ct)).ErrorCode == "work_storage_unavailable", "Corrupt public cover passed full-byte integrity staging."); }
            finally { corrupt(false); }
            clock.UtcNow = publicGrant!.ExpiresAt;
            Require(!(await admission.RevalidateAsync(publicGrant, null, ct)).Succeeded, "Expired cover retained its admission.");
            clock.UtcNow = DateTimeOffset.UtcNow;
            Require(!(await admission.RevalidateAsync(publicGrant, actor, ct)).Succeeded, "Cover admission switched anonymous/session actor identity.");
            afterRead(() => Select(false));
            Require(!(await service.PrepareAsync(card, null, ct)).Succeeded && !(await admission.RevalidateAsync(publicGrant, null, ct)).Succeeded,
                "Selection withdrawal during full staging retained a public cover grant.");
            before = reads();
            Require(!(await service.PrepareAsync(card, null, ct)).Succeeded && reads() == before, "Removed cover retried its old provider object.");
            await Select(true);
            afterRead(() => Visibility("PRIVATE"));
            Require(!(await service.PrepareAsync(card, null, ct)).Succeeded, "PUBLIC-to-private change during staging retained anonymous image bytes.");
            await Visibility("PUBLIC");
            foreach (var table in new[] { "cards", "board_lists", "boards" })
            {
                var where = table switch
                {
                    "cards" => "id=@card",
                    "board_lists" => "id=(SELECT list_id FROM cards WHERE id=@card AND tenant_id=@tenant)",
                    _ => "id=(SELECT board_id FROM cards WHERE id=@card AND tenant_id=@tenant)"
                };
                var granted = (await admission.AdmitAsync(card, null, ct)).Value;
                Require(granted is not null, "Active cover parent fixture lost admission.");
                await Scalar<int>("UPDATE " + table + " SET lifecycle_state='ARCHIVED',version=version+1,updated_at=GREATEST(updated_at,statement_timestamp()) WHERE tenant_id=@tenant AND " + where + " RETURNING 1;");
                try
                {
                    before = reads();
                    Require(!(await service.PrepareAsync(card, null, ct)).Succeeded && !(await admission.RevalidateAsync(granted!, null, ct)).Succeeded && reads() == before,
                        "Archived owning parent retained anonymous cover delivery.");
                    Require((await provider.GetRequiredService<CardAttachmentCoverService>().ListCandidatesAsync(card, actor, null, ct)).Value?.Items.Count == 0,
                        "Archived parent retained eligible cover choices.");
                }
                finally { await Scalar<int>("UPDATE " + table + " SET lifecycle_state='ACTIVE',version=version+1,updated_at=GREATEST(updated_at,statement_timestamp()) WHERE tenant_id=@tenant AND " + where + " RETURNING 1;"); }
            }
            await Select(false);
            await Projection(null, false);
            await Scalar<int>("UPDATE attachments SET lifecycle_state='ARCHIVED',archived_at=GREATEST(updated_at,statement_timestamp()),updated_at=GREATEST(updated_at,statement_timestamp()),version=version+1 WHERE tenant_id=@tenant AND id=@file RETURNING 1;");
            before = reads();
            Require(!(await service.PrepareAsync(card, null, ct)).Succeeded && reads() == before, "Archived/cleared source retained public cover bytes.");
            await Scalar<int>("UPDATE attachments SET lifecycle_state='ACTIVE',updated_at=GREATEST(updated_at,statement_timestamp()),version=version+1 WHERE tenant_id=@tenant AND id=@file RETURNING 1;");
            Require(!(await service.PrepareAsync(card, null, ct)).Succeeded && reads() == before, "Restoration silently reselected a public cover.");
        }
        finally
        {
            afterRead(null); corrupt(false); await Visibility("PRIVATE");
            if (await Scalar<bool>("SELECT cover_attachment_id IS NOT NULL FROM cards WHERE tenant_id=@tenant AND id=@card;")) await Select(false);
        }
        Console.WriteLine("Restricted selected-cover bytes: committed derivative only, current Internal/PUBLIC anonymous scope without fabricated actor, full staging outside DB, original/metadata separation, stale Card/expiry/actor refusal, corruption, staged selection/visibility withdrawal, parent archive and restoration without reselection passed.");
    }
}
