using System.Text.RegularExpressions;

namespace StrataAI.Infrastructure.WorkManagement;

// Internal append-only Demo facts, separate from diagnostics and activity projection.
internal sealed record DemoWorkAuditFact(Guid Id, Guid OrganizationId, Guid ActorId,
    string EventType, string EntityType, Guid EntityId, string CorrelationId, DateTimeOffset CreatedAt)
{
    public DemoRetentionAuditMetadata? RetentionMetadata { get; init; }
    public DemoRetentionHoldAuditMetadata? HoldMetadata { get; init; }
}
internal sealed record DemoRetentionAuditMetadata(long Version, string RecordClass, string Action, string ActingCapacity);
internal sealed record DemoRetentionHoldAuditMetadata(long Version, string Status, string ScopeKind, string ActingCapacity);

internal sealed partial class InMemoryWorkManagementStore
{
    private readonly Dictionary<Guid, DemoWorkAuditFact> _audits = [];

    // No HTTP exposure. Tests inspect immutable committed facts by owning tenant.
    internal IReadOnlyList<DemoWorkAuditFact> AuditSnapshot(Guid organizationId)
    {
        lock (_sync) return Array.AsReadOnly(_audits.Values.Where(a => a.OrganizationId == organizationId).ToArray());
    }

    internal Task AppendAuditFactAsync(Guid id, Guid organizationId, Guid actorId, string eventType,
        string entityType, Guid entityId, string correlationId, DateTimeOffset createdAt, CancellationToken ct,
        DemoRetentionAuditMetadata? retentionMetadata = null, DemoRetentionHoldAuditMetadata? holdMetadata = null)
    {
        ct.ThrowIfCancellationRequested();
        var configurationSource = eventType == "ORGANIZATION_CONFIGURATION_CHANGED"
            && entityType == "OrganizationConfiguration" && entityId == organizationId;
        var lifecycleSource = eventType is "ORGANIZATION_SUSPENDED" or "ORGANIZATION_DEACTIVATED" or "ORGANIZATION_REACTIVATED"
            && entityType == "Organization" && entityId == organizationId;
        var retentionSource = eventType == "RECORD_RETENTION_POLICY_CHANGED" && entityType == "RECORD_RETENTION_POLICY";
        var holdSource = eventType is "RETENTION_HOLD_APPLIED" or "RETENTION_HOLD_RELEASED" && entityType == "RETENTION_HOLD";
        var organizationSource = configurationSource || lifecycleSource || retentionSource || holdSource;
        if (holdSource != (holdMetadata is not null) || holdMetadata is { } hold
            && (hold.Version < 1 || hold.Status != (eventType == "RETENTION_HOLD_APPLIED" ? "ACTIVE" : "RELEASED")
                || hold.ScopeKind is not ("ORGANIZATION" or "CLASS" or "CATEGORY" or "RECORD")
                || hold.ActingCapacity is not ("ORGANIZATION_OWNER" or "ORGANIZATION_ADMIN")))
            throw new InvalidOperationException("Demo hold audit metadata is invalid.");
        if (retentionSource != (retentionMetadata is not null) || retentionMetadata is { } metadata
            && (metadata.Version < 1 || metadata.RecordClass is not ("OPERATIONAL_WORK" or "GOVERNANCE" or "AUDIT" or "CORRESPONDENCE" or "DOCUMENT" or "AI")
                || metadata.Action is not ("PRESERVE" or "DELETE" or "ANONYMIZE")
                || metadata.ActingCapacity is not ("ORGANIZATION_OWNER" or "ORGANIZATION_ADMIN")))
            throw new InvalidOperationException("Demo retention audit metadata is invalid.");
        if (!transactionScope.Owns(organizationId) || id == Guid.Empty || organizationId == Guid.Empty ||
            actorId == Guid.Empty || entityId == Guid.Empty ||
            !Regex.IsMatch(eventType, "\\A[A-Z][A-Z0-9_]{0,79}\\z", RegexOptions.NonBacktracking) ||
            string.IsNullOrWhiteSpace(entityType) || entityType.Length > 80 ||
            string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > (organizationSource ? 256 : 120)
            || organizationSource && (!transactionScope.OwnsOrganizationCommand(organizationId) || correlationId.Any(char.IsControl)))
            throw new InvalidOperationException("Demo audit requires an owning command and valid source references.");
        var fact = new DemoWorkAuditFact(id, organizationId, actorId, eventType, entityType, entityId,
            correlationId, createdAt.ToUniversalTime()) { RetentionMetadata = retentionMetadata, HoldMetadata = holdMetadata };
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
