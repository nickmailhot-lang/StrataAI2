using StrataAI.Application.Identity;

namespace StrataAI.Infrastructure.Identity;

internal interface IDemoIdentityTransactionParticipant { Action CaptureRollback(); }
internal sealed class DemoIdentityTransactionScope
{
    private sealed record State(Guid? Subject, Guid CommandId, bool WorkCleanup);
    private readonly AsyncLocal<State?> _current = new();
    public bool Active => _current.Value is not null;
    public Guid CommandId => _current.Value?.CommandId ?? Guid.Empty;
    public bool Owns(Guid subject) => subject != Guid.Empty && _current.Value?.Subject == subject;
    internal bool OwnsWorkCleanup => _current.Value?.WorkCleanup == true;
    public IDisposable Enter(Guid? subject, bool workCleanup = false)
    {
        if (Active) throw new InvalidOperationException("Nested identity transactions are unavailable.");
        _current.Value = new(subject, Guid.NewGuid(), workCleanup); return new Lease(() => _current.Value = null);
    }
    private sealed class Lease(Action release) : IDisposable { public void Dispose() => release(); }
}
internal static class DemoIdentityRollback
{
    public static Action Dictionary<TKey,TValue>(Dictionary<TKey,TValue> rows) where TKey : notnull
    {
        var snapshot = rows.ToArray();
        return () => { rows.Clear(); foreach (var row in snapshot) rows.Add(row.Key, row.Value); };
    }
}
internal sealed partial class InMemoryIdentityStore : IDemoIdentityTransactionParticipant
{
    public Action CaptureRollback()
    {
        lock (_sync)
        {
            var events = _events.ToArray();
            Action[] restore = [DemoIdentityRollback.Dictionary(_users), DemoIdentityRollback.Dictionary(_usersByEmail),
                DemoIdentityRollback.Dictionary(_sessions), DemoIdentityRollback.Dictionary(_revokedSessions),
                DemoIdentityRollback.Dictionary(_passwordResetTokens), DemoIdentityRollback.Dictionary(_emailVerificationTokens),
                DemoIdentityRollback.Dictionary(_issuerAuthorityProofs)];
            return () => { lock (_sync) { foreach (var action in restore) action(); _events.Clear(); _events.AddRange(events); } };
        }
    }
}
internal sealed partial class InMemoryIdentityProfileReplayStore : IDemoIdentityTransactionParticipant
{ public Action CaptureRollback() => DemoIdentityRollback.Dictionary(_records); }
internal sealed partial class InMemoryIdentityRevocationReplayStore : IDemoIdentityTransactionParticipant
{ public Action CaptureRollback() => DemoIdentityRollback.Dictionary(_receipts); }
internal sealed partial class InMemoryIdentityLoginReplayStore : IDemoIdentityTransactionParticipant
{ public Action CaptureRollback() => DemoIdentityRollback.Dictionary(_rows); }
internal sealed partial class InMemoryIdentityRegistrationReplayStore : IDemoIdentityTransactionParticipant
{ public Action CaptureRollback() => DemoIdentityRollback.Dictionary(_rows); }
internal sealed partial class InMemoryIdentityRecoveryRequestReplayStore : IDemoIdentityTransactionParticipant
{ public Action CaptureRollback() => DemoIdentityRollback.Dictionary(rows); }
internal sealed partial class InMemoryIdentityTokenConsumptionReplayStore : IDemoIdentityTransactionParticipant
{ public Action CaptureRollback() => DemoIdentityRollback.Dictionary(rows); }
