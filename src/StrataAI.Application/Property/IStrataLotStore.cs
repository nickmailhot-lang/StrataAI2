using StrataAI.Domain.Property;

namespace StrataAI.Application.Property;

public sealed record StrataLotRevision(StrataLot Lot, Guid ActorId, Guid EventId, string CorrelationId);
public sealed record StrataLotReceipt(Guid ActorId, Guid Key, string Fingerprint, StrataLot Result, DateTimeOffset ExpiresAt);
public enum StrataLotWriteResult { Written, VersionConflict, IdentifierConflict }
public interface IStrataLotStore
{
    Task<IReadOnlyList<StrataLot>> ListAsync(Guid organization, Guid? after, CancellationToken ct);
    Task<StrataLot?> ReadAsync(Guid organization, Guid id, CancellationToken ct);
    Task<IReadOnlyList<StrataLotRevision>> HistoryAsync(Guid organization, Guid id, long? before, CancellationToken ct);
    Task<StrataLotReceipt?> ReceiptAsync(Guid organization, Guid actor, Guid key, CancellationToken ct);
    Task<StrataLotWriteResult> WriteAsync(StrataLotRevision revision, StrataLotReceipt receipt, long expectedVersion, CancellationToken ct);
}
public sealed record StrataLotPage(Guid OrganizationId, IReadOnlyList<StrataLot> Items, Guid? NextCursor);
public sealed record StrataLotHistoryPage(Guid OrganizationId, Guid StrataLotId, IReadOnlyList<StrataLotRevision> Items, long? NextBeforeVersion);
