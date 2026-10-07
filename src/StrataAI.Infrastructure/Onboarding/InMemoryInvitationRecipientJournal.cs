using StrataAI.Application.Identity;
using StrataAI.Application.Onboarding;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.Identity;
using StrataAI.Infrastructure.Organizations;
using StrataAI.Infrastructure.WorkManagement;

namespace StrataAI.Infrastructure.Onboarding;

internal sealed record DemoInvitationAudit(Guid Id, Guid OrganizationId, Guid ActorId, string EventType,
    string EntityType, Guid EntityId, string CorrelationId, DateTimeOffset CreatedAt);
internal interface IDemoInvitationAuditProjection
{
    Task AppendAsync(DemoInvitationAudit audit, CancellationToken cancellationToken);
}
internal sealed record DemoInvitationProof(Guid InvitationId, Guid OrganizationId, long Version, string EventType,
    string EmailNormalized, InvitationSurface Surface, string TargetRole, BoardInvitationTarget? BoardTarget,
    Guid IssuerId, Guid? AcceptedActorId, DateTimeOffset CreatedAt)
{
    public bool Matches(InvitationRecord row) => InvitationId == row.Id && OrganizationId == row.OrganizationId
        && EmailNormalized == row.EmailNormalized && Surface == row.Surface && TargetRole == row.TargetRole
        && BoardTarget == row.BoardTarget && IssuerId == row.CreatedByUserId && AcceptedActorId == row.AcceptedByUserId
        && CreatedAt == (EventType == "INVITATION_ACCEPTED" ? row.AcceptedAt
            : EventType == "INVITATION_REVOKED" ? row.RevokedAt : row.CreatedAt);
}

// Owning Organization and Work commands produce this journal. Shared gate
// ownership excludes replay while tentative state is awaiting its final fence.
internal sealed class InMemoryInvitationRecipientJournal(DemoWorkTransactionScope workScope,
    DemoIdentityTransactionScope identityScope, IIdentityStore identities)
    : IInvitationRecipientEventReader, IDemoOrganizationTransactionParticipant, IDemoWorkTransactionParticipant, IDemoIdentityTransactionParticipant
{
    private readonly object _sync = new();
    private readonly Dictionary<(Guid Organization, Guid Invitation), DemoInvitationProof> _proofs = [];
    private readonly Dictionary<Guid, (DemoInvitationAudit Audit, DemoInvitationProof Proof)> _sources = [];
    private readonly HashSet<(Guid Organization, Guid Invitation, long Version)> _published = [];
    private readonly Dictionary<string, long> _heads = new(StringComparer.Ordinal);
    private readonly Dictionary<string, InvitationRecipientEvent[]> _events = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, (DemoInvitationAudit Audit, DemoOrganizationAuthorityProof Proof)> _authoritySources = [];
    private readonly HashSet<(Guid Organization, string EntityType, Guid Entity, long Version)> _authorityPublished = [];
    private readonly Dictionary<string, long> _authorityRevisions = new(StringComparer.Ordinal);
    private readonly HashSet<(Guid Source, string Email)> _authorityEffects = [];
    private readonly Dictionary<(Guid Organization, Guid Source, DateTimeOffset AfterAt, Guid AfterId), int> _authorityPages = [];
    private readonly Dictionary<Guid, (IdentityDomainEvent Source, DemoIssuerAuthorityProof Proof)> _issuerAuthoritySources = [];
    private readonly HashSet<(Guid Actor, long Version)> _issuerAuthorityPublished = [];

    internal void PublishIssuerAuthoritySource(IdentityDomainEvent source, DemoIssuerAuthorityProof proof,
        InMemoryInvitationStore invitations, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!identityScope.Active || proof.CommandId != identityScope.CommandId || source.EventType != "USER_DEACTIVATED"
            || source.ActorId != proof.ActorId || source.EntityId != proof.ActorId || source.Version != proof.Version)
            throw new InvalidOperationException("Issuer authority source requires its owning account command.");
        lock (_sync)
        {
            if (_issuerAuthoritySources.ContainsKey(source.EventId) || !_issuerAuthorityPublished.Add((proof.ActorId, proof.Version)))
                throw new InvalidOperationException("Issuer authority transition was already published.");
            _issuerAuthoritySources.Add(source.EventId, (source, proof));
            var after = Guid.Empty;
            while (true)
            {
                var organizations = invitations.ReadIssuerAuthorityOrganizations(proof.ActorId, source.CreatedAt, after, ct);
                foreach (var organization in organizations)
                    SimulateAuthorityPages(organization, source.EventId, source.CreatedAt, invitations, ct);
                if (organizations.Length < 100) return;
                after = organizations[^1];
            }
        }
    }
    private readonly Dictionary<Guid, (WorkEvent Source, DemoBoardAuthorityProof Proof)> _boardAuthoritySources = [];
    private readonly HashSet<(Guid Command, Guid Board, string Type, Guid Subject, long Version)> _boardAuthorityPublished = [];

    internal void PublishBoardAuthoritySource(WorkEvent source, DemoBoardAuthorityProof proof,
        InMemoryInvitationStore invitations, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!workScope.Owns(source.OrganizationId) || proof.CommandId != workScope.CommandId)
            throw new InvalidOperationException("Board authority source requires its owning command.");
        lock (_sync)
        {
            if (_boardAuthoritySources.ContainsKey(source.EventId) || _authoritySources.ContainsKey(source.EventId)
                || !_boardAuthorityPublished.Add((proof.CommandId, proof.BoardId, proof.EventType, proof.SubjectId, proof.SubjectVersion)))
                throw new InvalidOperationException("Board authority transition was already published.");
            _boardAuthoritySources.Add(source.EventId, (source, proof));
            SimulateAuthorityPages(source.OrganizationId, source.EventId, source.CreatedAt, invitations, ct);
        }
    }

    internal void SimulateAuthorityDelivery(DemoInvitationAudit audit, InMemoryInvitationStore invitations, CancellationToken ct)
    {
        // Demo follows its existing immediate Work-event simulation. Production
        // delivery remains exclusively the separately leased Worker capability.
        if (!workScope.OwnsOrganizationCommand(audit.OrganizationId))
            throw new InvalidOperationException("Demo authority delivery requires its owning command.");
        lock (_sync)
        {
            if (!_authoritySources.TryGetValue(audit.Id, out var source) || source.Audit != audit)
                throw new InvalidOperationException("Demo authority source is unavailable.");
            SimulateAuthorityPages(audit.OrganizationId, audit.Id, source.Proof.CreatedAt, invitations, ct);
        }
    }
    private void SimulateAuthorityPages(Guid organization, Guid source, DateTimeOffset cutoff, InMemoryInvitationStore invitations, CancellationToken ct)
    {
        lock (_sync)
        {
            (DateTimeOffset At, Guid Id) after = (DateTimeOffset.MinValue, Guid.Empty);
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var key = (organization, source, after.At, after.Id);
                if (_authorityPages.ContainsKey(key)) return;
                var page = invitations.ReadAuthorityPage(organization, cutoff, after, ct);
                foreach (var row in page)
                    if (_authorityEffects.Add((source, row.EmailNormalized)))
                        _authorityRevisions[row.EmailNormalized] = checked(_authorityRevisions.GetValueOrDefault(row.EmailNormalized) + 1);
                _authorityPages.Add(key, page.Length);
                if (page.Length < 100) return;
                after = (page[^1].CreatedAt, page[^1].Id);
            }
        }
    }

    internal void PublishAuthoritySource(DemoInvitationAudit audit, DemoOrganizationAuthorityProof proof, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!workScope.OwnsOrganizationCommand(audit.OrganizationId) || proof.OrganizationId != audit.OrganizationId)
            throw new InvalidOperationException("Authority source requires its owning Organization command.");
        lock (_sync)
        {
            if (_authoritySources.ContainsKey(audit.Id)
                || !_authorityPublished.Add((proof.OrganizationId, proof.EntityType, proof.EntityId, proof.Version)))
                throw new InvalidOperationException("Organization authority source was already published.");
            // Source publication itself scans no recipients and emits no
            // synthetic invitation event. Demo simulation follows separately.
            _authoritySources.Add(audit.Id, (audit, proof));
        }
    }

    internal void Capture(InvitationRecord row, long version, string eventType, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // Raw fixture/legacy store inserts are not owning commands and invent no history.
        if (!workScope.OwnsOrganizationCommand(row.OrganizationId)) return;
        var at = eventType == "INVITATION_ACCEPTED" ? row.AcceptedAt : eventType == "INVITATION_REVOKED" ? row.RevokedAt : row.CreatedAt;
        if (row.Id == Guid.Empty || row.OrganizationId == Guid.Empty || row.CreatedByUserId == Guid.Empty
            || row.EmailNormalized.Length is < 3 or > 320 || row.EmailNormalized != row.EmailNormalized.ToUpperInvariant()
            || !Enum.IsDefined(row.Surface) || version < 1 || at is null || at == default(DateTimeOffset)
            || row.BoardTarget is { } target && (row.Surface != InvitationSurface.Internal || row.TargetRole != "MEMBER"
                || target.BoardId == Guid.Empty || !Enum.IsDefined(target.Role))
            || row.BoardTarget is null && (row.Surface == InvitationSurface.Internal
                ? row.TargetRole is not ("OWNER" or "ADMIN" or "MEMBER")
                : row.TargetRole is not ("OWNER" or "CO_OWNER" or "TENANT" or "OCCUPANT" or "AUTHORIZED_REPRESENTATIVE" or "OTHER"))
            || eventType is not ("INVITATION_CREATED" or "INVITATION_ACCEPTED" or "INVITATION_REVOKED")
            || eventType != "INVITATION_ACCEPTED" && row.AcceptedByUserId is not null
            || eventType == "INVITATION_CREATED" && (version != 1 || row.AcceptedAt is not null || row.RevokedAt is not null)
            || eventType == "INVITATION_ACCEPTED" && (version <= 1 || row.AcceptedByUserId is null || row.AcceptedByUserId == Guid.Empty || row.RevokedAt is not null)
            || eventType == "INVITATION_REVOKED" && (version <= 1 || row.AcceptedAt is not null))
            throw new InvalidOperationException("Invitation recipient transition is unproven.");
        var proof = new DemoInvitationProof(row.Id, row.OrganizationId, version, eventType, row.EmailNormalized,
            row.Surface, row.TargetRole, row.BoardTarget, row.CreatedByUserId, row.AcceptedByUserId, at.Value);
        lock (_sync)
        {
            var key = (row.OrganizationId, row.Id);
            if (_proofs.TryGetValue(key, out var previous) && (version != previous.Version + 1
                || previous.EventType != "INVITATION_CREATED" || previous.EmailNormalized != proof.EmailNormalized
                || previous.Surface != proof.Surface || previous.TargetRole != proof.TargetRole
                || previous.BoardTarget != proof.BoardTarget || previous.IssuerId != proof.IssuerId))
                throw new InvalidOperationException("Invitation recipient transition is immutable.");
            _proofs[key] = proof;
        }
    }
    internal DemoInvitationProof RequireProof(InvitationRecord row, string eventType)
    {
        if (!workScope.OwnsOrganizationCommand(row.OrganizationId))
            throw new InvalidOperationException("Invitation audit requires its owning Organization command.");
        lock (_sync)
            return _proofs.TryGetValue((row.OrganizationId, row.Id), out var proof) && proof.EventType == eventType && proof.Matches(row)
                ? proof : throw new InvalidOperationException("Invitation recipient transition is unproven.");
    }
    internal void Publish(DemoInvitationAudit audit, DemoInvitationProof proof, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!workScope.OwnsOrganizationCommand(audit.OrganizationId))
            throw new InvalidOperationException("Invitation audit requires its owning Organization command.");
        lock (_sync)
        {
            if (_sources.ContainsKey(audit.Id) || !_published.Add((proof.OrganizationId, proof.InvitationId, proof.Version)))
                throw new InvalidOperationException("Invitation recipient source was already published.");
            var sequence = checked(_heads.GetValueOrDefault(proof.EmailNormalized) + 1);
            var source = new InvitationRecipientEvent(audit.Id, proof.EventType, sequence, proof.CreatedAt);
            _sources.Add(audit.Id, (audit, proof));
            _heads[proof.EmailNormalized] = sequence;
            _events[proof.EmailNormalized] = [.. _events.GetValueOrDefault(proof.EmailNormalized) ?? [], source];
        }
    }
    public Action CaptureRollback()
    {
        lock (_sync)
        {
            // Arrays and records are immutable; dictionary snapshots retain the
            // earlier committed identities and restore counters without gaps.
            Action[] restore = [DemoRollback.Dictionary(_proofs), DemoRollback.Dictionary(_sources), DemoRollback.Set(_published),
                DemoRollback.Dictionary(_heads), DemoRollback.Dictionary(_events),
                DemoRollback.Dictionary(_authoritySources), DemoRollback.Set(_authorityPublished),
                DemoRollback.Dictionary(_boardAuthoritySources), DemoRollback.Set(_boardAuthorityPublished),
                DemoRollback.Dictionary(_issuerAuthoritySources), DemoRollback.Set(_issuerAuthorityPublished),
                DemoRollback.Dictionary(_authorityRevisions), DemoRollback.Set(_authorityEffects), DemoRollback.Dictionary(_authorityPages)];
            return () => { lock (_sync) foreach (var action in restore) action(); };
        }
    }
    public async Task<InvitationRecipientCursorBinding?> GetScopeAsync(Guid actorId, CancellationToken cancellationToken)
    {
        if (!identityScope.Owns(actorId)) throw new InvalidOperationException("Recipient replay requires its owning account transaction.");
        var user = await identities.FindUserByIdAsync(actorId, cancellationToken);
        if (user is not { Status: AccountStatus.Active, EmailVerified: true } || user.Id != actorId) return null;
        lock (_sync) return new(actorId, user.EmailNormalized, user.Version, _authorityRevisions.GetValueOrDefault(user.EmailNormalized));
    }
    public async Task<long> GetHeadAsync(InvitationRecipientCursorBinding binding, CancellationToken cancellationToken)
    {
        await RequireAsync(binding, cancellationToken);
        lock (_sync) return _heads.GetValueOrDefault(binding.EmailNormalized);
    }
    public async Task<InvitationRecipientEventWindow> ReadAsync(InvitationRecipientCursorBinding binding, long since, int limit,
        CancellationToken cancellationToken)
    {
        if (since < 0 || limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(since));
        await RequireAsync(binding, cancellationToken);
        lock (_sync)
        {
            var head = _heads.GetValueOrDefault(binding.EmailNormalized);
            var rows = (_events.GetValueOrDefault(binding.EmailNormalized) ?? []).Where(e => e.Sequence > since && e.Sequence <= head)
                .Take(limit + 1).ToArray();
            return InvitationRecipientEventWindow.Build(since, head, limit, rows);
        }
    }
    private async Task RequireAsync(InvitationRecipientCursorBinding binding, CancellationToken ct)
    {
        if (await GetScopeAsync(binding.ActorId, ct) != binding) throw new InvalidOperationException("Recipient account admission changed.");
    }
}
