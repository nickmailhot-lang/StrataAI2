using System.Text.Json;
using Npgsql;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.Identity;
using StrataAI.Application.Onboarding;
using StrataAI.Infrastructure.BackgroundJobs;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Onboarding;

public sealed class PostgresInvitationMailPublisher(PostgresConnectionFactory connections,
    PostgresBackgroundJobStore jobs, IInvitationDeliveryTokenSigner signer, IdentityDeliveryOptions options)
    : IInvitationMailPublisher
{
    public InvitationDeliveryToken CreateToken(Guid organizationId, Guid invitationId) =>
        new(signer.DeriveInvitation(organizationId, invitationId, signer.CurrentKeyId), signer.CurrentKeyId);

    public async Task PublishAsync(InvitationRecord invitation, string keyId, string correlationId, CancellationToken cancellationToken)
    {
        if (!connections.HasCommandScope(invitation.OrganizationId))
            throw new InvalidOperationException("Invitation mail publication requires the authorized Organization transaction.");
        await using var session = await connections.OpenTenantSessionAsync(invitation.OrganizationId, cancellationToken);
        var jobId = Guid.NewGuid();
        var job = new NewBackgroundJob(jobId, invitation.OrganizationId, InvitationEmailHandler.Type,
            $"invitation-email/{invitation.Id:N}", invitation.CreatedByUserId, InvitationEmailHandler.Identity,
            correlationId, JsonSerializer.Serialize(new { invitationId = invitation.Id }));
        if (!await jobs.PublishAsync(session, job, cancellationToken))
            throw new InvalidOperationException("Invitation mail intent was already published.");
        await using var command = new NpgsqlCommand("""
            INSERT INTO invitation_mail_intents(job_id,tenant_id,invitation_id,issuer_id,recipient_email,
                target_surface,target_role,expires_at,key_id,sender_address,public_origin,provider_account,template_version,target_board_id,target_board_role)
            SELECT @job,tenant_id,id,created_by_user_id,invited_email,target_surface,target_role,expires_at,
                @key,@sender,@origin,@account,1,target_board_id,target_board_role FROM invitations
            WHERE id=@invitation AND tenant_id=@tenant AND created_by_user_id=@issuer AND token_hash=@hash
                AND target_board_id IS NOT DISTINCT FROM @board AND target_board_role IS NOT DISTINCT FROM @board_role
                AND accepted_at IS NULL AND revoked_at IS NULL AND expires_at>clock_timestamp();
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("job", jobId);
        command.Parameters.AddWithValue("invitation", invitation.Id);
        command.Parameters.AddWithValue("tenant", invitation.OrganizationId);
        command.Parameters.AddWithValue("issuer", invitation.CreatedByUserId);
        command.Parameters.AddWithValue("hash", invitation.TokenHash);
        command.Parameters.AddWithValue("board", NpgsqlTypes.NpgsqlDbType.Uuid,
            (object?)invitation.BoardTarget?.BoardId ?? DBNull.Value);
        command.Parameters.AddWithValue("board_role", NpgsqlTypes.NpgsqlDbType.Text,
            invitation.BoardTarget is { } target ? target.Role switch {
                StrataAI.Application.WorkManagement.BoardRole.Admin => "ADMIN",
                StrataAI.Application.WorkManagement.BoardRole.Member => "MEMBER",
                _ => throw new ArgumentOutOfRangeException(nameof(invitation)),
            } : DBNull.Value);
        command.Parameters.AddWithValue("key", keyId);
        command.Parameters.AddWithValue("sender", options.SenderAddress);
        command.Parameters.AddWithValue("origin", options.PublicOrigin);
        command.Parameters.AddWithValue("account", options.ProviderAccount);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("Canonical invitation mail snapshot was not admitted.");
        // Borrowed session: only the Organization unit of work owns commit.
    }
}
