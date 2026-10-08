using Npgsql;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed class PostgresCardWatchRecipientStore(PostgresConnectionFactory connections) : ICardWatchRecipientStore
{
    public async Task<IReadOnlyList<Guid>> LockRecipientsAsync(CardWatchActivity scope, bool requireVerifiedEmail, CancellationToken ct)
    {
        if (scope.OrganizationId == Guid.Empty || scope.BoardId == Guid.Empty || scope.ListId == Guid.Empty || scope.CardId == Guid.Empty ||
            !connections.HasCommandScope(scope.OrganizationId))
            throw new InvalidOperationException("Watch recipients require the originating Work transaction.");
        ct.ThrowIfCancellationRequested();
        await using var session = await connections.OpenTenantSessionAsync(scope.OrganizationId, ct);
        await using var query = new NpgsqlCommand("""
            SELECT u.id FROM users u
            JOIN organization_members m ON m.user_id=u.id AND m.tenant_id=@tenant
            JOIN organizations o ON o.id=m.tenant_id AND o.status='ACTIVE'
            JOIN boards b ON b.tenant_id=o.id AND b.id=@board AND b.lifecycle_state='ACTIVE'
            JOIN board_lists l ON l.tenant_id=b.tenant_id AND l.board_id=b.id AND l.id=@list AND l.lifecycle_state='ACTIVE'
            JOIN cards c ON c.tenant_id=l.tenant_id AND c.board_id=b.id AND c.list_id=l.id AND c.id=@card
            WHERE u.status='ACTIVE' AND m.status='ACTIVE' AND (NOT @verified OR u.email_verified)
              AND c.lifecycle_state IN ('ACTIVE','ARCHIVED') AND b.visibility IN ('PRIVATE','ORGANIZATION','PUBLIC')
              AND (b.visibility<>'PRIVATE' OR m.role IN ('OWNER','ADMIN') OR EXISTS(
                SELECT 1 FROM board_members bm WHERE bm.tenant_id=@tenant AND bm.board_id=@board AND bm.user_id=u.id AND bm.status='ACTIVE'))
              AND EXISTS(SELECT 1 FROM watch_subscriptions w WHERE w.tenant_id=@tenant AND w.user_id=u.id AND w.watching
                AND ((w.entity_type='CARD' AND w.entity_id=@card) OR (w.entity_type='LIST' AND w.entity_id=@list)
                  OR (w.entity_type='BOARD' AND w.entity_id=@board)))
            ORDER BY u.id FOR SHARE OF u,m;
            """, session.Connection, session.Transaction);
        query.Parameters.AddWithValue("tenant", scope.OrganizationId); query.Parameters.AddWithValue("board", scope.BoardId);
        query.Parameters.AddWithValue("list", scope.ListId); query.Parameters.AddWithValue("card", scope.CardId);
        query.Parameters.AddWithValue("verified", requireVerifiedEmail);
        var recipients = new List<Guid>(); await using var rows = await query.ExecuteReaderAsync(ct);
        while (await rows.ReadAsync(ct)) recipients.Add(rows.GetGuid(0));
        return recipients;
    }
}

internal sealed class InMemoryCardWatchRecipientStore(IWorkManagementStore work, IWatchSubscriptionStore watches,
    IOrganizationStore organizations, IIdentityStore identities, DemoWorkTransactionScope transaction) : ICardWatchRecipientStore
{
    public async Task<IReadOnlyList<Guid>> LockRecipientsAsync(CardWatchActivity scope, bool requireVerifiedEmail, CancellationToken ct)
    {
        if (scope.OrganizationId == Guid.Empty || scope.BoardId == Guid.Empty || scope.ListId == Guid.Empty || scope.CardId == Guid.Empty ||
            !transaction.Owns(scope.OrganizationId))
            throw new InvalidOperationException("Watch recipients require the originating Work transaction.");
        ct.ThrowIfCancellationRequested();
        var board = await work.FindBoardAsync(scope.BoardId, ct); var list = await work.FindListAsync(scope.ListId, ct);
        var card = await work.FindCardAsync(scope.CardId, ct);
        if (board is not { LifecycleState: BoardLifecycleState.Active } || board.OrganizationId != scope.OrganizationId ||
            board.Visibility is not (BoardVisibility.Private or BoardVisibility.Organization or BoardVisibility.Public) ||
            list is not { LifecycleState: WorkItemLifecycleState.Active } || list.OrganizationId != scope.OrganizationId || list.BoardId != scope.BoardId ||
            card is null || card.OrganizationId != scope.OrganizationId || card.BoardId != scope.BoardId || card.ListId != scope.ListId ||
            card.LifecycleState is not (WorkItemLifecycleState.Active or WorkItemLifecycleState.Archived) ||
            await organizations.FindOrganizationAsync(scope.OrganizationId, ct) is not { Status: OrganizationStatus.Active }) return [];
        var recipients = new List<Guid>();
        foreach (var recipient in await watches.ListActivityCandidatesAsync(scope, ct))
        {
            var account = await identities.FindUserByIdAsync(recipient, ct);
            if (account is not { Status: AccountStatus.Active } || requireVerifiedEmail && !account.EmailVerified) continue;
            var member = await organizations.FindMembershipAsync(scope.OrganizationId, recipient, ct);
            if (member is not { Active: true }) continue;
            if (board.Visibility == BoardVisibility.Private && member.Role is not (OrganizationRole.Owner or OrganizationRole.Admin) &&
                await work.FindBoardMemberAsync(scope.BoardId, recipient, ct) is not { Active: true }) continue;
            recipients.Add(recipient);
        }
        return recipients;
    }
}
