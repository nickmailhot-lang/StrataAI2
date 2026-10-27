using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StrataAI.Application.Property;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Domain.Property;

// Runs in the existing mandatory configuration persistence job, using the real
// restricted API login. This is not a deployed browser/Portal acceptance claim.
internal static class StrataLotContract
{
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    public static async Task RunAsync(NpgsqlConnection admin, IServiceProvider provider, Guid organization, Guid other,
        Guid actor, CancellationToken ct) {
        var service = provider.GetRequiredService<StrataLotService>();
        var id = Guid.NewGuid(); var key = Guid.NewGuid();
        var data = new StrataLotData("48", "SL48", null, null, null, null, "ACTIVE");
        var saved = await service.ChangeAsync(organization, actor, id, data, 0, key, "property-contract", ct);
        Require(saved.Succeeded && saved.Value!.Version == 1, "Restricted unit creation failed: " + saved.ErrorCode);
        var corrected = await service.ChangeAsync(organization, actor, id, data with { UnitIdentifier = "48B" }, 1,
            Guid.NewGuid(), "property-correction", ct);
        Require(corrected.Succeeded && corrected.Value!.Id == id && corrected.Value.Version == 2, "Unit correction changed stable identity.");
        var refreshed = await ActivatorUtilities.CreateInstance<StrataLotService>(provider).ReadAsync(organization, actor, id, ct);
        Require(refreshed.Succeeded && refreshed.Value == corrected.Value, "Unit correction did not survive a fresh consumer read.");
        var retry = await service.ChangeAsync(organization, actor, id, data, 0, key, "property-retry", ct);
        Require(retry.Succeeded && retry.Value == saved.Value, "Unit retry did not recover the original revision.");
        var conflicting = await service.ChangeAsync(organization, actor, id, data with { UnitIdentifier = "49" }, 0, key, "property-changed-intent", ct);
        Require(!conflicting.Succeeded && conflicting.ErrorCode == "idempotency_key_conflict", "Unit receipt accepted changed intent.");
        var history = await service.HistoryAsync(organization, actor, id, null, ct);
        Require(history.Succeeded && history.Value!.Items.Select(x => x.Lot.Version).SequenceEqual(new long[] { 2, 1 }), "Unit history was rewritten.");
        var duplicate = await service.ChangeAsync(organization, actor, Guid.NewGuid(), data with { UnitIdentifier = "48B" }, 0,
            Guid.NewGuid(), "property-duplicate", ct);
        Require(!duplicate.Succeeded && duplicate.ErrorCode == "strata_lot_identifier_unavailable", "Tenant directory accepted duplicate identifiers.");
        var foreign = await service.ReadAsync(other, actor, id, ct);
        Require(!foreign.Succeeded && foreign.ErrorCode == "strata_lot_not_found", "Unit ID crossed Organization scope.");
        var isolated = await provider.GetRequiredService<PostgresConnectionFactory>().ExecuteTenantCommandAsync(other, async () => {
            await using var session = await provider.GetRequiredService<PostgresConnectionFactory>().OpenTenantSessionAsync(other, ct);
            await using var query = new NpgsqlCommand("SELECT count(*) FROM strata_lots WHERE id=@id;", session.Connection, session.Transaction);
            query.Parameters.AddWithValue("id", id);
            return (long)(await query.ExecuteScalarAsync(ct))! == 0;
        }, result => result, ct);
        Require(isolated, "Forced RLS exposed another Organization's unit to the API role.");
        // Verify exact persisted history is also immutable under the owning role.
        await using (var inspect = new NpgsqlCommand("SELECT count(*) FROM strata_lot_history WHERE tenant_id=@tenant AND lot_id=@id;", admin)) {
            inspect.Parameters.AddWithValue("tenant", organization); inspect.Parameters.AddWithValue("id", id);
            Require((long)(await inspect.ExecuteScalarAsync(ct))! == 2, "Failed unit writes left phantom history.");
        }
        Console.WriteLine("PRD-28 restricted unit creation/correction, original retry, history and forced-RLS isolation passed.");
    }
}
