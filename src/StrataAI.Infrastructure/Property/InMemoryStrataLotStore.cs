using StrataAI.Application.Property;
using StrataAI.Domain.Property;
using StrataAI.Infrastructure.Organizations;
using StrataAI.Infrastructure.WorkManagement;

namespace StrataAI.Infrastructure.Property;

internal sealed class InMemoryStrataLotStore(DemoWorkTransactionScope scope) : IStrataLotStore, IDemoOrganizationTransactionParticipant
{
    private readonly Dictionary<(Guid Organization, Guid Id), StrataLot> _current = [];
    private readonly Dictionary<(Guid Organization, Guid Id, long Version), StrataLotRevision> _history = [];
    private readonly Dictionary<(Guid Organization, Guid Actor, Guid Key), StrataLotReceipt> _receipts = [];
    private void Require(Guid organization, CancellationToken ct) {
        ct.ThrowIfCancellationRequested();
        if (!scope.OwnsOrganizationCommand(organization)) throw new InvalidOperationException("Unit directory requires its Organization transaction.");
    }
    public Task<IReadOnlyList<StrataLot>> ListAsync(Guid organization, Guid? after, CancellationToken ct) {
        Require(organization, ct);
        return Task.FromResult<IReadOnlyList<StrataLot>>(_current.Values.Where(x => x.OrganizationId == organization
            && (after is null || x.Id.CompareTo(after.Value) > 0)).OrderBy(x => x.Id).Take(51).ToArray());
    }
    public Task<StrataLot?> ReadAsync(Guid organization, Guid id, CancellationToken ct) {
        Require(organization, ct); return Task.FromResult(_current.GetValueOrDefault((organization, id)));
    }
    public Task<IReadOnlyList<StrataLotRevision>> HistoryAsync(Guid organization, Guid id, long? before, CancellationToken ct) {
        Require(organization, ct);
        return Task.FromResult<IReadOnlyList<StrataLotRevision>>(_history.Values.Where(x => x.Lot.OrganizationId == organization
            && x.Lot.Id == id && (before is null || x.Lot.Version < before)).OrderByDescending(x => x.Lot.Version).Take(51).ToArray());
    }
    public Task<StrataLotReceipt?> ReceiptAsync(Guid organization, Guid actor, Guid key, CancellationToken ct) {
        Require(organization, ct); return Task.FromResult(_receipts.GetValueOrDefault((organization, actor, key)));
    }
    public Task<StrataLotWriteResult> WriteAsync(StrataLotRevision revision, StrataLotReceipt receipt, long expectedVersion, CancellationToken ct) {
        var lot = revision.Lot; Require(lot.OrganizationId, ct);
        var previous = _current.GetValueOrDefault((lot.OrganizationId, lot.Id));
        if ((previous?.Version ?? 0) != expectedVersion || lot.Version != expectedVersion + 1)
            return Task.FromResult(StrataLotWriteResult.VersionConflict);
        if (_current.Values.Any(x => x.OrganizationId == lot.OrganizationId && x.Id != lot.Id
            && (x.Data.UnitIdentifier == lot.Data.UnitIdentifier || x.Data.LotIdentifier == lot.Data.LotIdentifier)))
            return Task.FromResult(StrataLotWriteResult.IdentifierConflict);
        if (_receipts.ContainsKey((lot.OrganizationId, receipt.ActorId, receipt.Key)))
            throw new InvalidOperationException("Unit retry key already exists.");
        _current[(lot.OrganizationId, lot.Id)] = lot;
        _history.Add((lot.OrganizationId, lot.Id, lot.Version), revision);
        _receipts.Add((lot.OrganizationId, receipt.ActorId, receipt.Key), receipt);
        return Task.FromResult(StrataLotWriteResult.Written);
    }
    public Action CaptureRollback() {
        Action[] undo = [DemoRollback.Dictionary(_current), DemoRollback.Dictionary(_history), DemoRollback.Dictionary(_receipts)];
        return () => { foreach (var restore in undo) restore(); };
    }
}
