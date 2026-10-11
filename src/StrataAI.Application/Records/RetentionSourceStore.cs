using StrataAI.Application.Organizations;
using StrataAI.Domain.Records;

namespace StrataAI.Application.Records;

// Server command data, not an HTTP request. Review retains the exact admitted
// producer; category and jurisdiction require explicit human classification.
public sealed record RetentionSourceWrite(Guid OrganizationId, RetentionRecordClass RecordClass,
    Guid RecordId, RetentionProducerKind Kind, string Category, string? Jurisdiction, long SourceVersion,
    long CardVersion, Guid ActorId, Guid MembershipId, long MembershipVersion, OrganizationRole Capacity,
    Guid EventId, string CorrelationId, DateTimeOffset RegisteredAt, RetentionProducerReference Review,
    Guid? CommandKey = null, string? Fingerprint = null);

public sealed record RetentionSourceClassification(RetentionSourceReference Reference, string Category,
    string? Jurisdiction, Guid ActorId, Guid EventId, DateTimeOffset RegisteredAt);
public sealed record RetentionSourceReceipt(Guid ActorId, Guid Key, string Fingerprint,
    RetentionSourceClassification Result, DateTimeOffset ExpiresAt);

public interface IRetentionSourceCommandStore : IRetentionSourceStore
{
    // Read only the immutable source hint before current source authorization.
    Task<RetentionSourceReference?> ReadReceiptReferenceAsync(Guid organization, Guid actor, Guid key, CancellationToken ct);
    Task<RetentionSourceReceipt?> ReadReceiptAsync(Guid organization, Guid actor, Guid key, CancellationToken ct);
}

public static class RetentionSourceCommandIdentity
{
    public static bool Valid(RetentionSourceWrite write) => write.CommandKey is null
        ? write.Fingerprint is null
        : write.CommandKey != Guid.Empty && write.Fingerprint is { Length: 64 }
            && write.Fingerprint.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F')
            && write.RegisteredAt <= DateTimeOffset.MaxValue.AddHours(-24);
}

public interface IRetentionSourceStore : IRetentionSourceReferenceReader
{
    // Classification and its safe audit fact belong to the owning transaction.
    // Existing classifications are immutable; null is not permission to rewrite.
    Task<RetentionSourceReference?> CaptureAsync(RetentionSourceWrite write, CancellationToken ct);
}
