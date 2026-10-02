using Npgsql;
using StrataAI.Application.Identity;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Identity;

internal sealed class PostgresAccountDeactivationOwnership(PostgresConnectionFactory connections,
    IdentityPolicy policy) : IAccountDeactivationOwnership
{
    public async Task<AccountOwnershipPlan> PrepareAsync(Guid userId, CancellationToken cancellationToken)
    {
        RequireScope();
        await using var root = await connections.OpenRoutingSessionAsync(cancellationToken);
        var ids = await ReadRoutesAsync(root, userId, cancellationToken);
        var complete = true;
        try
        {
            // Schema 021 guarantees route completeness; each route is still only
            // a hint. Freeze real parents in PostgreSQL UUID order before users.
            foreach (var id in ids)
            {
                await SetTenantAsync(root, id, cancellationToken);
                await using var parent = new NpgsqlCommand("SELECT id FROM organizations WHERE id=@id FOR UPDATE;", root.Connection, root.Transaction);
                parent.Parameters.AddWithValue("id", id);
                if (await parent.ExecuteScalarAsync(cancellationToken) is not Guid) { complete = false; break; }
                await using var member = new NpgsqlCommand("SELECT user_id FROM organization_members WHERE tenant_id=@id AND user_id=@user FOR SHARE;", root.Connection, root.Transaction);
                member.Parameters.AddWithValue("id", id); member.Parameters.AddWithValue("user", userId);
                await member.ExecuteScalarAsync(cancellationToken);
            }
        }
        finally { await SetTenantAsync(root, null, cancellationToken); }
        return new(userId, ids, complete);
    }

    public async Task<string?> CheckAsync(AccountOwnershipPlan plan, CancellationToken cancellationToken)
    {
        RequireScope();
        if (!plan.Complete) return "ownership_changed";
        await using var root = await connections.OpenRoutingSessionAsync(cancellationToken);
        // Called after the actor lock. A grant committed during admission may
        // add an unplanned parent: reject atomically, never acquire it late.
        if ((await ReadRoutesAsync(root, plan.UserId, cancellationToken)).Except(plan.OrganizationIds).Any())
            return "ownership_changed";
        var ownerSets = new List<IReadOnlyList<Guid>>();
        try
        {
            foreach (var id in plan.OrganizationIds)
            {
                await SetTenantAsync(root, id, cancellationToken);
                await using var parent = new NpgsqlCommand("SELECT status FROM organizations WHERE id=@id;", root.Connection, root.Transaction);
                parent.Parameters.AddWithValue("id", id);
                if (await parent.ExecuteScalarAsync(cancellationToken) is not string status) return "ownership_changed";
                if (status == "DELETING") continue;
                var owners = new List<Guid>();
                await using var members = new NpgsqlCommand("""
                    SELECT user_id FROM organization_members
                    WHERE tenant_id=@id AND role='OWNER' AND status='ACTIVE' ORDER BY user_id FOR SHARE;
                    """, root.Connection, root.Transaction);
                members.Parameters.AddWithValue("id", id);
                await using (var rows = await members.ExecuteReaderAsync(cancellationToken))
                    while (await rows.ReadAsync(cancellationToken)) owners.Add(rows.GetGuid(0));
                // Recheck canonical membership rather than relying on the route's role.
                if (owners.Contains(plan.UserId)) ownerSets.Add(owners);
            }
        }
        finally { await SetTenantAsync(root, null, cancellationToken); }
        var alternatives = ownerSets.SelectMany(owners => owners).Where(id => id != plan.UserId).Distinct().ToArray();
        var active = new HashSet<Guid>();
        if (alternatives.Length > 0)
        {
            // Freeze accounts globally in a stable order before checking status,
            // including after a password reset/verification/lifecycle lock wait.
            await using var users = new NpgsqlCommand("SELECT id,status,email_verified FROM users WHERE id=ANY(@ids) ORDER BY id FOR SHARE;", root.Connection, root.Transaction);
            users.Parameters.AddWithValue("ids", alternatives);
            await using var rows = await users.ExecuteReaderAsync(cancellationToken);
            while (await rows.ReadAsync(cancellationToken))
                if (rows.GetString(1) == "ACTIVE" && (!policy.RequireVerifiedEmail || rows.GetBoolean(2))) active.Add(rows.GetGuid(0));
        }
        return ownerSets.Any(owners => !owners.Any(id => id != plan.UserId && active.Contains(id)))
            ? "organization_owner_required" : null;
    }

    private static async Task<Guid[]> ReadRoutesAsync(RoutingDbSession root, Guid userId, CancellationToken cancellationToken)
    {
        await root.SetLookupAsync(RoutingLookup.OrganizationUser, userId.ToString(), cancellationToken);
        var ids = new List<Guid>();
        await using var routes = new NpgsqlCommand("SELECT tenant_id FROM user_organization_access WHERE user_id=@user AND role='OWNER' AND status='ACTIVE' ORDER BY tenant_id;", root.Connection, root.Transaction);
        routes.Parameters.AddWithValue("user", userId);
        await using var rows = await routes.ExecuteReaderAsync(cancellationToken);
        while (await rows.ReadAsync(cancellationToken)) ids.Add(rows.GetGuid(0));
        return ids.ToArray();
    }
    private static async Task SetTenantAsync(RoutingDbSession root, Guid? id, CancellationToken cancellationToken)
    {
        await using var scope = new NpgsqlCommand("SELECT set_config('app.tenant_id',@tenant,true);", root.Connection, root.Transaction);
        scope.Parameters.AddWithValue("tenant", id?.ToString() ?? "");
        await scope.ExecuteScalarAsync(cancellationToken);
    }
    private void RequireScope()
    {
        if (!connections.HasIdentityCommandScope) throw new InvalidOperationException("Ownership admission requires an owning identity transaction.");
    }
}
