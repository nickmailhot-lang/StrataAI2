using Microsoft.Extensions.DependencyInjection;
using System.Security.Cryptography;
using System.Text.Json;
using Npgsql;
using StrataAI.Application.Identity;
using StrataAI.Application.Common;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.Runtime;
using StrataAI.Application.WorkManagement;
using StrataAI.Domain.WorkManagement;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Infrastructure.BackgroundJobs;
using StrataAI.Infrastructure.WorkManagement;

// Mandatory CI executable against real PostgreSQL and the restricted API login.
// It exercises persistence, Application file publication and Worker delivery.
// Actor admission and object/scanner fixtures are synthetic: this is not HTTP
// session authorization or deployed object-storage/antivirus acceptance.
var adminConnection = Environment.GetEnvironmentVariable("STRATAAI_CONTRACT_ADMIN_CONNECTION")
    ?? throw new InvalidOperationException("Contract admin connection is required.");
var apiConnection = Environment.GetEnvironmentVariable("STRATAAI_CONTRACT_API_CONNECTION")
    ?? throw new InvalidOperationException("Contract restricted API connection is required.");
var workerConnection=Environment.GetEnvironmentVariable("STRATAAI_CONTRACT_WORKER_CONNECTION")
    ?? throw new InvalidOperationException("Contract restricted Worker connection is required.");
var fixtureBytes=Enumerable.Range(0,128).Select(index=>(byte)index).ToArray();
var fixtureDigest=Convert.ToHexStringLower(SHA256.HashData(fixtureBytes));
var ct = CancellationToken.None;
var now = AttachmentMetadataMapping.DatabaseTimestamp(DateTimeOffset.UtcNow);
var organization = Guid.NewGuid(); var foreignOrganization = Guid.NewGuid(); var user = Guid.NewGuid(); var foreignUser = Guid.NewGuid();
var board = Guid.NewGuid(); var foreignBoard = Guid.NewGuid(); var list = Guid.NewGuid(); var foreignList = Guid.NewGuid();
var card = Guid.NewGuid(); var foreignCard = Guid.NewGuid();
await using var admin = new NpgsqlConnection(adminConnection); await admin.OpenAsync(ct);
async Task Seed(Guid tenant, Guid actor, Guid boardId, Guid listId, Guid cardId)
{
    await using var tx = await admin.BeginTransactionAsync(ct);
    await using var sql = new NpgsqlCommand("""
        INSERT INTO organizations(id,name,created_at,updated_at) VALUES(@tenant,'Persistence contract',@at,@at);
        INSERT INTO users(id,email,email_normalized,display_name,status,password_hash,created_at,updated_at)
         VALUES(@actor,@email,upper(@email),'Persistence contract','ACTIVE','unused-contract-hash',@at,@at);
        INSERT INTO organization_members(id,tenant_id,user_id,role,status) VALUES(@actor,@tenant,@actor,'MEMBER','ACTIVE');
        INSERT INTO boards(id,tenant_id,name,created_at,updated_at) VALUES(@board,@tenant,'Contract Board',@at,@at);
        INSERT INTO board_lists(id,tenant_id,board_id,name,rank,created_at,updated_at)
         VALUES(@list,@tenant,@board,'Contract List','500000000000000000000000000000',@at,@at);
        INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,created_at,updated_at)
         VALUES(@card,@tenant,@board,@list,'Contract Card','500000000000000000000000000000',@at,@at);
        """, admin, tx);
    sql.Parameters.AddWithValue("tenant",tenant); sql.Parameters.AddWithValue("actor",actor); sql.Parameters.AddWithValue("board",boardId);
    sql.Parameters.AddWithValue("list",listId); sql.Parameters.AddWithValue("card",cardId); sql.Parameters.AddWithValue("at",now);
    sql.Parameters.AddWithValue("email",$"contract-{actor:N}@example.test"); await sql.ExecuteNonQueryAsync(ct); await tx.CommitAsync(ct);
}
void Require(bool condition, string invariant) { if (!condition) throw new InvalidOperationException(invariant); }
var services = new ServiceCollection(); services.AddLogging(); services.AddSingleton(new PostgresConnectionFactory(apiConnection));
services.AddSingleton<IClock, SystemClock>();
services.AddSingleton<PostgresBackgroundJobStore>();
services.AddSingleton<ICommandActorAuthorization, NoActorFixture>();
services.AddStrataAiWorkManagement(new(RuntimeMode.Production,"contract","contract"));
await using var provider = services.BuildServiceProvider(); var store = provider.GetRequiredService<IAttachmentUploadIntentStore>();
var metadata = provider.GetRequiredService<IAttachmentMetadataStore>(); var unit = provider.GetRequiredService<IWorkManagementUnitOfWork>();
var scanJobs=provider.GetRequiredService<IAttachmentScanJobPublisher>();
async Task<long> ScanJobCount(Guid attachment)
{
    await using var query=new NpgsqlCommand("SELECT count(*) FROM background_jobs WHERE tenant_id=@tenant AND job_type='ATTACHMENT_SCAN' AND safe_metadata->>'attachmentId'=@attachment;",admin);
    query.Parameters.AddWithValue("tenant",organization); query.Parameters.AddWithValue("attachment",attachment.ToString("D"));
    return (long)(await query.ExecuteScalarAsync(ct))!;
}
async Task<T?> InScope<T>(Guid tenant, Func<Task<T>> operation)
{
    var result = await unit.ExecuteReadAsync(tenant,null,"contract_scope",() => Task.FromResult(true),
        async () => WorkOperation<T>.Success(await operation()),ct);
    Require(result.Succeeded,"Restricted persistence operation failed."); return result.Value;
}
Task<AttachmentUploadRecord?> Find(Guid tenant, Guid actor, Guid key) => InScope(tenant,() => store.FindUploadByRetryAsync(tenant,actor,key,ct));
Task<AttachmentUploadRecord?> Change(AttachmentUploadIntent value, long version, AttachmentUploadChange change) =>
    InScope(value.OrganizationId,() => store.TryChangeUploadAsync(value.OrganizationId,value.CardId,value.UploaderId,value.Id,version,change,ct));
AttachmentUploadIntent Intent(Guid? retry = null) => AttachmentUploadIntent.Prepare(Guid.NewGuid(),organization,card,user,retry ?? Guid.NewGuid(),1,
    "Contract image",128,fixtureDigest,now.AddHours(1),now);
try
{
    await RuntimeSchemaReadinessContract.RunAsync(admin,apiConnection,workerConnection,ct);
    await OrganizationMetadataEventContract.RunAsync(admin,apiConnection,workerConnection,ct);
    await OrganizationMetadataDiscoveryContract.RunAsync(admin,apiConnection,workerConnection,ct);
    await OrganizationDeletionProgressContract.RunAsync(admin,apiConnection,workerConnection,ct);
    await OrganizationDeletionPublicationContract.RunAsync(admin,apiConnection,ct);
    await OrganizationDeletionTerminalContract.RunAsync(admin,apiConnection,workerConnection,ct);
    await OrganizationDeletionCandidatesContract.RunAsync(admin,apiConnection,workerConnection,ct);
    await OrganizationDeletionPagesContract.RunAsync(admin,apiConnection,workerConnection,ct);
    await OrganizationDeletionDiscoveryContract.RunAsync(admin,apiConnection,workerConnection,ct);
    await Seed(organization,user,board,list,card); await Seed(foreignOrganization,foreignUser,foreignBoard,foreignList,foreignCard);
    await SearchTraversalStoreContract.RunAsync(admin,apiConnection,organization,user,ct);
    await SearchContentStoreContract.RunAsync(admin,provider,organization,board,user,ct);
    var value = Intent(); var prepared = await InScope(organization,() => store.PrepareUploadAsync(value,ct));
    Require(prepared is { Version:1, State:AttachmentUploadState.Prepared },"Prepared upload persistence shape failed.");
    Require(await InScope(organization,() => store.PrepareUploadAsync(Intent(value.RetryKey),ct)) is null,"Upload retry was duplicated.");
    Require(await Find(foreignOrganization,user,value.RetryKey) is null && await Find(organization,foreignUser,value.RetryKey) is null,"Upload discovery widened scope.");
    foreach (var scope in new[] { (foreignOrganization,card,user),(organization,foreignCard,user),(organization,card,foreignUser) })
        Require(await InScope(scope.Item1,() => store.TryChangeUploadAsync(scope.Item1,scope.Item2,scope.Item3,value.Id,1,new(AttachmentUploadAction.Abandon,now),ct)) is null,"Upload CAS widened scope.");
    var contenders = await Task.WhenAll(Enumerable.Range(0,8).Select(_ => Change(value,1,new(AttachmentUploadAction.StartWrite,now,Guid.NewGuid(),now.AddMinutes(5)))));
    Require(contenders.Count(x => x is not null)==1,"Concurrent upload claims had multiple writers.");
    var writing = contenders.Single(x => x is not null)!; var nonce = writing.WriteLeaseId!.Value;
    Require(await Change(value,2,new(AttachmentUploadAction.UnknownWrite,now,Guid.NewGuid())) is null,"Wrong writer nonce accepted.");
    var renewed = await Change(value,2,new(AttachmentUploadAction.RenewWrite,now.AddSeconds(1),nonce,now.AddMinutes(6)));
    Require(renewed is { Version:3 },"Upload writer renewal failed.");
    Require(await Change(value,3,new(AttachmentUploadAction.RenewWrite,now.AddSeconds(2),nonce,now.AddMinutes(6))) is null,"Unchanged renewal advanced revision.");
    var unknown = await Change(value,3,new(AttachmentUploadAction.UnknownWrite,now.AddSeconds(2),nonce)); Require(unknown is { Version:4,State:AttachmentUploadState.Reconcile },"Unknown outcome was lost.");
    Require(await Change(value,4,new(AttachmentUploadAction.StartWrite,now.AddSeconds(3),Guid.NewGuid(),now.AddMinutes(5))) is null,"Unknown outcome started another writer.");
    Require(await Change(value,4,new(AttachmentUploadAction.ConfirmMissing,now.AddSeconds(3))) is { Version:5,State:AttachmentUploadState.Prepared },"Verified absence did not restore original intent.");
    var replacement = Guid.NewGuid(); await Change(value,5,new(AttachmentUploadAction.StartWrite,now.AddSeconds(4),replacement,now.AddMinutes(5)));
    var measured = new StoredAttachmentObject(new(organization,value.Id),128,fixtureDigest);
    Require(await Change(value,6,new(AttachmentUploadAction.RecordStored,now.AddSeconds(5),nonce,Measured:measured,VerifiedMimeType:"image/png")) is null,"Stale writer callback accepted.");
    var stored = await Change(value,6,new(AttachmentUploadAction.RecordStored,now.AddSeconds(5),replacement,Measured:measured,VerifiedMimeType:"image/png"));
    Require(stored is { Version:7,State:AttachmentUploadState.Stored,WriteLeaseId:null },"Verified stored upload failed.");
    Require(await Change(value,7,new(AttachmentUploadAction.Publish,now.AddSeconds(6))) is null,"Upload published without matching quarantine.");
    var rolledBack = await unit.ExecuteReadAsync<AttachmentUploadRecord?>(organization,null,"contract_scope",() => Task.FromResult(true),async () =>
    {
        await metadata.CreateFileAttachmentAsync(measured,card,user,value.DisplayName,"image/png",now.AddSeconds(5),ct);
        var proposed=await store.TryChangeUploadAsync(organization,card,user,value.Id,7,new(AttachmentUploadAction.Publish,now.AddSeconds(6)),ct);
        Require(proposed is {Version:8},"Atomic publication failed.");
        var privateFile=await metadata.FindFileAttachmentAsync(organization,card,value.Id,ct);
        Require(privateFile is not null && await scanJobs.PublishScanAsync(proposed!,privateFile,user,"scan-contract-first",ct),"Atomic scan job publication failed.");
        return WorkOperation<AttachmentUploadRecord?>.Failure("intentional_contract_rollback");
    },ct);
    Require(!rolledBack.Succeeded && await Find(organization,user,value.RetryKey) is { Version:7,State:AttachmentUploadState.Stored },"Publication rollback lost stored intent.");
    Require(await InScope(organization,() => metadata.FindFileAttachmentAsync(organization,card,value.Id,ct)) is null,"Publication rollback retained metadata.");
    Require(await ScanJobCount(value.Id)==0,"Publication rollback retained its scan job.");
    var published = await InScope(organization,async () =>
    {
        await metadata.CreateFileAttachmentAsync(measured,card,user,value.DisplayName,"image/png",now.AddSeconds(5),ct);
        var proposed=await store.TryChangeUploadAsync(organization,card,user,value.Id,7,new(AttachmentUploadAction.Publish,now.AddSeconds(6)),ct);
        var privateFile=await metadata.FindFileAttachmentAsync(organization,card,value.Id,ct);
        Require(privateFile is not null && proposed is not null && await scanJobs.PublishScanAsync(proposed,privateFile,user,"scan-contract-first",ct),"Atomic scan job retry failed.");
        Require(!await scanJobs.PublishScanAsync(proposed!,privateFile!,user,"scan-contract-second",ct),"Scan publication duplicated its durable identity.");
        return proposed;
    });
    Require(published is { Version:8,State:AttachmentUploadState.Published } && published.ExpectedSha256==value.ExpectedSha256,"Atomic publication persistence failed.");
    Require(await ScanJobCount(value.Id)==1,"Scan job was lost or duplicated.");
    await using(var queue=new NpgsqlCommand("SELECT correlation_id,safe_metadata::text FROM background_jobs WHERE tenant_id=@tenant AND job_type='ATTACHMENT_SCAN' AND safe_metadata->>'attachmentId'=@attachment;",admin))
    {
        queue.Parameters.AddWithValue("tenant",organization); queue.Parameters.AddWithValue("attachment",value.Id.ToString("D"));
        await using var row=await queue.ExecuteReaderAsync(ct); Require(await row.ReadAsync(ct),"Scan job missing.");
        Require(row.GetString(0)=="scan-contract-first" && AttachmentScanAttempt.Parse(row.GetString(1))==new AttachmentScanAttempt(value.Id,card,1),"Retried scan job changed original safe metadata.");
    }
    Require(await Change(value,8,new(AttachmentUploadAction.Abandon,now.AddSeconds(7))) is null,"Published upload was abandoned.");
    var expiry = Intent(); await InScope(organization,() => store.PrepareUploadAsync(expiry,ct));
    await Change(expiry,1,new(AttachmentUploadAction.StartWrite,now,Guid.NewGuid(),now.AddMinutes(5)));
    Require(await Change(expiry,2,new(AttachmentUploadAction.ExpiredWriter,now.AddMinutes(4))) is null,"Active writer reconciled prematurely.");
    await Change(expiry,2,new(AttachmentUploadAction.ExpiredWriter,now.AddHours(1)));
    Require(await Change(expiry,3,new(AttachmentUploadAction.ConfirmMissing,now.AddHours(1))) is null,"Expired upload admitted another writer.");
    Require(await Change(expiry,3,new(AttachmentUploadAction.Abandon,now.AddHours(1))) is { State:AttachmentUploadState.Abandoned },"Expired intent was not retained.");
    Require(await InScope(organization,() => store.PrepareUploadAsync(Intent(expiry.RetryKey),ct)) is null,"Abandoned retry identity reused.");
    var reconciled = Intent(); await InScope(organization,() => store.PrepareUploadAsync(reconciled,ct));
    var reconcileNonce = Guid.NewGuid(); await Change(reconciled,1,new(AttachmentUploadAction.StartWrite,now,reconcileNonce,now.AddMinutes(5)));
    await Change(reconciled,2,new(AttachmentUploadAction.UnknownWrite,now.AddSeconds(1),reconcileNonce));
    var wrongMeasure = new StoredAttachmentObject(new(organization,reconciled.Id),128,new string('b',64));
    Require(await Change(reconciled,3,new(AttachmentUploadAction.RecordReconciled,now.AddSeconds(2),Measured:wrongMeasure,VerifiedMimeType:"image/png")) is null,"Reconciliation trusted mismatched bytes.");
    var recoveredMeasure = new StoredAttachmentObject(new(organization,reconciled.Id),128,reconciled.ExpectedSha256);
    var recovered = await Change(reconciled,3,new(AttachmentUploadAction.RecordReconciled,now.AddSeconds(2),Measured:recoveredMeasure,VerifiedMimeType:"image/png"));
    Require(recovered is { Version:4,State:AttachmentUploadState.Stored },"Existing-object reconciliation failed.");
    var tombstone = await Change(reconciled,4,new(AttachmentUploadAction.Abandon,now.AddSeconds(3)));
    Require(tombstone is { Version:5,State:AttachmentUploadState.Abandoned } && tombstone.StoredAt==recovered!.StoredAt
        && tombstone.VerifiedMimeType=="image/png" && tombstone.ExpectedSha256==reconciled.ExpectedSha256,"Stored upload tombstone lost private reconciliation identity.");
    Require(await Change(reconciled,5,new(AttachmentUploadAction.ConfirmMissing,now.AddSeconds(4))) is null,"Stored tombstone was resurrected.");
    await AttachmentWorkerContract.RunAsync(admin,workerConnection,provider,organization,foreignOrganization,value,
        fixtureBytes,fixtureDigest,ct);
    await AttachmentPublicationContract.RunAsync(admin,apiConnection,ct);
    await ActivityEventSourceStoreContract.RunAsync(admin,provider,organization,user,ct);
    await ActivityPrivateTargetStoreContract.RunAsync(provider,organization,foreignOrganization,user,foreignUser,card,ct);
    await CardCommentStoreContract.RunAsync(admin,provider,organization,foreignOrganization,value.CardId,value.UploaderId,ct);
    await CardMentionMemberStoreContract.RunAsync(admin,provider,organization,foreignOrganization,board,foreignBoard,foreignUser,card,ct);
    await CommentMentionSnapshotStoreContract.RunAsync(admin,provider,organization,foreignOrganization,card,foreignCard,user,foreignUser,ct);
    await CommentMentionNotificationContract.RunAsync(admin,provider,organization,card,user,foreignUser,ct);
    await CardMassMentionQuotaContract.RunAsync(admin,provider,organization,card,user,foreignOrganization,foreignCard,foreignUser,ct);
    await UserMentionHandleStoreContract.RunAsync(admin,apiConnection,ct);
    Console.WriteLine("Restricted C# upload persistence: scope, concurrent writers, nonce/revision CAS, reconciliation, metadata/scan-job rollback and retained expiry passed.");
}
finally
{
    // Successful Worker effects include append-only audit. Keep that evidence
    // and its referenced parents until the isolated CI database is torn down.
    await using var retained=new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM audit_events WHERE tenant_id=ANY(@tenants));",admin);
    retained.Parameters.AddWithValue("tenants",new[] {organization,foreignOrganization});
    var hasAudit=await retained.ExecuteScalarAsync(ct) is true;
    if(!hasAudit)
    {
    await using var cleanup = new NpgsqlCommand("""
        DELETE FROM attachment_upload_intents WHERE tenant_id=ANY(@tenants);
        DELETE FROM background_jobs WHERE tenant_id=ANY(@tenants);
        DELETE FROM attachments WHERE tenant_id=ANY(@tenants);
        DELETE FROM cards WHERE tenant_id=ANY(@tenants); DELETE FROM board_lists WHERE tenant_id=ANY(@tenants);
        DELETE FROM boards WHERE tenant_id=ANY(@tenants); DELETE FROM organization_members WHERE tenant_id=ANY(@tenants);
        DELETE FROM organizations WHERE id=ANY(@tenants); DELETE FROM users WHERE id=ANY(@users);
        """,admin);
    cleanup.Parameters.AddWithValue("tenants",new[] { organization,foreignOrganization }); cleanup.Parameters.AddWithValue("users",new[] { user,foreignUser });
    await cleanup.ExecuteNonQueryAsync(ct);
    }
}

sealed class NoActorFixture : ICommandActorAuthorization
{
    public Task<bool> VerifyAsync(Guid actorId,CancellationToken cancellationToken=default) =>
        throw new InvalidOperationException("Persistence fixture must not claim HTTP actor authorization.");
}
