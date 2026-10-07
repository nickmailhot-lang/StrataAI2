using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.Runtime;
using StrataAI.Infrastructure.Organizations;
using StrataAI.Infrastructure.Persistence;

// WS-FR-001/002: actual restricted adapter persistence. HTTP actor admission
// is covered by the native settings actor scenarios, not this store contract.
internal static class OrganizationCreationTimestampContract
{
    public static async Task RunAsync(NpgsqlConnection admin, string apiConnection, CancellationToken ct)
    {
        var actor = Guid.NewGuid(); var tenants = new List<Guid>();
        await using (var seed = new NpgsqlCommand("""
            INSERT INTO users(id,email,email_normalized,display_name,status,password_hash,created_at,updated_at)
            VALUES(@actor,@email,upper(@email),'Creation timestamp fixture','ACTIVE','unused-contract-hash',now(),now());
            """, admin))
        {
            seed.Parameters.AddWithValue("actor", actor);
            seed.Parameters.AddWithValue("email", $"creation-timestamp-{actor:N}@example.test");
            await seed.ExecuteNonQueryAsync(ct);
        }
        try
        {
            var services = new ServiceCollection();
            services.AddSingleton(new PostgresConnectionFactory(apiConnection));
            services.AddSingleton(new IdentityPolicy(false, true, 12, TimeSpan.FromHours(12), TimeSpan.FromMinutes(30)));
            services.AddStrataAiOrganizations(new(RuntimeMode.Production, "contract", "contract"));
            await using var provider = services.BuildServiceProvider();
            var store = provider.GetRequiredService<IOrganizationStore>();
            foreach (var fraction in new[] { "1234561", "1234567", "9999999" })
            {
                var tenant = Guid.NewGuid(); tenants.Add(tenant);
                var requested = DateTimeOffset.Parse($"2026-10-07T12:34:56.{fraction}+00:00",
                    System.Globalization.CultureInfo.InvariantCulture);
                var acknowledgment = await store.CreateOrganizationAsync(actor, tenant,
                    "Canonical creation", "Persisted description", requested, ct);
                var persisted = await store.FindOrganizationAsync(tenant, ct);
                if (persisted is null || acknowledgment != persisted)
                    throw new InvalidOperationException("Organization creation acknowledgment differs from its stored record.");
                if (acknowledgment.CreatedAt == requested || acknowledgment.UpdatedAt != acknowledgment.CreatedAt)
                    throw new InvalidOperationException("The fixture did not exercise stored sub-microsecond precision.");
                var membership = await store.FindMembershipAsync(tenant, actor, ct);
                if (membership is not { Active: true, Role: OrganizationRole.Owner, Version: 1 }
                    || membership.CreatedAt != acknowledgment.CreatedAt || membership.UpdatedAt != acknowledgment.UpdatedAt)
                    throw new InvalidOperationException("Creation lost its canonical initial Owner membership.");
            }
            Console.WriteLine("Organization creation timestamp contract passed three restricted persisted-record comparisons.");
        }
        finally
        {
            await using var cleanup = new NpgsqlCommand("""
                DELETE FROM organization_members WHERE tenant_id=ANY(@tenants);
                DELETE FROM organizations WHERE id=ANY(@tenants);
                DELETE FROM users WHERE id=@actor;
                """, admin);
            cleanup.Parameters.AddWithValue("tenants", tenants.ToArray()); cleanup.Parameters.AddWithValue("actor", actor);
            await cleanup.ExecuteNonQueryAsync(ct);
        }
    }
}
