using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StrataAI.Application.Identity;
using StrataAI.Infrastructure.Organizations;
using StrataAI.Infrastructure.Persistence;

// PRD-03-TC-04/05/09/10: actual sealed Worker source under the restricted API
// login. Admission seam exercises final-session refusal, not an HTTP cookie.
internal static class OrganizationLifecycleReplayContract
{
    public static async Task RunAsync(NpgsqlConnection admin, string apiConnection, Guid tenant, Guid sourceActor,
        Guid eventId, bool ready, CancellationToken ct)
    {
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        var member = Guid.NewGuid();
        await using (var seed = new NpgsqlCommand("""
            INSERT INTO users(id,email,email_normalized,display_name,status,password_hash,created_at,updated_at)
             VALUES(@member,@email,upper(@email),'Lifecycle Member','ACTIVE','unused-contract-hash',now(),now());
            INSERT INTO organization_members(id,tenant_id,user_id,role,status)
             VALUES(gen_random_uuid(),@tenant,@member,'MEMBER','ACTIVE');
            """, admin))
        {
            seed.Parameters.AddWithValue("member", member); seed.Parameters.AddWithValue("tenant", tenant);
            seed.Parameters.AddWithValue("email", $"lifecycle-{member:N}@example.test"); await seed.ExecuteNonQueryAsync(ct);
        }
        await using var connections = new PostgresConnectionFactory(apiConnection);
        var admission = new Admission();
        var reader = new PostgresOrganizationLifecycleEventReader(connections, admission,
            NullLogger<PostgresOrganizationLifecycleEventReader>.Instance);
        foreach (var actor in new[] { sourceActor, member })
        {
            var result = await reader.ReadAsync(tenant, actor, ct);
            Require(result.Succeeded && result.Value!.State == (ready ? "COMPLETED" : "PENDING"), "Current Internal membership did not receive the canonical lifecycle state.");
            Require(result.Value!.Events.Count == (ready ? 1 : 0), "Unready terminal source was disclosed or ready source was missing.");
            if (ready)
            {
                var row = result.Value.Events[0];
                Require(row.EventId == eventId && row.EventType == "ORGANIZATION_DELETED" && row.ActorId == sourceActor
                    && row.OrganizationId == tenant && row.EntityId == tenant && row.EntityType == "Organization"
                    && row.Version >= 3 && row.Metadata.Count == 0 && row.BoardId is null,
                    "Terminal replay changed canonical attribution or disclosed content.");
            }
        }
        Require((await reader.ReadAsync(Guid.NewGuid(), member, ct)).ErrorCode == "organization_not_found", "Lifecycle replay widened across tenants.");
        Require((await reader.ReadAsync(tenant, Guid.NewGuid(), ct)).ErrorCode == "organization_not_found", "Lifecycle replay admitted an unrelated account.");
        admission.FailAt = 2; admission.Calls = 0;
        Require((await reader.ReadAsync(tenant, member, ct)).ErrorCode == "session_unavailable" && admission.Calls == 2,
            "Terminal replay retained its page after final session refusal.");
        admission.FailAt = 0;
        await using (var remove = new NpgsqlCommand("UPDATE organization_members SET status='REMOVED',version=version+1,updated_at=clock_timestamp() WHERE tenant_id=@tenant AND user_id=@member;", admin))
        {
            remove.Parameters.AddWithValue("tenant", tenant); remove.Parameters.AddWithValue("member", member); await remove.ExecuteNonQueryAsync(ct);
        }
        Require((await reader.ReadAsync(tenant, member, ct)).ErrorCode == "organization_not_found", "Removed membership received terminal history.");
        Console.WriteLine($"Organization lifecycle replay: restricted current Member/Owner admission, {(ready ? "ready canonical event" : "unready source withholding")}, tenant/outsider denial and final session refusal passed.");
    }
    private sealed class Admission : ICommandActorAuthorization
    {
        public int Calls { get; set; }
        public int FailAt { get; set; }
        public Task<bool> VerifyAsync(Guid actorId, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); Calls++; return Task.FromResult(FailAt == 0 || Calls != FailAt); }
    }
}
