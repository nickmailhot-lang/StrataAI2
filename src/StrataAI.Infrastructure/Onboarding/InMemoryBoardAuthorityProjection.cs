using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.WorkManagement;

namespace StrataAI.Infrastructure.Onboarding;

internal interface IDemoBoardAuthorityProjection
{
    Task AppendAsync(WorkEvent source, CancellationToken ct);
}
internal sealed class InMemoryBoardAuthorityProjection(InMemoryInvitationRecipientJournal journal,
    InMemoryInvitationStore invitations, IWorkManagementStore work, IOrganizationStore organizations,
    IIdentityStore identities) : IDemoBoardAuthorityProjection
{
    public async Task AppendAsync(WorkEvent source, CancellationToken ct)
    {
        if (source.EventId == Guid.Empty || source.ActorId == Guid.Empty || source.CorrelationId.Length is < 1 or > 64
            || await identities.FindUserByIdAsync(source.ActorId, ct) is not { Status: AccountStatus.Active })
            throw new InvalidOperationException("Board authority source is invalid.");
        var proof = ((InMemoryWorkManagementStore)work).RequireBoardAuthorityProof(source);
        var member = await organizations.FindMembershipAsync(source.OrganizationId, source.ActorId, ct);
        // A proven deletion withdraws ordinary reads; retained membership still
        // identifies the administrator who performed that same command.
        var boardMember = await work.FindBoardMemberAsync(source.BoardId, source.ActorId, ct,
            includeDeleted: source.EventType == "BOARD_DELETED");
        var editorChange = source.EventType == "BOARD_UPDATED" && boardMember is { Active: true, Role: BoardRole.Member };
        var selfChange = proof.SubjectId == source.ActorId && proof.PreviousRole == BoardRole.Admin
            && source.EventType is "BOARD_MEMBER_UPDATED" or "BOARD_MEMBER_REMOVED";
        if (member is not { Active: true } || member.Role is not (OrganizationRole.Owner or OrganizationRole.Admin)
            && boardMember is not { Active: true, Role: BoardRole.Admin } && !selfChange && !editorChange)
            throw new InvalidOperationException("Board authority actor is unavailable.");
        journal.PublishBoardAuthoritySource(source, proof, invitations, ct);
    }
}
