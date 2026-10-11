using StrataAI.Application.Records;
using StrataAI.Application.WorkManagement;
using StrataAI.Domain.Records;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed record DemoRetentionProducerReference(Guid OrganizationId, Guid RecordId, RetentionProducerKind Kind,
    Guid? ParentCardId, Guid CardId, Guid BoardId, Guid ListId, long CardVersion, long ListVersion,
    long SourceVersion, WorkItemLifecycleState CardState, WorkItemLifecycleState ListState,
    WorkItemLifecycleState SourceState, DateTimeOffset CreatedAt);

internal sealed partial class InMemoryWorkManagementStore
{
    // Only identities, versions and lifecycle; no private work/Comment/file body.
    internal DemoRetentionProducerReference? ReadRetentionProducer(Guid organization, RetentionProducerKind kind, Guid record, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (organization == Guid.Empty || !transactionScope.OwnsOrganizationCommand(organization))
            throw new InvalidOperationException("Retention producer requires an owning Organization transaction.");
        lock (_sync)
        {
            Guid cardId; Guid? parent; long revision; WorkItemLifecycleState state; DateTimeOffset created;
            switch (kind)
            {
                case RetentionProducerKind.Card:
                    if (!_cards.TryGetValue(record, out var card) || card.OrganizationId != organization) return null;
                    cardId = record; parent = null; revision = card.Version; state = card.LifecycleState; created = card.CreatedAt; break;
                case RetentionProducerKind.CardComment:
                    if (!_comments.TryGetValue(record, out var comment) || comment.OrganizationId != organization) return null;
                    cardId = comment.CardId; parent = cardId; revision = comment.Version;
                    state = comment.DeletedAt is null ? WorkItemLifecycleState.Active : WorkItemLifecycleState.Deleted; created = comment.CreatedAt; break;
                case RetentionProducerKind.Attachment:
                    if (!_attachmentMetadata.TryGetValue(record, out var file) || file.OrganizationId != organization) return null;
                    cardId = file.CardId; parent = cardId; revision = file.Version;
                    state = Enum.Parse<WorkItemLifecycleState>(file.LifecycleState.ToString()); created = file.CreatedAt; break;
                default: return null;
            }
            if (!_cards.TryGetValue(cardId, out var current) || current.OrganizationId != organization
                || !_lists.TryGetValue(current.ListId, out var list) || list.OrganizationId != organization || list.BoardId != current.BoardId) return null;
            return new(organization, record, kind, parent, cardId, current.BoardId, current.ListId, current.Version, list.Version,
                revision, current.LifecycleState, list.LifecycleState, state, created);
        }
    }
}
