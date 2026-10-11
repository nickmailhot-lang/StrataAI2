using StrataAI.Application.Records;
using StrataAI.Infrastructure.WorkManagement;

namespace StrataAI.Infrastructure.Records;

// Deferred catalogs avoid a constructor cycle through the actual Board authority.
// Both Work and Organization commands retain the shared Demo mutation gate.
internal sealed class InMemoryRetentionWorkDisposition(InMemoryWorkManagementStore work,
    Func<InMemoryRetentionHoldStore> holds, Func<InMemoryRetentionSourceStore> sources) : IRetentionWorkDisposition
{
    public Task<RetentionDispositionAdmission> CheckAsync(Guid organization, RetentionWorkDispositionKind kind,
        Guid record, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (organization == Guid.Empty || record == Guid.Empty || !Enum.IsDefined(kind))
            throw new InvalidOperationException("Disposition requires valid owning identities.");
        var family = work.ReadDispositionFamily(organization, kind, record, ct);
        if (family is null) return Task.FromResult(RetentionDispositionAdmission.Unavailable);
        var holdCatalog = holds(); var sourceCatalog = sources();
        foreach (var affected in family)
        {
            var category = sourceCatalog.ReadDispositionCategory(organization, affected.Class, affected.Id, ct);
            if (holdCatalog.HasActiveDispositionHold(organization, affected.Class, affected.Id, category, ct))
                return Task.FromResult(RetentionDispositionAdmission.Held);
        }
        return Task.FromResult(RetentionDispositionAdmission.Clear);
    }
}
