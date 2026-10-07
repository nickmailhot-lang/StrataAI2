using StrataAI.Application.Identity;
using StrataAI.Application.Onboarding;
using StrataAI.Application.Organizations;
using StrataAI.Infrastructure.Onboarding;
using StrataAI.Infrastructure.WorkManagement;

namespace StrataAI.Infrastructure.Organizations;

// Demo delivery is synchronous simulation inside the original command. The
// shared Work gate prevents transports from observing tentative publication;
// Organization rollback restores sources, identities and contiguous sequences.
internal sealed class InMemoryOrganizationMetadataJournal(InMemoryOrganizationStore organizations,
    InMemoryInvitationStore invitations, InMemoryInvitationRecipientJournal recipientJournal,
    IIdentityStore identities, DemoWorkTransactionScope scope)
    : IOrganizationMetadataEventReader, IDemoOrganizationTransactionParticipant
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, DemoInvitationAudit> _sources = [];
    private readonly HashSet<(Guid Organization, string EntityType, Guid Entity, long Version)> _published = [];
    private readonly Dictionary<Guid, long> _heads = [];
    private readonly Dictionary<Guid, OrganizationMetadataEventCandidate[]> _events = [];

    internal async Task AppendAsync(DemoInvitationAudit audit, CancellationToken ct)
    {
        var type = audit.EventType == "ORGANIZATION_MEMBER_LEFT" ? "ORGANIZATION_MEMBER_REMOVED" : audit.EventType;
        if (type is not ("ORGANIZATION_CREATED" or "ORGANIZATION_UPDATED" or "ORGANIZATION_MEMBER_ADDED"
            or "ORGANIZATION_MEMBER_REMOVED" or "ORGANIZATION_MEMBER_INVITED" or "INVITATION_ACCEPTED" or "INVITATION_REVOKED")) return;
        ct.ThrowIfCancellationRequested();
        if (!scope.OwnsOrganizationCommand(audit.OrganizationId) || audit.Id == Guid.Empty || audit.ActorId == Guid.Empty
            || audit.CorrelationId.Length is < 1 or > 64
            || await identities.FindUserByIdAsync(audit.ActorId, ct) is not { Status: AccountStatus.Active }
            || await organizations.FindOrganizationAsync(audit.OrganizationId, ct) is not { Status: OrganizationStatus.Active } parent)
            throw new InvalidOperationException("Demo Organization metadata source authority is unavailable.");
        OrganizationMetadataEvent item;
        if (type is "ORGANIZATION_MEMBER_INVITED" or "INVITATION_ACCEPTED" or "INVITATION_REVOKED")
        {
            if (audit.EntityType != "Invitation") throw new InvalidOperationException("Demo metadata invitation subject is invalid.");
            var invitation = await invitations.FindByIdAsync(audit.OrganizationId, audit.EntityId, ct)
                ?? throw new InvalidOperationException("Demo metadata invitation subject is unavailable.");
            // Board and Portal audit sources retain their own authorization
            // surface and never enter the internal Organization metadata stream.
            if (invitation.Surface != InvitationSurface.Internal || invitation.BoardTarget is not null) return;
            var proof = recipientJournal.RequireProof(invitation, type == "ORGANIZATION_MEMBER_INVITED" ? "INVITATION_CREATED" : type);
            item = new(audit.Id, type, audit.ActorId, audit.OrganizationId, proof.Version, proof.CreatedAt, "Invitation", invitation.Id);
        }
        else
        {
            var proof = organizations.RequireTransitionProof(audit.OrganizationId, audit.EntityType, audit.EntityId, audit.EventType);
            Guid entity;
            if (type is "ORGANIZATION_CREATED" or "ORGANIZATION_UPDATED")
            {
                if (audit.EntityType != "Organization" || audit.EntityId != audit.OrganizationId
                    || parent.Version != proof.Version || parent.UpdatedAt != proof.CreatedAt
                    || type == "ORGANIZATION_CREATED" && (parent.Version != 1 || parent.OwnerUserId != audit.ActorId)
                    || type == "ORGANIZATION_UPDATED" && parent.Version <= 1)
                    throw new InvalidOperationException("Demo metadata parent transition is unproven.");
                var actor = await organizations.FindMembershipAsync(audit.OrganizationId, audit.ActorId, ct);
                if (actor is not { Active: true, Role: OrganizationRole.Owner or OrganizationRole.Admin })
                    throw new InvalidOperationException("Demo metadata parent actor is unavailable.");
                entity = parent.Id;
            }
            else
            {
                var member = type == "ORGANIZATION_MEMBER_ADDED"
                    ? organizations.FindMetadataMembership(audit.OrganizationId, audit.EntityId)
                    : await organizations.FindMembershipAsync(audit.OrganizationId, audit.EntityId, ct);
                if (member is null || member.Id == Guid.Empty || member.Version != proof.Version || member.UpdatedAt != proof.CreatedAt
                    || type == "ORGANIZATION_MEMBER_ADDED" && (audit.EntityType != "OrganizationMembership" || !member.Active || member.UserId != audit.ActorId)
                    || type == "ORGANIZATION_MEMBER_REMOVED" && (audit.EntityType != "User" || member.Active || member.Version <= 1))
                    throw new InvalidOperationException("Demo metadata membership transition is unproven.");
                entity = member.Id;
            }
            item = new(audit.Id, type, audit.ActorId, audit.OrganizationId, proof.Version, proof.CreatedAt,
                type is "ORGANIZATION_CREATED" or "ORGANIZATION_UPDATED" ? "Organization" : "OrganizationMembership", entity);
        }
        lock (_sync)
        {
            if (_sources.ContainsKey(audit.Id) || !_published.Add((item.OrganizationId, item.EntityType, item.EntityId, item.Version)))
                throw new InvalidOperationException("Demo Organization metadata source was already published.");
            var sequence = checked(_heads.GetValueOrDefault(item.OrganizationId) + 1);
            _sources.Add(audit.Id, audit);
            _heads[item.OrganizationId] = sequence;
            _events[item.OrganizationId] = [.. _events.GetValueOrDefault(item.OrganizationId) ?? [], new(sequence, item, true)];
        }
    }

    private void RequireScope(Guid organizationId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!scope.Owns(organizationId)) throw new InvalidOperationException("Demo metadata replay requires its owning Organization read transaction.");
    }
    public async Task<OrganizationMetadataCursorBinding?> GetScopeAsync(Guid organizationId, Guid actorId, CancellationToken ct)
    {
        RequireScope(organizationId, ct);
        if (await organizations.FindOrganizationAsync(organizationId, ct) is not { Status: OrganizationStatus.Active }
            || await organizations.FindMembershipAsync(organizationId, actorId, ct) is not { Active: true } member) return null;
        return new(organizationId, actorId, member.Id, member.Version);
    }
    public Task<long> GetHeadAsync(Guid organizationId, CancellationToken ct)
    {
        RequireScope(organizationId, ct);
        lock (_sync) return Task.FromResult(_heads.GetValueOrDefault(organizationId));
    }
    public Task<OrganizationMetadataEventWindow> ReadAsync(Guid organizationId, long since, int limit, CancellationToken ct)
    {
        RequireScope(organizationId, ct);
        if (since < 0 || limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(since));
        lock (_sync)
        {
            var head = _heads.GetValueOrDefault(organizationId);
            var rows = (_events.GetValueOrDefault(organizationId) ?? []).Where(row => row.Sequence > since && row.Sequence <= head).Take(limit + 1).ToArray();
            return Task.FromResult(OrganizationMetadataEventWindow.Build(since, head, limit, rows));
        }
    }
    public Action CaptureRollback()
    {
        lock (_sync)
        {
            Action[] restore = [DemoRollback.Dictionary(_sources), DemoRollback.Set(_published), DemoRollback.Dictionary(_heads), DemoRollback.Dictionary(_events)];
            return () => { lock (_sync) foreach (var action in restore) action(); };
        }
    }
}
