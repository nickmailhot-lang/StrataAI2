namespace StrataAI.Application.Organizations;

public sealed record OrganizationOperationalLifecycleReview(Guid OrganizationId, Guid ActorId,
    OrganizationStatus Status, long Version, DateTimeOffset UpdatedAt, string ActingCapacity);

// This admission is for reviewed lifecycle commands only. It does not admit
// operational writes, restore withdrawn memberships or authorize portal reads.
public sealed record OrganizationOperationalLifecycleScope(Guid OrganizationId,
    OrganizationStatus Status, long Version, DateTimeOffset UpdatedAt, Guid ActorId,
    string ActorLabel, Guid MembershipId, long MembershipVersion, OrganizationRole Role,
    bool MembershipActive)
{
    public bool Admitted(Guid organization, Guid actor) => OrganizationId == organization
        && organization != Guid.Empty && ActorId == actor && actor != Guid.Empty
        && Status is OrganizationStatus.Active or OrganizationStatus.Suspended or OrganizationStatus.Deactivated
        && Version > 0 && MembershipId != Guid.Empty && MembershipVersion > 0 && MembershipActive
        && Role is OrganizationRole.Owner or OrganizationRole.Admin
        && !string.IsNullOrWhiteSpace(ActorLabel) && ActorLabel.Length <= 160 && !ActorLabel.Any(char.IsControl);
}

public sealed record OrganizationOperationalLifecycleTransition(Guid OrganizationId,
    Guid EventId, Guid ActorId, string ActorLabel, string ActingCapacity,
    Guid MembershipId, long MembershipVersion, OrganizationStatus PreviousStatus,
    OrganizationStatus Status, long Version, string CorrelationId, DateTimeOffset CreatedAt)
{
    public string EntityType => "Organization";
    public Guid EntityId => OrganizationId;
    public string EventType => Status switch
    {
        OrganizationStatus.Suspended => "ORGANIZATION_SUSPENDED",
        OrganizationStatus.Deactivated => "ORGANIZATION_DEACTIVATED",
        OrganizationStatus.Active => "ORGANIZATION_REACTIVATED",
        _ => throw new InvalidOperationException("Invalid operational lifecycle source."),
    };
}

public sealed record OrganizationOperationalLifecycleReceipt(Guid ActorId, Guid Key,
    string Fingerprint, OrganizationOperationalLifecycleTransition Result, DateTimeOffset ExpiresAt);

public enum OrganizationOperationalLifecycleWriteResult { Written, VersionConflict, KeyConflict, InvalidSource }

public interface IOrganizationOperationalLifecycleUnitOfWork
{
    // Lock the retained parent before memberships/account/session. Only Active,
    // Suspended and Deactivated parents are eligible. Commit successful results
    // after the final current-session fence; otherwise roll back every participant.
    Task<OrganizationOperation<T>> ExecuteAsync<T>(Guid organization, Guid actor,
        Func<Task<OrganizationOperation<T>>> action, CancellationToken ct);
}

public interface IOrganizationOperationalLifecycleStore
{
    // All calls require the owning lifecycle transaction; never global routing.
    Task<OrganizationOperationalLifecycleScope?> ReadScopeAsync(Guid organization, Guid actor, CancellationToken ct);
    Task<OrganizationOperationalLifecycleReceipt?> ReadReceiptAsync(Guid organization, Guid actor, Guid key, CancellationToken ct);
    // Atomic CAS of parent state/version, immutable transition/audit, receipt and
    // durable publication intent. Leave the entire historical graph unchanged.
    Task<OrganizationOperationalLifecycleWriteResult> WriteAsync(long expectedVersion,
        OrganizationOperationalLifecycleTransition transition, OrganizationOperationalLifecycleReceipt receipt, CancellationToken ct);
}
