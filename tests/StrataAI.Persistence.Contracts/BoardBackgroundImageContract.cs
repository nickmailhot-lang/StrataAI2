using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.WorkManagement;

// Real restricted persistence, Worker publication and private-byte verification.
// Actor proof and provider bytes retain the enclosing explicit fixture adapters.
internal static class BoardBackgroundImageContract
{
    private sealed class Context : IWorkCommandContext { public Guid? IdempotencyKey { get; set; } = Guid.NewGuid(); }
    public static async Task RunAsync(NpgsqlConnection admin, IServiceProvider provider, IAttachmentObjectStorage objects,
        Guid tenant, Guid card, Guid file, Guid actor, byte[] expected, Func<int> reads, Action<bool> corrupt,
        Action<Func<Task>?> afterRead, CancellationToken ct)
    {
        void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        var work = provider.GetRequiredService<IWorkManagementStore>(); var unit = provider.GetRequiredService<IWorkManagementUnitOfWork>();
        var organizations = provider.GetRequiredService<IOrganizationStore>(); var boards = provider.GetRequiredService<IWorkBoardAuthorization>();
        var actors = provider.GetRequiredService<ICommandActorAuthorization>(); var clock = new SystemClock(); var context = new Context();
        var sourceAdmission = provider.GetRequiredService<AttachmentDownloadAdmissionService>(); var preparer = new PrivateAttachmentDownloadPreparer(objects);
        var select = new BoardBackgroundImageSelectionService(work, organizations, boards, unit, context, actors,
            new(sourceAdmission, preparer), sourceAdmission, clock, provider.GetRequiredService<IWorkEventStore>());
        var admission = new BoardBackgroundImageAdmissionService(work, organizations, boards, unit, actors, clock);
        var read = new BoardBackgroundImageReadService(admission, preparer);
        async Task<T> Scalar<T>(string sql)
        {
            await using var query = new NpgsqlCommand(sql, admin);
            query.Parameters.AddWithValue("tenant", tenant); query.Parameters.AddWithValue("card", card); query.Parameters.AddWithValue("file", file);
            return (T)(await query.ExecuteScalarAsync(ct))!;
        }
        var board = await Scalar<Guid>("SELECT board_id FROM cards WHERE tenant_id=@tenant AND id=@card;");
        async Task<T> BoardScalar<T>(string sql)
        {
            await using var query = new NpgsqlCommand(sql, admin); query.Parameters.AddWithValue("tenant", tenant); query.Parameters.AddWithValue("board", board);
            return (T)(await query.ExecuteScalarAsync(ct))!;
        }
        Task<string> Snapshot() => BoardScalar<string>("""
            SELECT jsonb_build_object('board',to_jsonb(b),'images',(SELECT count(*) FROM board_background_images WHERE tenant_id=@tenant),
             'events',(SELECT count(*) FROM work_events WHERE tenant_id=@tenant),'audit',(SELECT count(*) FROM audit_events WHERE tenant_id=@tenant),
             'jobs',(SELECT count(*) FROM background_jobs WHERE tenant_id=@tenant),'receipts',(SELECT count(*) FROM work_command_replays WHERE tenant_id=@tenant))::text
            FROM boards b WHERE tenant_id=@tenant AND id=@board;
            """);
        var revision = await BoardScalar<long>("SELECT version FROM boards WHERE tenant_id=@tenant AND id=@board;");
        var fileVersion = await Scalar<long>("SELECT version FROM attachments WHERE tenant_id=@tenant AND id=@file;");
        var input = new SelectBoardBackgroundImageInput(card, file, fileVersion, revision);
        var before = await Snapshot(); var initialReads = reads();
        Require(!(await select.SelectAsync(board, Guid.NewGuid(), input, "image-hidden-actor", ct)).Succeeded && reads() == initialReads,
            "Unadmitted image selection reached provider bytes.");
        await using (var revoke = new NpgsqlCommand("REVOKE INSERT ON audit_events FROM strataai_api_runtime;", admin)) await revoke.ExecuteNonQueryAsync(ct);
        try
        {
            Require((await select.SelectAsync(board, actor, input, "image-atomic-refusal", ct)).ErrorCode == "work_storage_unavailable",
                "Board image did not preserve its storage refusal boundary.");
            Require(await Snapshot() == before, "Late image audit refusal retained ownership, Board selection, effects or receipt.");
        }
        finally { await using var grant = new NpgsqlCommand("GRANT INSERT ON audit_events TO strataai_api_runtime;", admin); await grant.ExecuteNonQueryAsync(ct); }
        var selected = await select.SelectAsync(board, actor, input, "image-atomic-retry", ct);
        Require(selected.Value is { BackgroundType: "IMAGE" } && selected.Value.Version == revision + 1, "Board image retry lost its original version/ownership contract.");
        var original = selected.Value!; before = await Snapshot();
        Require((await select.SelectAsync(board, actor, input, "image-ack-replay", ct)).Value == original && await Snapshot() == before,
            "Board image replay created another owner or changed current state.");
        Require((await select.SelectAsync(board, actor, input with { AttachmentVersion = fileVersion + 1 }, "image-key-reuse", ct)).ErrorCode == "idempotency_key_reused",
            "Board image changed intent reused its receipt.");
        async Task Bytes(Guid id, Guid? viewer)
        {
            var result = await read.PrepareAsync(id, viewer, ct); Require(result.Value is not null, "Owned Board image refused its committed PNG.");
            await using var content = result.Value!; using var output = new MemoryStream(); await content.Bytes.CopyToAsync(output, ct);
            Require(output.ToArray().SequenceEqual(expected), "Board image delivered original or different bytes.");
        }
        initialReads = reads();
        Require(!(await read.PrepareAsync(board, null, ct)).Succeeded && !(await read.PrepareAsync(board, Guid.NewGuid(), ct)).Succeeded
            && !(await read.PrepareAsync(board, actor, ct, revision)).Succeeded && reads() == initialReads,
            "Private or stale Board image reached its object before admission.");
        await Bytes(board, actor);
        var imageId = Guid.Parse(original.BackgroundValue!);
        var foreignTenant = Guid.NewGuid();
        var foreign = await unit.ExecuteReadAsync(foreignTenant, null, "fixture_scope", () => Task.FromResult(true), async () =>
            WorkOperation<BoardBackgroundImage?>.Success(await work.FindBoardBackgroundImageAsync(foreignTenant, board, imageId, ct)), ct);
        Require(foreign.Succeeded && foreign.Value is null, "A different owning tenant scope read Board image metadata.");
        await BoardScalar<int>("UPDATE boards SET visibility='PUBLIC',version=version+1,updated_at=GREATEST(updated_at,statement_timestamp()) WHERE tenant_id=@tenant AND id=@board RETURNING 1;");
        revision = await BoardScalar<long>("SELECT version FROM boards WHERE tenant_id=@tenant AND id=@board;"); context.IdempotencyKey = Guid.NewGuid(); before = await Snapshot();
        var publicInput = input with { BoardVersion = revision };
        Require((await select.SelectAsync(board, actor, publicInput, "image-public-unconfirmed", ct)).ErrorCode == "background_public_confirmation_required"
            && await Snapshot() == before, "Public Board image selection committed without visibility consent.");
        var confirmed = await select.SelectAsync(board, actor, publicInput with { PublicVisibilityConfirmed = true }, "image-public-confirmed", ct);
        Require(confirmed.Value is not null, "Confirmed public Board image refused its publication."); await Bytes(board, null);
        var copy = await provider.GetRequiredService<IWorkManagementService>().CopyBoardAsync(board, actor, "Owned image copy", confirmed.Value!.Version, "image-board-copy", ct);
        Require(copy.Value is { BackgroundType: "IMAGE", Version: 1, Visibility: BoardVisibility.Private }
            && copy.Value.BackgroundValue != confirmed.Value.BackgroundValue, "Board copy reused source image identity or admission.");
        await Bytes(copy.Value!.Id, actor);
        await Scalar<int>("UPDATE attachments SET lifecycle_state='ARCHIVED',archived_at=GREATEST(updated_at,statement_timestamp()),updated_at=GREATEST(updated_at,statement_timestamp()),version=version+1 WHERE tenant_id=@tenant AND id=@file RETURNING 1;");
        try { await Bytes(board, null); await Bytes(copy.Value.Id, actor); }
        finally { await Scalar<int>("UPDATE attachments SET lifecycle_state='ACTIVE',updated_at=GREATEST(updated_at,statement_timestamp()),version=version+1 WHERE tenant_id=@tenant AND id=@file RETURNING 1;"); }
        corrupt(true);
        try { Require((await read.PrepareAsync(board, null, ct)).ErrorCode == "work_storage_unavailable", "Corrupt Board image passed full-byte staging."); }
        finally { corrupt(false); }
        afterRead(async () => { await BoardScalar<int>("UPDATE boards SET visibility='PRIVATE',version=version+1,updated_at=GREATEST(updated_at,statement_timestamp()) WHERE tenant_id=@tenant AND id=@board RETURNING 1;"); });
        try { Require(!(await read.PrepareAsync(board, null, ct)).Succeeded, "Visibility withdrawal during staging retained anonymous image bytes."); }
        finally { afterRead(null); }
        // A copied owner must re-admit its own Board after provider IO. Source
        // ownership and source Card admission cannot authorize this delivery.
        var sourceBeforeWithdrawal = await Snapshot();
        async Task ChangeCopy(string change)
        {
            await using var query = new NpgsqlCommand("UPDATE boards SET " + change +
                ",version=version+1,updated_at=GREATEST(updated_at,statement_timestamp()) WHERE tenant_id=@tenant AND id=@copy;", admin);
            query.Parameters.AddWithValue("tenant", tenant); query.Parameters.AddWithValue("copy", copy.Value.Id);
            Require(await query.ExecuteNonQueryAsync(ct) == 1, "Copied Board withdrawal fixture lost its owner.");
        }
        var copiedGrant = (await admission.AdmitAsync(copy.Value.Id, actor, ct)).Value;
        Require(copiedGrant is not null, "Copied Board lost its private image grant.");
        afterRead(() => ChangeCopy("lifecycle_state='ARCHIVED'"));
        try
        {
            Require(!(await read.PrepareAsync(copy.Value.Id, actor, ct)).Succeeded,
                "Copied Board archive during staging retained private image bytes.");
            initialReads = reads();
            Require(!(await read.PrepareAsync(copy.Value.Id, actor, ct)).Succeeded
                && !(await admission.RevalidateAsync(copiedGrant!, actor, ct)).Succeeded && reads() == initialReads,
                "Archived copied Board reused a grant or reached provider bytes.");
        }
        finally { afterRead(null); await ChangeCopy("lifecycle_state='ACTIVE'"); }
        Require(!(await admission.RevalidateAsync(copiedGrant!, actor, ct)).Succeeded,
            "Restored copied Board revived a pre-archive image grant.");
        await Bytes(copy.Value.Id, actor);
        var copiedImage = Guid.Parse(copy.Value.BackgroundValue!);
        afterRead(() => ChangeCopy("background_type='COLOR',background_value=NULL"));
        try
        {
            Require(!(await read.PrepareAsync(copy.Value.Id, actor, ct)).Succeeded,
                "Copied image selection withdrawal during staging retained private bytes.");
            initialReads = reads();
            Require(!(await read.PrepareAsync(copy.Value.Id, actor, ct)).Succeeded && reads() == initialReads,
                "Cleared copied Board reached its old provider object.");
        }
        finally { afterRead(null); await ChangeCopy("background_type='IMAGE',background_value='" + copiedImage.ToString("D") + "'"); }
        Require(await Snapshot() == sourceBeforeWithdrawal, "Copied Board withdrawal changed source Board ownership or effects.");
        await Bytes(board, actor); await Bytes(copy.Value.Id, actor);
        // PRD-04-TC-05/10: private copied ownership cannot preserve disclosure
        // after its actor loses Organization membership or its parent closes.
        // Changes use the trusted fixture connection; the reads and re-admission
        // execute the genuine restricted adapter and current scope transaction.
        async Task ChangeOrganization(string status)
        {
            await using var query = new NpgsqlCommand("UPDATE organizations SET status=@status WHERE id=@tenant;", admin);
            query.Parameters.AddWithValue("tenant", tenant); query.Parameters.AddWithValue("status", status);
            Require(await query.ExecuteNonQueryAsync(ct) == 1, "Image parent withdrawal fixture lost its Organization.");
        }
        async Task ChangeMembership(string status)
        {
            await using var query = new NpgsqlCommand("UPDATE organization_members SET status=@status WHERE tenant_id=@tenant AND user_id=@actor;", admin);
            query.Parameters.AddWithValue("tenant", tenant); query.Parameters.AddWithValue("actor", actor); query.Parameters.AddWithValue("status", status);
            Require(await query.ExecuteNonQueryAsync(ct) == 1, "Image membership withdrawal fixture lost its actor.");
        }
        async Task<string> OwnedState() => await Snapshot() + await BoardScalar<string>("""
            SELECT jsonb_build_object(
              'boards',(SELECT jsonb_agg(to_jsonb(b) ORDER BY b.id) FROM boards b WHERE b.tenant_id=@tenant),
              'images',(SELECT jsonb_agg(to_jsonb(i) ORDER BY i.id) FROM board_background_images i WHERE i.tenant_id=@tenant)
            )::text;
            """);
        foreach (var withdrawMembership in new[] { true, false })
        {
            var baseline = await OwnedState();
            var grant = (await admission.AdmitAsync(copy.Value.Id, actor, ct)).Value;
            Require(grant is not null, "Copied image withdrawal fixture lacks an admitted private owner.");
            Task Withdraw() => withdrawMembership ? ChangeMembership("REMOVED") : ChangeOrganization("ARCHIVED");
            afterRead(Withdraw);
            try
            {
                Require((await read.PrepareAsync(copy.Value.Id, actor, ct)).ErrorCode == "board_not_found",
                    "Organization/membership withdrawal during staging retained copied image bytes.");
                afterRead(null); initialReads = reads();
                Require((await admission.RevalidateAsync(grant!, actor, ct)).ErrorCode == "board_not_found"
                    && (await read.PrepareAsync(copy.Value.Id, actor, ct)).ErrorCode == "board_not_found"
                    && reads() == initialReads,
                    "Withdrawn image admission reused its grant or reached provider bytes again.");
                Require(await OwnedState() == baseline,
                    "Image admission withdrawal changed Board ownership, audit, events, jobs or receipts.");
            }
            finally
            {
                afterRead(null);
                if (withdrawMembership) await ChangeMembership("ACTIVE"); else await ChangeOrganization("ACTIVE");
            }
            await Bytes(copy.Value.Id, actor); await Bytes(board, actor);
            Require(await OwnedState() == baseline, "Restored image readmission changed protected Board state.");
        }
        revision = await BoardScalar<long>("SELECT version FROM boards WHERE tenant_id=@tenant AND id=@board;");
        var cleared = await provider.GetRequiredService<IWorkManagementService>().UpdateBoardAsync(board, actor, original.Name, original.Description,
            "COLOR", null, revision, "image-selection-clear", ct);
        Require(cleared.Value is { BackgroundType: "COLOR", BackgroundValue: null }, "Board image could not return to its approved default.");
        await Bytes(copy.Value.Id, actor);
        Console.WriteLine("Restricted Board images: published private PNG ownership, late audit rollback/same-key recovery, private/stale admission, public consent/staging withdrawal, independent copy, copied-owner archive/selection and staged Organization/membership withdrawal, fresh readmission and attachment archive survival passed.");
    }
}
