using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Runtime;
using StrataAI.Application.WorkManagement;
using StrataAI.Domain.WorkManagement;
using StrataAI.Infrastructure.BackgroundJobs;
using StrataAI.Infrastructure.Identity;
using StrataAI.Infrastructure.Organizations;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Infrastructure.WorkManagement;

// Real Application command + restricted database adapters. Actor/session
// admission is explicitly synthetic here; this is not HTTP/session proof.
internal static class AttachmentPublicationContract
{
    private sealed class Clock(DateTimeOffset at) : IClock { public DateTimeOffset UtcNow { get; set; } = at; }
    private sealed class Actor : ICommandActorAuthorization
    {
        public int Calls; public int DenyAt = int.MaxValue;
        public Task<bool> VerifyAsync(Guid actorId, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(++Calls < DenyAt); }
    }
    private sealed class Context : IWorkCommandContext { public Guid? IdempotencyKey => null; }
    private static void Require(bool condition, string invariant) { if (!condition) throw new InvalidOperationException(invariant); }

    public static async Task RunAsync(NpgsqlConnection admin, string apiConnection, CancellationToken ct)
    {
        var at = AttachmentMetadataMapping.DatabaseTimestamp(DateTimeOffset.UtcNow);
        var tenant = Guid.NewGuid(); var user = Guid.NewGuid(); var secondOwner = Guid.NewGuid(); var outsider = Guid.NewGuid();
        var board = Guid.NewGuid(); var list = Guid.NewGuid(); var card = Guid.NewGuid();
        await using (var seed = new NpgsqlCommand("""
            INSERT INTO organizations(id,name,created_at,updated_at) VALUES(@tenant,'File publication contract',@at,@at);
            INSERT INTO users(id,email,email_normalized,display_name,status,email_verified,password_hash,created_at,updated_at)
            SELECT id,'publication-' || id::text || '@example.test',upper('publication-' || id::text || '@example.test'),
              'Publication contract','ACTIVE',true,'unused-contract-hash',@at,@at FROM unnest(@users) id;
            INSERT INTO organization_members(id,tenant_id,user_id,role,status)
              VALUES(@actor,@tenant,@actor,'OWNER','ACTIVE'),(@second,@tenant,@second,'OWNER','ACTIVE');
            INSERT INTO boards(id,tenant_id,name,created_at,updated_at) VALUES(@board,@tenant,'Private',@at,@at);
            INSERT INTO board_lists(id,tenant_id,board_id,name,rank,created_at,updated_at)
              VALUES(@list,@tenant,@board,'List','500000000000000000000000000000',@at,@at);
            INSERT INTO cards(id,tenant_id,board_id,list_id,title,description,rank,created_at,updated_at)
              VALUES(@card,@tenant,@board,@list,'Preserved title','Preserved description','500000000000000000000000000000',@at,@at);
            """, admin))
        {
            seed.Parameters.AddWithValue("tenant", tenant); seed.Parameters.AddWithValue("actor", user); seed.Parameters.AddWithValue("second", secondOwner);
            seed.Parameters.AddWithValue("users", new[] { user, secondOwner, outsider }); seed.Parameters.AddWithValue("board", board);
            seed.Parameters.AddWithValue("list", list); seed.Parameters.AddWithValue("card", card); seed.Parameters.AddWithValue("at", at);
            await seed.ExecuteNonQueryAsync(ct);
        }
        var clock = new Clock(at.AddSeconds(3)); var actor = new Actor(); var services = new ServiceCollection();
        var runtime = new RuntimeDescriptor(RuntimeMode.Production, "contract", "contract");
        services.AddLogging(); services.AddSingleton<IClock>(clock); services.AddSingleton<ICommandActorAuthorization>(actor);
        services.AddSingleton<IWorkCommandContext, Context>(); services.AddSingleton(_ => new PostgresConnectionFactory(apiConnection));
        services.AddSingleton<PostgresBackgroundJobStore>();
        var settings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["STRATAAI_AUTH_RETRY_CURRENT_KEY"] = "contract",
            ["STRATAAI_AUTH_RETRY_KEYS"] = JsonSerializer.Serialize(new Dictionary<string,string>
            { ["contract"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) })
        }).Build();
        services.AddStrataAiIdentity(settings, runtime); services.AddStrataAiOrganizations(runtime); services.AddStrataAiWorkManagement(runtime);
        await using var provider = services.BuildServiceProvider();
        var uploads = provider.GetRequiredService<IAttachmentUploadIntentStore>(); var unit = provider.GetRequiredService<IWorkManagementUnitOfWork>();
        var publication = provider.GetRequiredService<AttachmentFilePublicationService>();
        var digest = Convert.ToHexStringLower(SHA256.HashData(new byte[128]));
        async Task<AttachmentUploadIntent> Stored(long originalVersion = 1)
        {
            var value = AttachmentUploadIntent.Prepare(Guid.NewGuid(), tenant, card, user, Guid.NewGuid(), originalVersion,
                "Contract file.png", 128, digest, at.AddHours(1), at);
            var nonce = Guid.NewGuid();
            var result = await unit.ExecuteReadAsync(tenant, null, "contract_scope", () => Task.FromResult(true), async () =>
            {
                Require(await uploads.PrepareUploadAsync(value, ct) is not null, "Publication fixture prepare failed.");
                Require(await uploads.TryChangeUploadAsync(tenant, card, user, value.Id, 1,
                    new(AttachmentUploadAction.StartWrite, at, nonce, at.AddMinutes(5)), ct) is not null, "Publication fixture claim failed.");
                var stored = await uploads.TryChangeUploadAsync(tenant, card, user, value.Id, 2,
                    new(AttachmentUploadAction.RecordStored, at.AddSeconds(1), nonce,
                        Measured: new(new(tenant, value.Id), 128, digest), VerifiedMimeType: "image/png"), ct);
                Require(stored is { State: AttachmentUploadState.Stored, Version: 3 }, "Publication fixture measurement failed.");
                return WorkOperation<bool>.Success(true);
            }, ct);
            Require(result.Succeeded, "Publication fixture transaction failed."); return value;
        }
        async Task Effects(long revision, long count)
        {
            await using var query = new NpgsqlCommand("""
                SELECT c.version,c.title,c.description,c.rank,
                  (SELECT count(*) FROM attachments WHERE tenant_id=@tenant),
                  (SELECT count(*) FROM background_jobs WHERE tenant_id=@tenant AND job_type='ATTACHMENT_SCAN'),
                  (SELECT count(*) FROM audit_events WHERE tenant_id=@tenant),
                  (SELECT count(*) FROM work_events WHERE tenant_id=@tenant),
                  (SELECT count(*) FROM work_command_replays WHERE tenant_id=@tenant),
                  (SELECT stream_sequence FROM boards WHERE id=@board)
                FROM cards c WHERE c.id=@card;
                """, admin);
            query.Parameters.AddWithValue("tenant", tenant); query.Parameters.AddWithValue("card", card); query.Parameters.AddWithValue("board", board);
            await using var reader = await query.ExecuteReaderAsync(ct); Require(await reader.ReadAsync(ct), "Publication fixture Card disappeared.");
            Require(reader.GetInt64(0) == revision && reader.GetString(1) == "Preserved title" && reader.GetString(2) == "Preserved description"
                && reader.GetString(3) == "500000000000000000000000000000", "Publication changed unrelated Card fields or revision.");
            for (var index = 4; index <= 9; index++) Require(reader.GetInt64(index) == count, "Publication effects were partial or duplicated.");
        }
        async Task StillStored(AttachmentUploadIntent value)
        {
            await using var query = new NpgsqlCommand("SELECT status,version FROM attachment_upload_intents WHERE id=@id;", admin);
            query.Parameters.AddWithValue("id", value.Id); await using var reader = await query.ExecuteReaderAsync(ct);
            Require(await reader.ReadAsync(ct) && reader.GetString(0) == "STORED" && reader.GetInt64(1) == 3, "Failed publication lost retained measured intent.");
        }
        var upload = await Stored();
        Require((await publication.PublishAsync(card, outsider, Guid.Empty, Guid.Empty, "publication-outsider", ct)).ErrorCode == "card_not_found", "Publication disclosed invalid inputs to outsider.");
        Require((await publication.PublishAsync(card, user, Guid.NewGuid(), upload.RetryKey, "publication-mismatch", ct)).ErrorCode == "attachment_upload_unavailable", "Publication widened upload identity.");
        clock.UtcNow = at.AddHours(2);
        Require((await publication.PublishAsync(card, user, upload.Id, upload.RetryKey, "publication-expired", ct)).ErrorCode == "attachment_upload_unavailable", "Expired Stored intent was published.");
        clock.UtcNow = at.AddSeconds(3);
        var stale = await Stored(2);
        Require((await publication.PublishAsync(card, user, stale.Id, stale.RetryKey, "publication-stale", ct)).ErrorCode == "version_conflict", "Publication rebased original Card revision.");
        await Effects(1, 0); await StillStored(upload); await StillStored(stale);

        // Fail after Card/metadata/intent/scan-job writes. The actual command and
        // restricted adapters must roll all effects and the retry claim back.
        await using (var inject = new NpgsqlCommand($"""
            CREATE FUNCTION public.__contract_publication_reject() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN IF NEW.tenant_id='{tenant:D}'::uuid AND NEW.event_type='ATTACHMENT_ADDED' THEN
              RAISE EXCEPTION 'Injected publication audit failure' USING ERRCODE='23514'; END IF; RETURN NEW; END $$;
            CREATE TRIGGER __contract_publication_reject BEFORE INSERT ON audit_events FOR EACH ROW EXECUTE FUNCTION public.__contract_publication_reject();
            """, admin)) { await inject.ExecuteNonQueryAsync(ct); }
        try
        {
            Require((await publication.PublishAsync(card, user, upload.Id, upload.RetryKey, "publication-audit-failure", ct)).ErrorCode == "work_storage_unavailable", "Publication audit failure did not fail closed.");
            await Effects(1, 0); await StillStored(upload);
        }
        finally
        {
            await using var remove = new NpgsqlCommand("DROP TRIGGER __contract_publication_reject ON audit_events; DROP FUNCTION public.__contract_publication_reject();", admin);
            await remove.ExecuteNonQueryAsync(ct);
        }
        actor.Calls = 0; actor.DenyAt = 3;
        Require((await publication.PublishAsync(card, user, upload.Id, upload.RetryKey, "publication-actor-revoked", ct)).ErrorCode == "session_unavailable", "Final actor refusal was ignored.");
        await Effects(1, 0); await StillStored(upload); actor.DenyAt = int.MaxValue;
        var created = await publication.PublishAsync(card, user, upload.Id, upload.RetryKey, "publication-first", ct);
        Require(created.Succeeded && created.Value is { CardVersion: 2 } && created.Value.Attachment.Id == upload.Id
            && created.Value.Attachment.ScanStatus == AttachmentScanStatus.Pending, "Stored upload did not become one quarantined publication.");
        await Effects(2, 1);
        var replay = await publication.PublishAsync(card, user, upload.Id, upload.RetryKey, "publication-replay", ct);
        Require(replay.Succeeded && JsonSerializer.Serialize(replay) == JsonSerializer.Serialize(created), "Publication replay changed its original receipt.");
        Require(!JsonSerializer.Serialize(created).Contains(digest, StringComparison.OrdinalIgnoreCase), "Publication receipt disclosed private integrity.");
        await Effects(2, 1);
        await using (var revoke = new NpgsqlCommand("UPDATE organization_members SET status='REMOVED',updated_at=clock_timestamp(),version=version+1 WHERE tenant_id=@tenant AND user_id=@actor;", admin))
        { revoke.Parameters.AddWithValue("tenant", tenant); revoke.Parameters.AddWithValue("actor", user); await revoke.ExecuteNonQueryAsync(ct); }
        Require((await publication.PublishAsync(card, user, upload.Id, upload.RetryKey, "publication-revoked-replay", ct)).ErrorCode == "card_not_found", "Revoked member recovered publication receipt.");
        await Effects(2, 1);
        // Keep immutable audit and its parents until isolated CI DB teardown.
        Console.WriteLine("Restricted Application file publication: current scope, original Card CAS, expiry, audit/actor rollback, metadata/intent/scan-job/event atomicity and private authorized receipts passed.");
    }
}
