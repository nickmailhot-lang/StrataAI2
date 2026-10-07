using StrataAI.Application.Identity;
using StrataAI.Infrastructure.Identity;

namespace StrataAI.Infrastructure.Identity
{
    internal sealed record DemoIssuerAuthorityProof(Guid ActorId, long Version, DateTimeOffset ChangedAt, Guid CommandId);
}

namespace StrataAI.Infrastructure.Onboarding
{
    internal interface IDemoIssuerAuthorityProjection
    {
        void Append(IdentityDomainEvent source, DemoIssuerAuthorityProof proof, CancellationToken ct);
    }

    internal sealed class InMemoryIssuerAuthorityProjection(InMemoryInvitationRecipientJournal journal,
        InMemoryInvitationStore invitations) : IDemoIssuerAuthorityProjection
    {
        public void Append(IdentityDomainEvent source, DemoIssuerAuthorityProof proof, CancellationToken ct)
            => journal.PublishIssuerAuthoritySource(source, proof, invitations, ct);
    }
}
