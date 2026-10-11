using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.DataProtection;
using Npgsql;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.Runtime;
using StrataAI.Infrastructure.Organizations;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Infrastructure.WorkManagement;

// Real restricted SQL/owning transactions; session admission is synthetic here.
internal static class OrganizationOperationalLifecycleContract
{
    private sealed class Admission : ICommandActorAuthorization
    {
        public bool Active { get; set; } = true;
        public Task<bool> VerifyAsync(Guid actor, CancellationToken cancellationToken = default) => Task.FromResult(Active);
    }
    private static void Require(bool condition, string rule) { if (!condition) throw new InvalidOperationException(rule); }
    public static async Task RunAsync(NpgsqlConnection admin, string apiConnection, CancellationToken ct)
    {
        var organization = Guid.NewGuid(); var other = Guid.NewGuid(); var actor = Guid.NewGuid(); var stranger = Guid.NewGuid();
        await using (var seed = new NpgsqlCommand("""
            INSERT INTO users(id,email,email_normalized,display_name,status,password_hash,email_verified,created_at,updated_at)
            VALUES(@actor,@email,upper(@email),'Lifecycle original owner','ACTIVE','unusable-ci-hash',true,now(),now()),
                  (@stranger,@otheremail,upper(@otheremail),'Lifecycle ordinary member','ACTIVE','unusable-ci-hash',true,now(),now());
            INSERT INTO organizations(id,name,owner_user_id,status,version,organization_type,created_at,updated_at)
            VALUES(@tenant,'Retained lifecycle contract',@actor,'ACTIVE',1,'STRATA',now(),now()),
                  (@other,'Other lifecycle contract',@actor,'ACTIVE',1,'STRATA',now(),now());
            INSERT INTO organization_members(id,tenant_id,user_id,role,status,created_at,updated_at)
            VALUES(@member,@tenant,@actor,'OWNER','ACTIVE',now(),now()),
                  (@othermember,@tenant,@stranger,'MEMBER','ACTIVE',now(),now());
            """, admin))
        {
            seed.Parameters.AddWithValue("actor", actor); seed.Parameters.AddWithValue("stranger", stranger);
            seed.Parameters.AddWithValue("email", $"lifecycle-{actor:N}@example.test"); seed.Parameters.AddWithValue("otheremail", $"lifecycle-{stranger:N}@example.test");
            seed.Parameters.AddWithValue("tenant", organization); seed.Parameters.AddWithValue("other", other);
            seed.Parameters.AddWithValue("member", Guid.NewGuid()); seed.Parameters.AddWithValue("othermember", Guid.NewGuid());
            await seed.ExecuteNonQueryAsync(ct);
        }
        var admission = new Admission(); var services = new ServiceCollection(); services.AddLogging();
        services.AddSingleton(new PostgresConnectionFactory(apiConnection)); services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddSingleton<ICommandActorAuthorization>(admission);
        services.AddSingleton(new IdentityPolicy(true, false, 12, TimeSpan.FromHours(1), TimeSpan.FromHours(1)));
        var runtime = new RuntimeDescriptor(RuntimeMode.Production, "contract", "contract");
        services.AddStrataAiOrganizations(runtime); services.AddStrataAiWorkManagement(runtime);
        await using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<OrganizationOperationalLifecycleService>();
        var store = provider.GetRequiredService<IOrganizationOperationalLifecycleStore>();
        var unit = provider.GetRequiredService<IOrganizationOperationalLifecycleUnitOfWork>();
        var key = Guid.NewGuid();
        var suspended = await service.ChangeAsync(organization, actor, OrganizationStatus.Suspended, 1, key, "restricted-suspension", ct);
        Require(suspended.Succeeded && suspended.Value!.Version == 2, "Restricted lifecycle suspension failed.");
        var review = await service.ReviewAsync(organization, actor, ct);
        Require(review.Succeeded && review.Value!.Status == OrganizationStatus.Suspended && review.Value.Version == 2,
            "Restricted lifecycle current administrative review failed for a suspended parent.");
        var directory = provider.GetRequiredService<OrganizationOperationalLifecycleDirectoryService>();
        var discovered = await directory.ReadAsync(actor, null, ct);
        Require(discovered.Succeeded && discovered.Value!.Items.Any(item => item.OrganizationId == organization
            && item.Status == OrganizationStatus.Suspended && item.Name == "Retained lifecycle contract")
            && discovered.Value.Items.All(item => item.OrganizationId != other), "Restricted inactive administrative discovery lost scope.");
        var memberDirectory = await directory.ReadAsync(stranger, null, ct);
        Require(memberDirectory.Succeeded && memberDirectory.Value!.Items.Count == 0, "Restricted lifecycle directory disclosed an ordinary member target.");
        Require((await service.ReviewAsync(organization, stranger, ct)).ErrorCode == "organization_not_found"
            && (await service.ReviewAsync(other, actor, ct)).ErrorCode == "organization_not_found",
            "Restricted lifecycle review admitted a member or foreign tenant.");
        var recovered = await service.ChangeAsync(organization, actor, OrganizationStatus.Suspended, 1, key, "retry", ct);
        Require(recovered.Succeeded && recovered.Value == suspended.Value, "Restricted lifecycle original receipt changed.");
        Require((await service.ChangeAsync(organization, stranger, OrganizationStatus.Active, 2, Guid.NewGuid(), "member-denied", ct)).ErrorCode == "organization_not_found",
            "Ordinary member changed retained lifecycle state.");
        Require((await service.ChangeAsync(other, actor, OrganizationStatus.Suspended, 1, Guid.NewGuid(), "foreign-denied", ct)).ErrorCode == "organization_not_found",
            "Foreign lifecycle scope disclosed a tenant.");
        var normal = provider.GetRequiredService<IOrganizationUnitOfWork>();
        var forbidden = await normal.ExecuteAsync<bool>(organization, actor, null, false, () => Task.FromResult(OrganizationOperation<bool>.Success(true)), ct);
        Require(forbidden.ErrorCode == "organization_not_found", "Suspended parent admitted normal work.");
        var reactivated = await service.ChangeAsync(organization, actor, OrganizationStatus.Active, 2, Guid.NewGuid(), "restricted-reactivation", ct);
        Require(reactivated.Succeeded && reactivated.Value!.Version == 3, "Restricted lifecycle reactivation failed.");
        var retryAfter = await service.ChangeAsync(organization, actor, OrganizationStatus.Suspended, 1, key, "old-original", ct);
        Require(retryAfter.Succeeded && retryAfter.Value == suspended.Value, "Original lifecycle recovery restored a later state.");
        await using (var proof = new NpgsqlCommand("""
            SELECT o.status='ACTIVE' AND o.version=3 AND o.operational_lifecycle_version=3
             AND (SELECT count(*) FROM organization_operational_lifecycle_history WHERE tenant_id=o.id)=2
             AND (SELECT count(*) FROM organization_operational_lifecycle_receipts WHERE tenant_id=o.id)=2
             AND (SELECT count(*) FROM organization_operational_lifecycle_events WHERE tenant_id=o.id)=2
             AND EXISTS(SELECT 1 FROM audit_events WHERE id=@event AND tenant_id=o.id AND event_type='ORGANIZATION_SUSPENDED')
            FROM organizations o WHERE id=@tenant;
            """, admin))
        {
            proof.Parameters.AddWithValue("tenant", organization); proof.Parameters.AddWithValue("event", suspended.Value!.EventId);
            Require(await proof.ExecuteScalarAsync(ct) is true, "Lifecycle state/source/receipt/audit/event proof differed.");
        }
        foreach (var withdrawSession in new[] { false, true })
        {
            var rollbackKey = Guid.NewGuid();
            var rolledBack = await unit.ExecuteAsync<bool>(organization, actor, async () =>
            {
                var scope = (await store.ReadScopeAsync(organization, actor, ct))!;
                var at = DateTimeOffset.UtcNow; at = at.AddTicks(-(at.Ticks % 10));
                var source = new OrganizationOperationalLifecycleTransition(organization, Guid.NewGuid(), actor, scope.ActorLabel,
                    "ORGANIZATION_OWNER", scope.MembershipId, scope.MembershipVersion, scope.Status, OrganizationStatus.Deactivated,
                    scope.Version + 1, "restricted-rollback", at);
                var receipt = new OrganizationOperationalLifecycleReceipt(actor, rollbackKey,
                    OrganizationOperationalLifecycleRules.Fingerprint(organization, source.Status, scope.Version), source, at.AddHours(24));
                Require(await store.WriteAsync(scope.Version, source, receipt, ct) == OrganizationOperationalLifecycleWriteResult.Written,
                    "Restricted lifecycle rollback setup failed.");
                if (!withdrawSession) return OrganizationOperation<bool>.Failure("injected_after_source_failure");
                admission.Active = false;
                return OrganizationOperation<bool>.Success(true);
            }, ct);
            Require(rolledBack.ErrorCode == (withdrawSession ? "session_unavailable" : "injected_after_source_failure"),
                "Lifecycle owning transaction committed after failure or session withdrawal.");
            admission.Active = true;
            var checkedRollback = await unit.ExecuteAsync<bool>(organization, actor, async () =>
            {
                Require((await store.ReadScopeAsync(organization, actor, ct))!.Version == 3, "Lifecycle rollback changed aggregate state.");
                Require(await store.ReadReceiptAsync(organization, actor, rollbackKey, ct) is null, "Lifecycle rollback retained a receipt.");
                return OrganizationOperation<bool>.Success(true);
            }, ct);
            Require(checkedRollback.Succeeded, "Restricted lifecycle rollback proof could not read scope.");
        }
        admission.Active = false;
        Require((await service.ChangeAsync(organization, actor, OrganizationStatus.Deactivated, 3, Guid.NewGuid(), "withdrawn-session", ct)).ErrorCode == "session_unavailable",
            "Withdrawn lifecycle session committed a change."); admission.Active = true;
        // A direct status/pointer write without its matching immutable source must fail at commit.
        await using var api = new NpgsqlConnection(apiConnection); await api.OpenAsync(ct);
        await using (var isolation = await api.BeginTransactionAsync(ct))
        {
            await using var hidden = new NpgsqlCommand("""
                SELECT set_config('app.tenant_id',@other::text,true);
                SELECT (SELECT count(*) FROM organization_operational_lifecycle_history WHERE tenant_id=@tenant)
                     + (SELECT count(*) FROM organization_operational_lifecycle_receipts WHERE tenant_id=@tenant)
                     + (SELECT count(*) FROM organization_operational_lifecycle_events WHERE tenant_id=@tenant);
                """, api, isolation);
            hidden.Parameters.AddWithValue("other", other); hidden.Parameters.AddWithValue("tenant", organization);
            await using var rows = await hidden.ExecuteReaderAsync(ct);
            Require(await rows.NextResultAsync(ct) && await rows.ReadAsync(ct) && rows.GetInt64(0) == 0,
                "Restricted lifecycle RLS exposed foreign history, receipts or events.");
        }
        await using (var transaction = await api.BeginTransactionAsync(ct))
        {
            await using var raw = new NpgsqlCommand("""
                SELECT set_config('app.tenant_id',@tenant::text,true);
                UPDATE organizations SET status='SUSPENDED',version=4,operational_lifecycle_version=4,updated_at=clock_timestamp() WHERE id=@tenant;
                """, api, transaction); raw.Parameters.AddWithValue("tenant", organization);
            var rejected = false;
            try { await raw.ExecuteNonQueryAsync(ct); await transaction.CommitAsync(ct); }
            catch (PostgresException exception) when (exception.SqlState is PostgresErrorCodes.CheckViolation or PostgresErrorCodes.ForeignKeyViolation) { rejected = true; }
            Require(rejected, "Status-only lifecycle change committed without a source.");
        }
        Console.WriteLine("Restricted Organization lifecycle transitions, original receipts, administrative refusal, rollback and source-only commit guards passed.");
    }
}
