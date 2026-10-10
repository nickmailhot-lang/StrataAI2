using System.Text.Json;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.Runtime;
using StrataAI.Domain.Organizations;
using StrataAI.Infrastructure.Organizations;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Infrastructure.WorkManagement;

// Real restricted PostgreSQL sessions, commands and rollback. HTTP/session
// admission is synthetic here; this does not establish deployed HTTP acceptance.
internal static class OrganizationConfigurationContract
{
    private sealed class Admission : ICommandActorAuthorization
    {
        public bool Denied { get; set; }
        public Task<bool> VerifyAsync(Guid actorId, CancellationToken cancellationToken = default) => Task.FromResult(!Denied);
    }
    private sealed class Clock : IClock { public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow; }
    private static void Require(bool condition, string rule)
    {
        if (!condition) throw new InvalidOperationException(rule);
    }
    private static bool Same<T>(T left, T right) => JsonSerializer.Serialize(left) == JsonSerializer.Serialize(right);

    private static async Task VerifyIntakePaginationAsync(NpgsqlConnection admin, OrganizationConfigurationService service,
        Guid tenant, Guid actor, Guid foreignBoard, CancellationToken ct)
    {
        var board = Guid.NewGuid();
        await using (var seed = new NpgsqlCommand("""
            SELECT set_config('app.tenant_id',@tenant::text,false);
            INSERT INTO boards(id,tenant_id,name,created_at,updated_at)
            VALUES(@board,@tenant,'Bounded intake contract',now(),now());
            INSERT INTO board_lists(id,tenant_id,board_id,name,rank,created_at,updated_at)
            SELECT gen_random_uuid(),@tenant,@board,'Intake contract List '||item,lpad(item::text,30,'0'),now(),now()
            FROM generate_series(1,53) AS item;
            SELECT set_config('app.tenant_id','',false);
            """, admin))
        {
            seed.Parameters.AddWithValue("tenant", tenant); seed.Parameters.AddWithValue("board", board);
            await seed.ExecuteNonQueryAsync(ct);
        }
        var first = await service.ReadIntakeListsAsync(tenant, actor, board, null, ct);
        Require(first.Succeeded && first.Value!.Items.Count == 50 && first.Value.NextAfterRank == first.Value.Items[^1].Rank,
            "Restricted intake first page did not retain the bounded exclusive cursor.");
        var second = await service.ReadIntakeListsAsync(tenant, actor, board, first.Value!.NextAfterRank, ct);
        Require(second.Succeeded && second.Value!.Items.Count == 3 && second.Value.NextAfterRank is null
            && first.Value.Items.Concat(second.Value.Items).Select(row => row.Id).Distinct().Count() == 53
            && second.Value.Items.All(row => string.CompareOrdinal(row.Rank, first.Value.NextAfterRank) > 0),
            "Restricted intake continuation duplicated, skipped or exposed an invalid boundary.");
        var exhausted = await service.ReadIntakeListsAsync(tenant, actor, board, second.Value!.Items[^1].Rank, ct);
        Require(exhausted.Succeeded && exhausted.Value!.Items.Count == 0 && exhausted.Value.NextAfterRank is null,
            "Restricted intake terminal page was not empty.");
        var foreign = await service.ReadIntakeListsAsync(tenant, actor, foreignBoard, null, ct);
        Require(!foreign.Succeeded && foreign.Value is null && foreign.ErrorCode == "configuration_intake_unavailable",
            "Restricted intake disclosed another tenant Board.");
        await using (var archive = new NpgsqlCommand("""
            SELECT set_config('app.tenant_id',@tenant::text,false);
            UPDATE boards SET lifecycle_state='ARCHIVED',version=version+1,updated_at=now() WHERE tenant_id=@tenant AND id=@board;
            SELECT set_config('app.tenant_id','',false);
            """, admin))
        {
            archive.Parameters.AddWithValue("tenant", tenant); archive.Parameters.AddWithValue("board", board);
            await archive.ExecuteNonQueryAsync(ct);
        }
        var archived = await service.ReadIntakeListsAsync(tenant, actor, board, null, ct);
        Require(!archived.Succeeded && archived.Value is null && archived.ErrorCode == "configuration_intake_unavailable",
            "Restricted intake returned an archived Board.");
    }

    public static async Task RunAsync(NpgsqlConnection admin, string apiConnection, CancellationToken ct)
    {
        await VerifyNamespaceAsync(apiConnection, ct);
        var tenant = Guid.NewGuid(); var other = Guid.NewGuid(); var actor = Guid.NewGuid(); var foreignBoard = Guid.NewGuid();
        await using (var seed = new NpgsqlCommand("""
            INSERT INTO users(id,email,email_normalized,display_name,status,password_hash,email_verified,created_at,updated_at)
            VALUES(@actor,@email,upper(@email),'Configuration contract owner','ACTIVE','unusable-ci-hash',true,now(),now());
            INSERT INTO organizations(id,name,owner_user_id,status,version,organization_type,created_at,updated_at)
            VALUES(@tenant,'Configuration contract',@actor,'ACTIVE',1,'STRATA',now(),now()),
                  (@other,'Other configuration contract',@actor,'ACTIVE',1,'STRATA',now(),now());
            INSERT INTO organization_members(id,tenant_id,user_id,role,status,created_at,updated_at)
            VALUES(@member,@tenant,@actor,'OWNER','ACTIVE',now(),now()),(@other_member,@other,@actor,'OWNER','ACTIVE',now(),now());
            SELECT set_config('app.tenant_id',@other::text,false);
            INSERT INTO boards(id,tenant_id,name,created_at,updated_at) VALUES(@foreign_board,@other,'Foreign intake contract',now(),now());
            SELECT set_config('app.tenant_id','',false);
            """, admin))
        {
            seed.Parameters.AddWithValue("actor", actor); seed.Parameters.AddWithValue("email", $"config-contract-{actor:N}@example.test");
            seed.Parameters.AddWithValue("tenant", tenant); seed.Parameters.AddWithValue("other", other);
            seed.Parameters.AddWithValue("foreign_board", foreignBoard);
            seed.Parameters.AddWithValue("member", Guid.NewGuid()); seed.Parameters.AddWithValue("other_member", Guid.NewGuid());
            await seed.ExecuteNonQueryAsync(ct);
        }
        var admission = new Admission(); var clock = new Clock();
        var services = new ServiceCollection(); services.AddLogging();
        services.AddSingleton(new PostgresConnectionFactory(apiConnection)); services.AddSingleton<IClock>(clock);
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddSingleton<ICommandActorAuthorization>(admission);
        services.AddSingleton(new IdentityPolicy(true, false, 12, TimeSpan.FromHours(1), TimeSpan.FromHours(1)));
        var runtime = new RuntimeDescriptor(RuntimeMode.Production, "contract", "contract");
        services.AddStrataAiOrganizations(runtime); services.AddStrataAiWorkManagement(runtime);
        await using var provider = services.BuildServiceProvider();
        var service = ActivatorUtilities.CreateInstance<OrganizationConfigurationService>(provider);
        var store = provider.GetRequiredService<IOrganizationConfigurationStore>();
        var unit = provider.GetRequiredService<IOrganizationUnitOfWork>();
        var organizations = provider.GetRequiredService<IOrganizationStore>();
        await VerifyIntakePaginationAsync(admin, service, tenant, actor, foreignBoard, ct);
        var empty = await service.ReadAsync(tenant, actor, ct);
        Require(empty.Succeeded && empty.Value is { Version: 0, Revision: null }, "Configuration empty state was fabricated.");
        var substituted = await service.ChangeAsync(tenant, actor,
            new("Reviewed legal name", "CA-BC", "UTC", IntakeBoardId: foreignBoard), 0, Guid.NewGuid(), "configuration-foreign-intake", ct);
        Require(!substituted.Succeeded && substituted.ErrorCode == "configuration_intake_unavailable",
            "Configuration foreign intake did not return a stable tenant-safe refusal.");
        var intent = new OrganizationConfigurationData("Reviewed legal name", "CA-BC", "America/Vancouver",
            CorporationIdentifier: "REG-EXAMPLE", ManagementCompanyName: "Original manager",
            EmergencyContacts: [new("Reviewed emergency contact", Phone: "reviewed-telephone")],
            JurisdictionPolicies: [new("reviewed-policy", "Reviewed value", "Reviewed source", "Reviewed notes")]);
        var key = Guid.NewGuid();
        var first = await service.ChangeAsync(tenant, actor, intent, 0, key, "configuration-first", ct);
        Require(first.Succeeded && first.Value is { Version: 1 }, "Configuration first command failed.");
        var second = await service.ChangeAsync(tenant, actor, intent with
        {
            Jurisdiction = "CA-ON", Timezone = "America/Toronto", ManagementCompanyName = "New manager",
        }, 1, Guid.NewGuid(), "configuration-second", ct);
        Require(second.Succeeded && second.Value is { Version: 2 }, "Configuration later command failed.");
        var recovered = await service.ChangeAsync(tenant, actor, intent, 0, key, "configuration-recovery", ct);
        Require(recovered.Succeeded && Same(recovered.Value, first.Value), "Configuration original acknowledgment was overwritten.");
        var history = await service.ReadHistoryAsync(tenant, actor, null, ct);
        Require(history.Succeeded && history.Value!.Items.Select(row => row.Version).SequenceEqual([2L, 1L])
            && Same(history.Value.Items[1], first.Value), "Configuration historical meaning was lost.");
        var conflicts = await service.ChangeAsync(tenant, actor, intent with { LegalName = "Changed intent" }, 0, key, "configuration-conflict", ct);
        Require(!conflicts.Succeeded && conflicts.ErrorCode == "idempotency_key_conflict", "Configuration changed intent was accepted.");
        var stale = await service.ChangeAsync(tenant, actor, intent, 0, Guid.NewGuid(), "configuration-stale", ct);
        Require(!stale.Succeeded && stale.ErrorCode == "version_conflict", "Configuration stale command was accepted.");
        var registration = await service.ChangeAsync(other, actor, second.Value!.Configuration with
        {
            CorporationIdentifier = " reg-example ", Jurisdiction = "ca-on",
        }, 0, Guid.NewGuid(), "configuration-identifier-conflict", ct);
        Require(!registration.Succeeded && registration.ErrorCode == "configuration_identifier_unavailable",
            "Configuration protected registration conflict was missed.");
        clock.UtcNow = clock.UtcNow.AddHours(24);
        var expired = await service.ChangeAsync(tenant, actor, intent, 0, key, "configuration-expired", ct);
        Require(!expired.Succeeded && expired.ErrorCode == "idempotency_key_expired", "Configuration expired key was reused.");
        var tentativeKey = Guid.NewGuid(); OrganizationConfigurationRecord? tentative = null;
        var failed = await unit.ExecuteAsync(tenant, actor, null, false, async () =>
        {
            var parent = (await organizations.FindOrganizationAsync(tenant, ct))!;
            tentative = new(tenant, 3, second.Value.Configuration with { CorporationIdentifier = "ROLLED-BACK-REGISTRATION" },
                parent.Name, parent.Type, parent.Version, actor, Guid.NewGuid(), "configuration-final-fence", first.Value!.CreatedAt, clock.UtcNow);
            var written = await store.WriteAsync(2, tentative,
                new(actor, tentativeKey, new string('B', 64), tentative, clock.UtcNow.AddHours(24)), ct);
            Require(written == OrganizationConfigurationWriteResult.Written, "Configuration tentative source did not execute.");
            admission.Denied = true;
            return OrganizationOperation<bool>.Success(true);
        }, ct);
        admission.Denied = false;
        Require(!failed.Succeeded && failed.ErrorCode == "session_unavailable", "Configuration final actor fence was ignored.");
        var checkedState = await unit.ExecuteAsync(tenant, actor, null, false, async () =>
        {
            Require(Same(await store.ReadAsync(tenant, ct), second.Value), "Configuration rollback replaced current state.");
            Require((await store.ReadHistoryAsync(tenant, null, ct)).Count == 2, "Configuration rollback retained history.");
            var events = await store.ReadEventsAsync(tenant, 0, ct);
            Require(events.Count == 2 && events[0].EventId == first.Value!.EventId && events[0].CreatedAt == first.Value.UpdatedAt
                && events[1].EventId == second.Value.EventId, "Configuration rollback or event source lost identity/time.");
            Require(await store.ReadReceiptAsync(tenant, actor, tentativeKey, ct) is null, "Configuration rollback retained receipt.");
            return OrganizationOperation<bool>.Success(true);
        }, ct);
        Require(checkedState.Succeeded, "Configuration rollback could not be inspected.");
        var reclaimed = await service.ChangeAsync(other, actor, tentative!.Configuration, 0, Guid.NewGuid(), "configuration-reclaimed", ct);
        Require(reclaimed.Succeeded, "Configuration rollback retained registration reservation.");
        await using (var demote = new NpgsqlCommand("UPDATE organization_members SET role='MEMBER',version=version+1,updated_at=clock_timestamp() WHERE tenant_id=@tenant AND user_id=@actor;", admin))
        {
            demote.Parameters.AddWithValue("tenant", tenant); demote.Parameters.AddWithValue("actor", actor);
            await demote.ExecuteNonQueryAsync(ct);
        }
        Require((await service.ReadAsync(tenant, actor, ct)).ErrorCode == "organization_not_found"
            && (await service.ReadHistoryAsync(tenant, actor, null, ct)).ErrorCode == "organization_not_found"
            && (await service.ChangeAsync(tenant, actor, intent, 0, key, "configuration-withdrawn", ct)).ErrorCode == "organization_not_found",
            "Configuration withdrawal disclosed private state/history/receipt.");
        try { await store.ReadAsync(tenant, ct); throw new InvalidOperationException("Configuration standalone read was admitted."); }
        catch (InvalidOperationException error) when (error.Message == "Organization configuration requires an owning transaction.") { }
        await using var audit = new NpgsqlCommand("SELECT count(*) FROM audit_events WHERE tenant_id=@tenant AND event_type='ORGANIZATION_CONFIGURATION_CHANGED';", admin);
        audit.Parameters.AddWithValue("tenant", tenant);
        Require(Convert.ToInt64(await audit.ExecuteScalarAsync(ct)) == 2, "Configuration final fence retained an audit effect.");
        Console.WriteLine("Restricted Organization configuration persistence, private replay, historical changes and final-fence rollback passed.");
    }

    private static async Task VerifyNamespaceAsync(string apiConnection, CancellationToken ct)
    {
        var lower = new StringBuilder(); var upper = new StringBuilder();
        for (var value = 1; value <= 0x10ffff; value++)
        {
            if (!Rune.IsValid(value)) continue;
            var original = new Rune(value); var folded = Rune.ToUpperInvariant(original);
            Require(original.ToString().ToUpperInvariant() == folded.ToString(),
                "Configuration invariant string/scalar namespace semantics changed.");
            if (original == folded) continue;
            lower.Append(original.ToString()); upper.Append(folded.ToString());
        }
        await using var session = new NpgsqlConnection(apiConnection); await session.OpenAsync(ct);
        await using var command = new NpgsqlCommand("SELECT organization_configuration_namespace(@input);", session);
        command.Parameters.AddWithValue("input", lower.ToString());
        Require(await command.ExecuteScalarAsync(ct) is string allPairs && allPairs == upper.ToString(),
            "Configuration SQL namespace disagrees with invariant runtime case conversion.");
        string[] cases = ["ascii", "\u0131", "\u0130", "\u00df", "\u1e9e", "\u03c3", "\u03c2", "\u212a", "\u00e9", "\u00e5",
            "\u0085ascii\u0085", "\u000bascii\u000b", "\u00a0ascii\u00a0", "\u2000ascii\u2000", "\u3000ascii\u3000"];
        foreach (var value in cases)
        {
            command.Parameters["input"].Value = value;
            Require(await command.ExecuteScalarAsync(ct) is string normalized && normalized == value.Trim().ToUpperInvariant(),
                "Configuration namespace whitespace or unmapped scalar semantics disagree.");
        }
        Console.WriteLine("Restricted configuration namespace invariant scalar/whitespace parity passed.");
    }
}
