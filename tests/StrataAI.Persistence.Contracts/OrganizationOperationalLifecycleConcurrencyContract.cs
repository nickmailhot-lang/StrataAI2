using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Npgsql;
using StrataAI.Application.Common;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.Runtime;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.Organizations;
using StrataAI.Infrastructure.BackgroundJobs;
using StrataAI.Infrastructure.Identity;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Infrastructure.WorkManagement;

internal static class OrganizationOperationalLifecycleConcurrencyContract
{
    private sealed class Admission : ICommandActorAuthorization
    {
        public Task<bool> VerifyAsync(Guid actor, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
    private sealed class CommandContext : IWorkCommandContext { public Guid? IdempotencyKey { get; } = Guid.NewGuid(); }
    private static void Require(bool value, string rule) { if (!value) throw new InvalidOperationException(rule); }
    public static async Task RunAsync(NpgsqlConnection admin, string apiConnection, CancellationToken ct)
    {
        foreach (var target in new[] { OrganizationStatus.Suspended, OrganizationStatus.Deactivated })
        foreach (var workFirst in new[] { false, true })
        {
            var organization = Guid.NewGuid(); var actor = Guid.NewGuid();
            await using (var seed = new NpgsqlCommand("""
                INSERT INTO users(id,email,email_normalized,display_name,status,password_hash,email_verified,created_at,updated_at)
                 VALUES(@actor,@email,upper(@email),'Concurrency actor','ACTIVE','fixture',true,now(),now());
                INSERT INTO organizations(id,name,owner_user_id,status,version,organization_type,created_at,updated_at)
                 VALUES(@tenant,'Retained concurrency Organization',@actor,'ACTIVE',1,'STRATA',now(),now());
                INSERT INTO organization_members(id,tenant_id,user_id,role,status,created_at,updated_at)
                 VALUES(@membership,@tenant,@actor,'OWNER','ACTIVE',now(),now());
                """, admin))
            {
                seed.Parameters.AddWithValue("actor", actor); seed.Parameters.AddWithValue("tenant", organization);
                seed.Parameters.AddWithValue("membership", Guid.NewGuid()); seed.Parameters.AddWithValue("email", $"lifecycle-race-{actor:N}@example.test");
                await seed.ExecuteNonQueryAsync(ct);
            }
            var lifecycleName = "lifecycle-race-" + Guid.NewGuid().ToString("N");
            var workName = "work-race-" + Guid.NewGuid().ToString("N");
            await using var lifecycleProvider = Provider(apiConnection, lifecycleName);
            await using var workProvider = Provider(apiConnection, workName);
            var lifecycle = lifecycleProvider.GetRequiredService<OrganizationOperationalLifecycleService>();
            var work = workProvider.GetRequiredService<IWorkManagementService>();
            await using var gate = await admin.BeginTransactionAsync(ct);
            await using (var hold = new NpgsqlCommand("SELECT id FROM organizations WHERE id=@tenant FOR UPDATE", admin, gate))
            {
                hold.Parameters.AddWithValue("tenant", organization); Require(await hold.ExecuteScalarAsync(ct) is Guid, "Concurrency parent gate missing.");
            }
            Task<OrganizationOperation<OrganizationOperationalLifecycleTransition>>? transition = null;
            Task<WorkOperation<BoardRecord>>? creation = null;
            Task<OrganizationOperation<OrganizationOperationalLifecycleTransition>> StartTransition() => Task.Run(() =>
                lifecycle.ChangeAsync(organization, actor, target, 1, Guid.NewGuid(), "controlled-lifecycle-race", ct), ct);
            Task<WorkOperation<BoardRecord>> StartCreation() => Task.Run(() =>
                work.CreateBoardAsync(organization, actor, "Retained before inactivity", null, BoardVisibility.Private, null, null, "controlled-work-race", ct), ct);
            if (workFirst) { creation = StartCreation(); await Waiting(workName); transition = StartTransition(); await Waiting(lifecycleName); }
            else { transition = StartTransition(); await Waiting(lifecycleName); creation = StartCreation(); await Waiting(workName); }
            await gate.CommitAsync(ct);
            var changed = await transition.WaitAsync(TimeSpan.FromSeconds(15), ct);
            var created = await creation.WaitAsync(TimeSpan.FromSeconds(15), ct);
            Require(changed.Succeeded && changed.Value!.Version == 2 && changed.Value.Status == target, "Concurrent lifecycle transition failed.");
            Require(workFirst ? created.Succeeded : !created.Succeeded && created.ErrorCode == "organization_not_found",
                "Work escaped the serialized inactive parent decision.");
            await using var proof = new NpgsqlCommand("""
                SELECT o.status=@status AND o.version=2
                 AND (SELECT count(*) FROM boards WHERE tenant_id=o.id)=@boards
                 AND (SELECT count(*) FROM work_events WHERE tenant_id=o.id)=@boards
                 AND (SELECT count(*) FROM background_jobs WHERE tenant_id=o.id AND job_type='WORK_EVENT_READY')=@boards
                 AND (SELECT count(*) FROM audit_events WHERE tenant_id=o.id AND event_type='BOARD_CREATED')=@boards
                 AND (SELECT count(*) FROM organization_operational_lifecycle_history WHERE tenant_id=o.id)=1
                 AND (SELECT count(*) FROM organization_operational_lifecycle_receipts WHERE tenant_id=o.id)=1
                 AND (SELECT count(*) FROM organization_operational_lifecycle_events WHERE tenant_id=o.id)=1
                FROM organizations o WHERE id=@tenant;
                """, admin);
            proof.Parameters.AddWithValue("tenant", organization); proof.Parameters.AddWithValue("status", target == OrganizationStatus.Suspended ? "SUSPENDED" : "DEACTIVATED");
            proof.Parameters.AddWithValue("boards", workFirst ? 1L : 0L);
            Require(await proof.ExecuteScalarAsync(ct) is true, "Concurrent work/lifecycle left partial state or lost retained history.");

            async Task Waiting(string name)
            {
                // Observe actual database lock admission before starting the next
                // contender; no sleep is used to assume which command won.
                var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
                while (DateTimeOffset.UtcNow < deadline)
                {
                    await using (var refresh = new NpgsqlCommand("SELECT pg_stat_clear_snapshot()", admin, gate))
                        await refresh.ExecuteNonQueryAsync(ct);
                    await using var check = new NpgsqlCommand("""
                        SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE application_name=@name
                         AND state='active' AND wait_event_type='Lock' AND query LIKE '%organizations%');
                        """, admin, gate);
                    check.Parameters.AddWithValue("name", name);
                    if (await check.ExecuteScalarAsync(ct) is true) return;
                    await Task.Delay(25, ct);
                }
                throw new InvalidOperationException("Controlled parent lock admission was not observed.");
            }
        }
        Console.WriteLine("Restricted PostgreSQL lifecycle/work concurrency passed in both lock orders for suspension/deactivation; retained history and rejected-write atomicity verified.");
    }
    private static ServiceProvider Provider(string connection, string applicationName)
    {
        var services = new ServiceCollection(); services.AddLogging();
        services.AddSingleton(new PostgresConnectionFactory(new NpgsqlConnectionStringBuilder(connection) { ApplicationName = applicationName }.ConnectionString));
        services.AddSingleton<IClock, SystemClock>(); services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddSingleton<PostgresBackgroundJobStore>();
        services.AddSingleton<IBackgroundJobStore>(provider => provider.GetRequiredService<PostgresBackgroundJobStore>());
        services.AddSingleton<ICommandActorAuthorization, Admission>();
        services.AddSingleton<IWorkCommandContext, CommandContext>();
        var runtime = new RuntimeDescriptor(RuntimeMode.Production, "contract", "contract");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["STRATAAI_AUTH_ALLOW_SELF_REGISTRATION"] = "true", ["STRATAAI_AUTH_REQUIRE_VERIFIED_EMAIL"] = "false",
            ["STRATAAI_AUTH_RETRY_CURRENT_KEY"] = "contract", ["STRATAAI_AUTH_RETRY_KEYS"] = System.Text.Json.JsonSerializer.Serialize(
                new Dictionary<string, string> { ["contract"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)) }),
        }).Build();
        services.AddStrataAiIdentity(configuration, runtime);
        services.AddStrataAiOrganizations(runtime); services.AddStrataAiWorkManagement(runtime);
        return services.BuildServiceProvider();
    }
}
