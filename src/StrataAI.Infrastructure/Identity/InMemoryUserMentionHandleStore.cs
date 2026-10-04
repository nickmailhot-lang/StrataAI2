using StrataAI.Application.Identity;
using StrataAI.Domain.WorkManagement;

namespace StrataAI.Infrastructure.Identity;

internal sealed class DemoMentionHandleRegistry : IDemoIdentityTransactionParticipant
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, UserMentionHandle> _current = [];
    private readonly Dictionary<string, Guid> _reserved = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, int> _counts = [];
    // Called by canonical account insertion; no client can seed arbitrary aliases.
    public void Seed(UserIdentity user)
    {
        lock (_sync)
        {
            if (_current.ContainsKey(user.Id)) return;
            var handle = MentionHandle.DefaultForUser(user.Id); var utc = user.CreatedAt.ToUniversalTime();
            var at = new DateTimeOffset(utc.Ticks - utc.Ticks % 10, TimeSpan.Zero);
            _current.Add(user.Id, new(user.Id, handle, at, at, 1)); _reserved.Add(handle, user.Id); _counts.Add(user.Id, 1);
        }
    }
    public UserMentionHandle? Find(Guid user) { lock (_sync) return _current.GetValueOrDefault(user); }
    public IdentityOperation<UserMentionHandleChange> Claim(Guid user, string normalized, long version, DateTimeOffset at)
    {
        lock (_sync)
        {
            if (!_current.TryGetValue(user, out var current)) return IdentityOperation<UserMentionHandleChange>.Failure("mention_handle_unavailable");
            if (current.Version != version) return IdentityOperation<UserMentionHandleChange>.Failure("version_conflict");
            if (current.Handle == normalized) return IdentityOperation<UserMentionHandleChange>.Success(new(current, false));
            if (current.Version == long.MaxValue || current.UpdatedAt > at) return IdentityOperation<UserMentionHandleChange>.Failure("version_conflict");
            if (_reserved.TryGetValue(normalized, out var owner))
            {
                if (owner != user) return IdentityOperation<UserMentionHandleChange>.Failure("mention_handle_unavailable");
            }
            else
            {
                if (_counts[user] >= MentionHandle.MaximumLifetimeReservations)
                    return IdentityOperation<UserMentionHandleChange>.Failure("mention_handle_claim_refused");
                _reserved.Add(normalized, user); _counts[user]++;
            }
            var next = current with { Handle = normalized, UpdatedAt = at, Version = current.Version + 1 };
            _current[user] = next; return IdentityOperation<UserMentionHandleChange>.Success(new(next, true));
        }
    }
    public Action CaptureRollback()
    {
        lock (_sync)
        {
            Action[] restore = [DemoIdentityRollback.Dictionary(_current), DemoIdentityRollback.Dictionary(_reserved), DemoIdentityRollback.Dictionary(_counts)];
            return () => { lock (_sync) foreach (var action in restore) action(); };
        }
    }
}
internal sealed class InMemoryUserMentionHandleStore(DemoMentionHandleRegistry registry, DemoIdentityTransactionScope scope) : IUserMentionHandleStore
{
    private void RequireScope(Guid user)
    {
        if (!scope.Owns(user)) throw new InvalidOperationException("Mention handles require an owning identity subject transaction.");
    }
    public Task<UserMentionHandle?> FindAsync(Guid userId, CancellationToken cancellationToken)
    { RequireScope(userId); cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(registry.Find(userId)); }
    public Task<IdentityOperation<UserMentionHandleChange>> ClaimAsync(Guid userId, string handle, long expectedVersion,
        DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        RequireScope(userId); cancellationToken.ThrowIfCancellationRequested();
        string normalized;
        try
        {
            normalized = MentionHandle.Normalize(handle);
            if (normalized.StartsWith("u_", StringComparison.Ordinal) && normalized != MentionHandle.DefaultForUser(userId))
                return Task.FromResult(IdentityOperation<UserMentionHandleChange>.Failure("mention_handle_invalid"));
        }
        catch (ArgumentException) { return Task.FromResult(IdentityOperation<UserMentionHandleChange>.Failure("mention_handle_invalid")); }
        if (expectedVersion < 1) return Task.FromResult(IdentityOperation<UserMentionHandleChange>.Failure("invalid_version"));
        var utc = updatedAt.ToUniversalTime(); var at = new DateTimeOffset(utc.Ticks - utc.Ticks % 10, TimeSpan.Zero);
        return Task.FromResult(registry.Claim(userId, normalized, expectedVersion, at));
    }
}
