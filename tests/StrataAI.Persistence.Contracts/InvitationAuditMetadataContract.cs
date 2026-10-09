using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Onboarding;
using StrataAI.Application.Organizations;
using StrataAI.Application.Runtime;
using StrataAI.Infrastructure.BackgroundJobs;
using StrataAI.Infrastructure.Identity;
using StrataAI.Infrastructure.Onboarding;
using StrataAI.Infrastructure.Organizations;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Infrastructure.WorkManagement;

// FOUND-FR-009; PRD-03/60: actual restricted store projections and transitions.
// Account/Organization setup and actor admission are fixtures, not HTTP proof.
internal static class InvitationAuditMetadataContract
{
    public static async Task RunAsync(NpgsqlConnection admin,string apiConnection,CancellationToken ct)
    {
        static void Require(bool value) { if(!value) throw new InvalidOperationException("Invitation audit metadata contract failed."); }
        var owner=Guid.NewGuid(); var recipient=Guid.NewGuid(); var tenant=Guid.NewGuid();
        var email=$"invitation-clock-{recipient:N}@example.test";
        await using(var seed=new NpgsqlCommand("""
            INSERT INTO users(id,email,email_normalized,display_name,status,email_verified,password_hash,created_at,updated_at)
            VALUES(@owner,@owner_email,upper(@owner_email),'Owner fixture','ACTIVE',true,'unused-fixture-hash',now(),now()),
              (@recipient,@email,upper(@email),'Recipient fixture','ACTIVE',true,'unused-fixture-hash',now(),now());
            INSERT INTO organizations(id,name,owner_user_id,status,created_at,updated_at) VALUES(@tenant,'Invitation clock fixture',@owner,'ACTIVE',now(),now());
            INSERT INTO organization_members(id,tenant_id,user_id,role,status) VALUES(gen_random_uuid(),@tenant,@owner,'OWNER','ACTIVE');
            """,admin))
        {
            seed.Parameters.AddWithValue("owner",owner);seed.Parameters.AddWithValue("recipient",recipient);
            seed.Parameters.AddWithValue("tenant",tenant);seed.Parameters.AddWithValue("email",email);
            seed.Parameters.AddWithValue("owner_email",$"invitation-clock-owner-{owner:N}@example.test");await seed.ExecuteNonQueryAsync(ct);
        }
        var services=new ServiceCollection();services.AddLogging();services.AddSingleton<IClock,SystemClock>();
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddSingleton(new PostgresConnectionFactory(apiConnection));
        services.AddSingleton<ICommandActorContext,InProcessContext>();
        services.AddSingleton<ICommandActorAuthorization,CommandActorAuthorization>();
        services.AddSingleton<PostgresBackgroundJobStore>();
        var runtime=new RuntimeDescriptor(RuntimeMode.Production,"contract","contract");
        var ring=JsonSerializer.Serialize(new Dictionary<string,string>{["contract"]=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))});
        var settings=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
            ["STRATAAI_AUTH_RETRY_CURRENT_KEY"]="contract",["STRATAAI_AUTH_RETRY_KEYS"]=ring }).Build();
        services.AddStrataAiIdentity(settings,runtime);services.AddStrataAiOrganizations(runtime);
        services.AddStrataAiWorkManagement(runtime);services.AddStrataAiOnboarding(runtime,settings);
        await using var provider=services.BuildServiceProvider();var store=provider.GetRequiredService<IInvitationStore>();
        var commands=provider.GetRequiredService<IOrganizationUnitOfWork>();var tokens=provider.GetRequiredService<ISecureTokenService>();
        async Task<T> Owned<T>(Func<Task<T>> action)
        {
            var result=await commands.ExecuteAsync(tenant,owner,null,false,async()=>OrganizationOperation<T>.Success(await action()),ct);
            Require(result.Succeeded);return result.Value!;
        }
        async Task Check(InvitationRecord row)
        {
            await using var query=new NpgsqlCommand("""
                SELECT i.created_at=@created AND i.updated_at=@updated AND i.version=@version
                  AND r.created_at=i.created_at AND r.updated_at=i.updated_at
                FROM invitations i JOIN invitation_routes r ON r.invitation_id=i.id AND r.tenant_id=i.tenant_id AND r.token_hash=i.token_hash
                WHERE i.id=@id AND i.tenant_id=@tenant;
                """,admin);
            query.Parameters.AddWithValue("id",row.Id);query.Parameters.AddWithValue("tenant",tenant);
            query.Parameters.AddWithValue("created",row.CreatedAt);query.Parameters.AddWithValue("updated",row.UpdatedAt);
            query.Parameters.AddWithValue("version",row.Version);Require(await query.ExecuteScalarAsync(ct) is true);
        }
        var now=new DateTimeOffset(DateTimeOffset.UtcNow.UtcTicks/10*10,TimeSpan.Zero).AddMinutes(-1);
        InvitationRecord New() => new(Guid.NewGuid(),tenant,email,email.ToUpperInvariant(),tokens.Hash(tokens.Generate()),
            InvitationSurface.Internal,"MEMBER",owner,now,now.AddDays(1),null,null);
        var key=Guid.NewGuid();var fingerprint=new string('A',64);
        var first=await Owned(async()=>{var row=await store.CreateAsync(New(),ct);
            await store.SaveCreationReplayAsync(tenant,owner,key,fingerprint,row.Id,ct);return row;});
        Require(first.Version==1 && first.UpdatedAt==first.CreatedAt);await Check(first);
        // Recipient-visible Organization labels are publication snapshots.
        // A later parent rename must not invent an invitation mutation clock.
        await using(var rename=new NpgsqlCommand("""
            WITH renamed AS (
              UPDATE organizations SET name='Invitation route rename fixture',updated_at=clock_timestamp(),version=version+1
              WHERE id=@tenant RETURNING id,name)
            SELECT o.name<>r.organization_name AND r.created_at=@created AND r.updated_at=@updated
            FROM renamed o JOIN invitation_routes r ON r.tenant_id=o.id WHERE r.invitation_id=@id;
            """,admin))
        {
            rename.Parameters.AddWithValue("tenant",tenant);rename.Parameters.AddWithValue("id",first.Id);
            rename.Parameters.AddWithValue("created",first.CreatedAt);rename.Parameters.AddWithValue("updated",first.UpdatedAt);
            Require(await rename.ExecuteScalarAsync(ct) is true);
        }
        Require((await store.FindActiveByTokenHashAsync(first.TokenHash,DateTimeOffset.UtcNow,ct))==first);
        Require((await store.FindActiveByIdForEmailAsync(first.Id,recipient,email.ToUpperInvariant(),DateTimeOffset.UtcNow,ct))==first);
        var replay=await Owned(()=>store.FindCreationReplayAsync(tenant,owner,key,ct));
        Require(replay is {Expired:false} && replay.Fingerprint==fingerprint && replay.Invitation==first);
        // Retain a sub-microsecond command tick: the returned canonical row must
        // match stored precision, rather than echoing this caller's local value.
        var acceptedAt=new DateTimeOffset(DateTimeOffset.UtcNow.UtcTicks/10*10+7,TimeSpan.Zero);
        var accepted=await store.AcceptAsync(first.TokenHash,recipient,email.ToUpperInvariant(),acceptedAt,ct);
        Require(accepted.Succeeded && accepted.Invitation is not null);var consumed=accepted.Invitation!;
        Require(consumed.Version==2 && consumed.CreatedAt==first.CreatedAt && consumed.UpdatedAt>=first.UpdatedAt);await Check(consumed);
        Require(await Owned(()=>store.FindByIdAsync(tenant,first.Id,ct))==consumed);
        Require(await store.FindActiveByIdForEmailAsync(first.Id,recipient,email.ToUpperInvariant(),DateTimeOffset.UtcNow,ct)==consumed);
        replay=await Owned(()=>store.FindCreationReplayAsync(tenant,owner,key,ct));Require(replay!.Invitation==consumed);
        Require(!(await store.AcceptAsync(first.TokenHash,recipient,email.ToUpperInvariant(),DateTimeOffset.UtcNow,ct)).Succeeded);
        await Check(consumed);
        var next=await Owned(()=>store.CreateAsync(New(),ct));await Check(next);
        var before=await Owned(()=>store.FindByIdAsync(tenant,next.Id,ct));
        var refused=await commands.ExecuteAsync(tenant,owner,null,false,async()=>{
            Require(await store.RevokeAsync(tenant,next.Id,DateTimeOffset.UtcNow,ct));
            return OrganizationOperation<bool>.Failure("fixture_refusal");},ct);
        Require(!refused.Succeeded && await Owned(()=>store.FindByIdAsync(tenant,next.Id,ct))==before);
        await Check(before!);
        Require(await Owned(()=>store.RevokeAsync(tenant,next.Id,DateTimeOffset.UtcNow,ct)));
        var revoked=await Owned(()=>store.FindByIdAsync(tenant,next.Id,ct));Require(revoked is {Version:2});await Check(revoked!);
        Require(!await Owned(()=>store.RevokeAsync(tenant,next.Id,DateTimeOffset.UtcNow,ct)));
        Require(await Owned(()=>store.FindByIdAsync(tenant,next.Id,ct))==revoked);
        Console.WriteLine("Restricted invitation audit metadata: creation, routing reads, original replay, acceptance return, revocation, refused repeats and rollback passed.");
    }
    private sealed class InProcessContext : ICommandActorContext
    {
        public bool HasHttpRequest => false;
        public Guid? AuthenticatedUserId => null;
        public string? SessionTokenHash => null;
    }
}
