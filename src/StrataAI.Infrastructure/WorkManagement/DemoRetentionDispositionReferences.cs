using StrataAI.Application.Records;
using StrataAI.Application.WorkManagement;
using StrataAI.Domain.Records;
using StrataAI.Domain.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed record WorkDispositionRecord(Guid Id, RetentionRecordClass Class);

internal sealed partial class InMemoryWorkManagementStore
{
    // Complete private identity projection, not the bounded Board/archive UI.
    internal IReadOnlyList<WorkDispositionRecord>? ReadDispositionFamily(Guid organization,
        RetentionWorkDispositionKind kind, Guid record, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!transactionScope.Owns(organization)) throw new InvalidOperationException("Disposition requires its owning tenant transaction.");
        lock (_sync)
        {
            var root = kind switch
            {
                RetentionWorkDispositionKind.Board => _boards.TryGetValue(record, out var board)
                    && board.OrganizationId == organization && board.LifecycleState != BoardLifecycleState.Deleted,
                RetentionWorkDispositionKind.List => _lists.TryGetValue(record, out var list)
                    && list.OrganizationId == organization && list.LifecycleState != WorkItemLifecycleState.Deleted,
                RetentionWorkDispositionKind.Card => _cards.TryGetValue(record, out var card)
                    && card.OrganizationId == organization && card.LifecycleState != WorkItemLifecycleState.Deleted,
                RetentionWorkDispositionKind.CardComment => _comments.TryGetValue(record, out var comment)
                    && comment.OrganizationId == organization && comment.DeletedAt is null,
                RetentionWorkDispositionKind.Attachment => _attachmentMetadata.TryGetValue(record, out var attachment)
                    && attachment.OrganizationId == organization && attachment.LifecycleState != AttachmentLifecycleState.Deleted,
                _ => false
            };
            if (!root) return null;
            var cards = _cards.Values.Where(c => c.OrganizationId == organization
                && (kind == RetentionWorkDispositionKind.Board && c.BoardId == record
                    || kind == RetentionWorkDispositionKind.List && c.ListId == record
                    || kind == RetentionWorkDispositionKind.Card && c.Id == record)).Select(c => c.Id).ToHashSet();
            return new[] { new WorkDispositionRecord(record, kind == RetentionWorkDispositionKind.Attachment
                    ? RetentionRecordClass.Document : RetentionRecordClass.OperationalWork) }
                .Concat(cards.Select(id => new WorkDispositionRecord(id, RetentionRecordClass.OperationalWork)))
                .Concat(_comments.Values.Where(c => c.OrganizationId == organization && cards.Contains(c.CardId))
                    .Select(c => new WorkDispositionRecord(c.Id, RetentionRecordClass.OperationalWork)))
                .Concat(_attachmentMetadata.Values.Where(a => a.OrganizationId == organization && cards.Contains(a.CardId)).Select(a => new WorkDispositionRecord(a.Id, RetentionRecordClass.Document)))
                .Distinct().ToArray();
        }
    }
}
