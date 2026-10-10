using System.Text.RegularExpressions;

namespace StrataAI.Infrastructure.WorkManagement;

// Internal append-only Demo facts, separate from diagnostics and activity projection.
internal sealed record DemoWorkAuditFact(Guid Id, Guid OrganizationId, Guid ActorId,
    string EventType, string EntityType, Guid EntityId, string CorrelationId, DateTimeOffset CreatedAt);

internal sealed partial class InMemoryWorkManagementStore
{
    private readonly Dictionary<Guid, DemoWorkAuditFact> _audits = [];

    // No HTTP exposure. Tests inspect immutable committed facts by owning tenant.
    internal IReadOnlyList<DemoWorkAuditFact> AuditSnapshot(Guid organizationId)
    {
        lock (_sync) return Array.AsReadOnly(_audits.Values.Where(a => a.OrganizationId == organizationId).ToArray());
    }

    internal Task AppendAuditFactAsync(Guid id, Guid organizationId, Guid actorId, string eventType,
        string entityType, Guid entityId, string correlationId, DateTimeOffset createdAt, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!transactionScope.Owns(organizationId) || id == Guid.Empty || organizationId == Guid.Empty ||
            actorId == Guid.Empty || entityId == Guid.Empty ||
            !Regex.IsMatch(eventType, "\\A[A-Z][A-Z0-9_]{0,79}\\z", RegexOptions.NonBacktracking) ||
            string.IsNullOrWhiteSpace(entityType) || entityType.Length > 80 ||
            string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > 120)
            throw new InvalidOperationException("Demo audit requires an owning command and valid source references.");
        var fact = new DemoWorkAuditFact(id, organizationId, actorId, eventType, entityType, entityId,
            correlationId, createdAt.ToUniversalTime());
        lock (_sync)
        {
            if (_audits.TryGetValue(id, out var original))
            {
                if (original != fact) throw new InvalidOperationException("Demo audit source identity was reused.");
            }
            else _audits.Add(id, fact);
        }
        return Task.CompletedTask;
    }
}
