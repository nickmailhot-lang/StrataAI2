using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed class InMemoryActivityFeedStore(InMemoryWorkEventStore journal, ActivitySourceScopeResolver scopes,
    DemoWorkTransactionScope transactions) : IActivityFeedStore
{
    public async Task<IReadOnlyList<ActivityEventSource>> ReadAsync(ActivityCursorBinding binding, ActivityCursor? before,
        CancellationToken ct = default)
    {
        if (binding.OrganizationId == Guid.Empty || binding.ViewerId == Guid.Empty || binding.TargetId == Guid.Empty ||
            !Enum.IsDefined(binding.Kind) || !transactions.Owns(binding.OrganizationId))
            throw new InvalidOperationException("Activity candidates require the owning Work transaction.");
        ActivityEventSourceWindow.RequireCursor(before?.CreatedAt, before?.EventId);
        var rows = new List<ActivityEventSource>();
        var resolve = scopes.CreateReadPass(binding.ViewerId);
        foreach (var row in journal.ActivitySources(binding.OrganizationId))
        {
            ct.ThrowIfCancellationRequested();
            if (binding.Kind == ActivityTargetKind.Board && row.BoardId != binding.TargetId ||
                before is not null && (row.CreatedAt > before.CreatedAt || row.CreatedAt == before.CreatedAt &&
                    string.CompareOrdinal(row.EventId.ToString("N"), before.EventId.ToString("N")) >= 0)) continue;
            var scope = await resolve(row, ct);
            if (scope is null || binding.Kind == ActivityTargetKind.Card && (scope.TargetType != "Card" || scope.TargetId != binding.TargetId)) continue;
            rows.Add(row);
        }
        return Array.AsReadOnly(rows.OrderByDescending(row => row.CreatedAt)
            .ThenByDescending(row => row.EventId.ToString("N"), StringComparer.Ordinal).Take(51).ToArray());
    }
}
