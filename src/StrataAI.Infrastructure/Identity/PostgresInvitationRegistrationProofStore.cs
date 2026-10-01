using Npgsql;
using StrataAI.Application.Identity;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Identity;

internal sealed class PostgresInvitationRegistrationProofStore(PostgresConnectionFactory connections, IdentityPolicy policy)
    : IInvitationRegistrationProofStore
{
    public bool RequiresFinalCheck => true;

    public async Task<InvitationRegistrationProof?> PrepareAsync(string tokenHash, string emailNormalized, CancellationToken ct)
    {
        RequireScope();
        await using var root = await connections.OpenRoutingSessionAsync(ct);
        Guid organizationId; Guid invitationId;
        await using (var route = new NpgsqlCommand("""
            SELECT tenant_id,invitation_id FROM invitation_routes WHERE token_hash=@hash AND email_normalized=@email
              AND accepted_at IS NULL AND revoked_at IS NULL AND expires_at>clock_timestamp();
            """, root.Connection, root.Transaction))
        {
            route.Parameters.AddWithValue("hash", tokenHash); route.Parameters.AddWithValue("email", emailNormalized);
            await using var rows = await route.ExecuteReaderAsync(ct);
            if (!await rows.ReadAsync(ct)) return null;
            organizationId = rows.GetGuid(0); invitationId = rows.GetGuid(1);
        }
        InvitationRegistrationProof proof;
        try
        {
            await SetTenantAsync(root, organizationId, ct);
            await using (var parent = new NpgsqlCommand("SELECT id FROM organizations WHERE id=@org AND status='ACTIVE' FOR UPDATE;", root.Connection, root.Transaction))
            {
                parent.Parameters.AddWithValue("org", organizationId);
                if (await parent.ExecuteScalarAsync(ct) is null) return null;
            }
            Guid issuerId;
            await using (var hint = new NpgsqlCommand("SELECT created_by_user_id FROM invitations WHERE id=@id AND tenant_id=@org AND token_hash=@hash;", root.Connection, root.Transaction))
            {
                hint.Parameters.AddWithValue("id", invitationId); hint.Parameters.AddWithValue("org", organizationId); hint.Parameters.AddWithValue("hash", tokenHash);
                if (await hint.ExecuteScalarAsync(ct) is not Guid issuer) return null;
                issuerId = issuer;
            }
            // Match Organization command order: parent, membership, invitation, global accounts.
            await using (var member = new NpgsqlCommand("SELECT user_id FROM organization_members WHERE tenant_id=@org AND user_id=@issuer FOR SHARE;", root.Connection, root.Transaction))
            {
                member.Parameters.AddWithValue("org", organizationId); member.Parameters.AddWithValue("issuer", issuerId);
                if (await member.ExecuteScalarAsync(ct) is null) return null;
            }
            await using (var invitation = new NpgsqlCommand("SELECT id FROM invitations WHERE tenant_id=@org AND id=@id AND created_by_user_id=@issuer AND token_hash=@hash FOR SHARE;", root.Connection, root.Transaction))
            {
                invitation.Parameters.AddWithValue("org", organizationId); invitation.Parameters.AddWithValue("id", invitationId);
                invitation.Parameters.AddWithValue("issuer", issuerId); invitation.Parameters.AddWithValue("hash", tokenHash);
                if (await invitation.ExecuteScalarAsync(ct) is null) return null;
            }
            // Existing subjects and issuers are frozen in canonical PostgreSQL UUID order.
            // No identity lock precedes acquisition of the real tenant parent.
            await using (var users = new NpgsqlCommand("SELECT id FROM users WHERE id=@issuer OR email_normalized=@email ORDER BY id FOR UPDATE;", root.Connection, root.Transaction))
            {
                users.Parameters.AddWithValue("issuer", issuerId); users.Parameters.AddWithValue("email", emailNormalized);
                await using var rows = await users.ExecuteReaderAsync(ct);
                while (await rows.ReadAsync(ct)) { }
            }
            proof = new(organizationId, invitationId, issuerId, tokenHash);
        }
        finally { await SetTenantAsync(root, null, ct); }
        return await CheckAsync(proof, emailNormalized, ct) ? proof : null;
    }

    public async Task<bool> CheckAsync(InvitationRegistrationProof proof, string emailNormalized, CancellationToken ct)
    {
        RequireScope();
        await using var root = await connections.OpenRoutingSessionAsync(ct);
        try
        {
            await SetTenantAsync(root, proof.OrganizationId, ct);
            await using var check = new NpgsqlCommand("""
                SELECT EXISTS(
                  SELECT 1 FROM invitations i
                  JOIN organizations o ON o.id=i.tenant_id
                  JOIN organization_members m ON m.tenant_id=i.tenant_id AND m.user_id=i.created_by_user_id
                  JOIN users u ON u.id=i.created_by_user_id
                  WHERE i.tenant_id=@org AND i.id=@id AND i.created_by_user_id=@issuer AND i.token_hash=@hash
                    AND i.email_normalized=@email AND i.accepted_at IS NULL AND i.revoked_at IS NULL
                    AND i.expires_at>clock_timestamp() AND o.status='ACTIVE'
                    AND m.status='ACTIVE' AND m.role IN ('OWNER','ADMIN') AND u.status='ACTIVE'
                    AND (NOT @verified OR u.email_verified)
                    AND ((i.target_surface='INTERNAL' AND i.target_role IN ('OWNER','ADMIN','MEMBER')
                          AND (i.target_role<>'OWNER' OR m.role='OWNER'))
                      OR (i.target_surface='PORTAL' AND i.target_role IN ('OWNER','CO_OWNER','TENANT','OCCUPANT','AUTHORIZED_REPRESENTATIVE','OTHER')))
                );
                """, root.Connection, root.Transaction);
            check.Parameters.AddWithValue("org", proof.OrganizationId); check.Parameters.AddWithValue("id", proof.InvitationId);
            check.Parameters.AddWithValue("issuer", proof.IssuerId); check.Parameters.AddWithValue("hash", proof.TokenHash);
            check.Parameters.AddWithValue("email", emailNormalized); check.Parameters.AddWithValue("verified", policy.RequireVerifiedEmail);
            return await check.ExecuteScalarAsync(ct) is true;
        }
        finally { await SetTenantAsync(root, null, ct); }
    }

    private static async Task SetTenantAsync(RoutingDbSession root, Guid? organizationId, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("SELECT set_config('app.tenant_id',@tenant,true);", root.Connection, root.Transaction);
        command.Parameters.AddWithValue("tenant", organizationId?.ToString() ?? "");
        await command.ExecuteScalarAsync(ct);
    }
    private void RequireScope()
    {
        if (!connections.HasIdentityCommandScope) throw new InvalidOperationException("Invitation signup requires the owning identity transaction.");
    }
}
