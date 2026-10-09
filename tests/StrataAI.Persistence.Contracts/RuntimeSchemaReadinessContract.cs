using Npgsql;
using StrataAI.Application.Runtime;
using StrataAI.Infrastructure.Persistence;

// ARCH-04/11: prove readiness against the real migration ledger using restricted API and Worker logins.
internal static class RuntimeSchemaReadinessContract
{
    public static async Task RunAsync(NpgsqlConnection admin, string apiConnection, string workerConnection, CancellationToken ct)
    {
        await using var api = new PostgresConnectionFactory(apiConnection);
        await using var worker = new PostgresConnectionFactory(workerConnection);
        var factories = new[] { api, worker };
        foreach (var factory in factories)
        { await using var complete = await factory.OpenConnectionAsync(ct); }
        // Exercise every applied ledger entry independently. A selected list
        // can miss older required migrations while the complete-ledger path passes.
        var versions = new List<string>();
        await using (var ledger = new NpgsqlCommand("SELECT version FROM public.schema_migrations ORDER BY version", admin))
        await using (var rows = await ledger.ExecuteReaderAsync(ct))
        {
            while (await rows.ReadAsync(ct)) versions.Add(rows.GetString(0));
        }
        if (versions.Count == 0) throw new InvalidOperationException("Runtime readiness ledger fixture was empty.");
        foreach (var version in versions)
        {
            var hidden = $"contract_missing_{Guid.NewGuid():N}";
            await using var hide = new NpgsqlCommand("UPDATE public.schema_migrations SET version=@hidden WHERE version=@version", admin);
            hide.Parameters.AddWithValue("hidden", hidden); hide.Parameters.AddWithValue("version", version);
            if (await hide.ExecuteNonQueryAsync(ct) != 1) throw new InvalidOperationException("Required migration fixture was absent.");
            try
            {
                foreach (var factory in factories)
                {
                    var rejected = false;
                    try { await using var connection = await factory.OpenConnectionAsync(ct); }
                    catch (RuntimeDatabaseSchemaException) { rejected = true; }
                    if (!rejected) throw new InvalidOperationException("Restricted runtime admitted an incomplete migration ledger.");
                }
            }
            finally
            {
                await using var restore = new NpgsqlCommand("UPDATE public.schema_migrations SET version=@version WHERE version=@hidden", admin);
                restore.Parameters.AddWithValue("hidden", hidden); restore.Parameters.AddWithValue("version", version);
                if (await restore.ExecuteNonQueryAsync(CancellationToken.None) != 1)
                    throw new InvalidOperationException("Migration readiness fixture could not restore the ledger.");
            }
            foreach (var factory in factories)
            { await using var recovered = await factory.OpenConnectionAsync(ct); }
        }
        Console.WriteLine("Restricted API/Worker schema readiness: complete ledger accepted, missing required migrations refused, restored ledger recovered.");
        Console.WriteLine($"Runtime readiness: {versions.Count} migrated ledger entries individually rejected/restored for API and Worker.");
    }
}
