using StrataAI.Domain.Organizations;

namespace StrataAI.Application.Organizations;

public sealed record OrganizationConfigurationRecord(Guid OrganizationId, long Version,
    OrganizationConfigurationData Configuration, string OrganizationName, string OrganizationType,
    long OrganizationVersion, Guid ActorId, Guid EventId, string CorrelationId,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record OrganizationConfigurationReceipt(Guid ActorId, Guid Key, string Fingerprint,
    OrganizationConfigurationRecord Result, DateTimeOffset ExpiresAt);

// Private values belong to the revision; publication contains only the source identity/version.
public sealed record OrganizationConfigurationEvent(Guid EventId, Guid OrganizationId, Guid ActorId,
    long Version, string CorrelationId, DateTimeOffset CreatedAt)
{
    public string EventType => "ORGANIZATION_CONFIGURATION_CHANGED";
    public string EntityType => "OrganizationConfiguration";
    public Guid EntityId => OrganizationId;
}

public enum OrganizationConfigurationWriteResult { Written, VersionConflict, IdentifierConflict, KeyConflict, InvalidSource }

public interface IOrganizationConfigurationStore
{
    // Owning Organization transaction and current configuration read authorization required.
    Task<OrganizationConfigurationRecord?> ReadAsync(Guid organization, CancellationToken ct);
    Task<OrganizationConfigurationReceipt?> ReadReceiptAsync(Guid organization, Guid actor, Guid key, CancellationToken ct);
    Task<IReadOnlyList<OrganizationConfigurationRecord>> ReadHistoryAsync(Guid organization, long? beforeVersion, CancellationToken ct);
    Task<IReadOnlyList<OrganizationConfigurationEvent>> ReadEventsAsync(Guid organization, long afterVersion, CancellationToken ct);
    // Resolve inside this tenant, never through a transferable global routing hint.
    Task<bool> IntakeAvailableAsync(Guid organization, Guid? board, Guid? list, CancellationToken ct);
    // Atomically persist state, immutable history, audit source and original receipt.
    Task<OrganizationConfigurationWriteResult> WriteAsync(long expectedVersion, OrganizationConfigurationRecord record,
        OrganizationConfigurationReceipt receipt, CancellationToken ct);
}
