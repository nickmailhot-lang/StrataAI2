namespace StrataAI.Infrastructure.WorkManagement;

internal interface IDemoWorkTransactionParticipant
{
    Action CaptureRollback();
}
internal sealed class DemoWorkTransactionScope
{
    private readonly AsyncLocal<(Guid Organization, bool OrganizationCommand, Guid CommandId, Guid? DeletionRequest, Guid? DeletionActor)?> _organization = new();
    public Guid CommandId => _organization.Value?.CommandId ?? Guid.Empty;
    public bool Owns(Guid organization) => _organization.Value?.Organization == organization;
    public bool OwnsOrganizationCommand(Guid organization) => Owns(organization) && _organization.Value?.OrganizationCommand == true;
    internal bool OwnsAcceptedDeletion(Guid organization, Guid actor, Guid? request = null) =>
        OwnsOrganizationCommand(organization) && _organization.Value?.DeletionActor == actor
        && _organization.Value?.DeletionRequest is { } accepted && (request is null || request == accepted);
    public IDisposable Enter(Guid organization, bool organizationCommand = false, Guid? deletionRequest = null, Guid? deletionActor = null)
    {
        if (deletionRequest.HasValue != deletionActor.HasValue || deletionRequest == Guid.Empty || deletionActor == Guid.Empty
            || deletionRequest.HasValue && !organizationCommand) throw new InvalidOperationException("Accepted deletion scope is invalid.");
        var previous = _organization.Value; _organization.Value = (organization, organizationCommand, Guid.NewGuid(), deletionRequest, deletionActor);
        return new Lease(() => _organization.Value = previous);
    }
    private sealed class Lease(Action release) : IDisposable
    { public void Dispose() => release(); }
}
internal static class DemoRollback
{
    internal static Action Dictionary<TKey,TValue>(Dictionary<TKey,TValue> rows) where TKey : notnull
    {
        var snapshot = rows.ToArray();
        return () => { rows.Clear(); foreach (var entry in snapshot) rows.Add(entry.Key, entry.Value); };
    }
    internal static Action Set<T>(HashSet<T> rows)
    { var snapshot = rows.ToArray(); return () => { rows.Clear(); rows.UnionWith(snapshot); }; }
}
internal sealed partial class InMemoryWorkManagementStore : IDemoWorkTransactionParticipant
{
    public Action CaptureRollback()
    {
        lock (_sync)
        {
            Action[] restore = [DemoRollback.Dictionary(_audits), DemoRollback.Dictionary(_boards), DemoRollback.Dictionary(_boardAuthorityProofs), DemoRollback.Dictionary(_boardBackgroundImages), DemoRollback.Dictionary(_lists), DemoRollback.Dictionary(_cards),
                DemoRollback.Dictionary(_members), DemoRollback.Dictionary(_directoryPermissionRevisions), DemoRollback.Dictionary(_directoryReaderRevisions), DemoRollback.Dictionary(_directoryVisibilityRevisions), DemoRollback.Dictionary(_starred), DemoRollback.Dictionary(_starEvents), DemoRollback.Dictionary(_labels), DemoRollback.Set(_cardLabels),
                DemoRollback.Dictionary(_cardMembers), DemoRollback.Dictionary(_checklists), DemoRollback.Dictionary(_checklistItems),
                DemoRollback.Dictionary(_attachmentMetadata), DemoRollback.Dictionary(_attachmentIntegrity), DemoRollback.Dictionary(_uploads),
                DemoRollback.Dictionary(_cardCovers), DemoRollback.Dictionary(_comments), DemoRollback.Dictionary(_mentionSnapshots)];
            return () => { lock (_sync) foreach (var action in restore) action(); };
        }
    }
}
