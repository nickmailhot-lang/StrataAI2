namespace StrataAI.Infrastructure.WorkManagement;

internal interface IDemoWorkTransactionParticipant
{
    Action CaptureRollback();
}
internal sealed class DemoWorkTransactionScope
{
    private readonly AsyncLocal<(Guid Organization, bool OrganizationCommand, Guid CommandId)?> _organization = new();
    public Guid CommandId => _organization.Value?.CommandId ?? Guid.Empty;
    public bool Owns(Guid organization) => _organization.Value?.Organization == organization;
    public bool OwnsOrganizationCommand(Guid organization) => Owns(organization) && _organization.Value?.OrganizationCommand == true;
    public IDisposable Enter(Guid organization, bool organizationCommand = false)
    {
        var previous = _organization.Value; _organization.Value = (organization, organizationCommand, Guid.NewGuid());
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
            Action[] restore = [DemoRollback.Dictionary(_boards), DemoRollback.Dictionary(_boardBackgroundImages), DemoRollback.Dictionary(_lists), DemoRollback.Dictionary(_cards),
                DemoRollback.Dictionary(_members), DemoRollback.Dictionary(_directoryPermissionRevisions), DemoRollback.Dictionary(_directoryReaderRevisions), DemoRollback.Dictionary(_directoryVisibilityRevisions), DemoRollback.Dictionary(_starred), DemoRollback.Dictionary(_starEvents), DemoRollback.Dictionary(_labels), DemoRollback.Set(_cardLabels),
                DemoRollback.Dictionary(_cardMembers), DemoRollback.Dictionary(_checklists), DemoRollback.Dictionary(_checklistItems),
                DemoRollback.Dictionary(_attachmentMetadata), DemoRollback.Dictionary(_attachmentIntegrity), DemoRollback.Dictionary(_uploads),
                DemoRollback.Dictionary(_cardCovers), DemoRollback.Dictionary(_comments), DemoRollback.Dictionary(_mentionSnapshots)];
            return () => { lock (_sync) foreach (var action in restore) action(); };
        }
    }
}
