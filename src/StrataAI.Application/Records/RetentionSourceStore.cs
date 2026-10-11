using StrataAI.Application.Organizations;
using StrataAI.Domain.Records;

namespace StrataAI.Application.Records;

// Server command data, not an HTTP request. Review retains the exact admitted
// producer; category and jurisdiction require explicit human classification.
public sealed record RetentionSourceWrite(Guid OrganizationId, RetentionRecordClass RecordClass,
    Guid RecordId, RetentionProducerKind Kind, string Category, string? Jurisdiction, long SourceVersion,
    long CardVersion, Guid ActorId, Guid MembershipId, long MembershipVersion, OrganizationRole Capacity,
    Guid EventId, string CorrelationId, DateTimeOffset RegisteredAt, RetentionProducerReference Review);

public interface IRetentionSourceStore : IRetentionSourceReferenceReader
{
    // Classification and its safe audit fact belong to the owning transaction.
    // Existing classifications are immutable; null is not permission to rewrite.
    Task<RetentionSourceReference?> CaptureAsync(RetentionSourceWrite write, CancellationToken ct);
}
