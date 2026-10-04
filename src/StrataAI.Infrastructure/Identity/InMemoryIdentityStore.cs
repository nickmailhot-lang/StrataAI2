using StrataAI.Application.Identity;
using StrataAI.Application.Common;

namespace StrataAI.Infrastructure.Identity;

internal sealed partial class InMemoryIdentityStore(IClock clock, DemoMentionHandleRegistry handles) : IIdentityStore
{
    private sealed record TokenState(
        SecurityTokenRecord Token, DateTimeOffset? UsedAt = null);

    public Task<IdentitySecurityTokenProof?> FindSecurityTokenRetryProofAsync(string tokenHash, IdentityTokenPurpose purpose,
        DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            var tokens = purpose switch { IdentityTokenPurpose.ResetPassword => _passwordResetTokens, IdentityTokenPurpose.VerifyEmail => _emailVerificationTokens, _ => throw new ArgumentOutOfRangeException(nameof(purpose)) };
            if (!tokens.TryGetValue(tokenHash, out var state) || state.Token.ExpiresAt <= now || !_users.TryGetValue(state.Token.UserId, out var user))
                return Task.FromResult<IdentitySecurityTokenProof?>(null);
            return Task.FromResult<IdentitySecurityTokenProof?>(new(user, state.Token.Id, state.Token.ExpiresAt, state.UsedAt));
        }
    }

    private readonly object _sync = new();
    private readonly Dictionary<Guid, UserIdentity> _users = [];
    private readonly Dictionary<string, Guid> _usersByEmail = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SessionRecord> _sessions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SessionRecord> _revokedSessions = new(StringComparer.Ordinal);

    public Task<RevocationSessionProof?> FindRevocationSessionProofAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            var revoked = !_sessions.TryGetValue(tokenHash, out var session);
            if (revoked && !_revokedSessions.TryGetValue(tokenHash, out session)) return Task.FromResult<RevocationSessionProof?>(null);
            if (!_users.TryGetValue(session!.UserId, out var user)) return Task.FromResult<RevocationSessionProof?>(null);
            return Task.FromResult<RevocationSessionProof?>(new(user.Id, session.Id, session.ExpiresAt, revoked, user.Status, user.EmailVerified));
        }
    }
    private readonly Dictionary<string, TokenState> _passwordResetTokens = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TokenState> _emailVerificationTokens = new(StringComparer.Ordinal);

    public Task<bool> TryCreateUserAsync(
        UserIdentity user,
        SecurityTokenRecord? verificationToken,
        IdentityTokenDelivery? delivery,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_usersByEmail.ContainsKey(user.EmailNormalized))
            {
                return Task.FromResult(false);
            }

            _users[user.Id] = user;
            _usersByEmail[user.EmailNormalized] = user.Id;
            handles.Seed(user);
            if (verificationToken is not null)
                _emailVerificationTokens[verificationToken.TokenHash] = new TokenState(verificationToken);
            return Task.FromResult(true);
        }
    }

    public Task<UserIdentity?> FindUserByNormalizedEmailAsync(
        string emailNormalized,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (!_usersByEmail.TryGetValue(emailNormalized, out var userId))
            {
                return Task.FromResult<UserIdentity?>(null);
            }

            return Task.FromResult<UserIdentity?>(_users[userId]);
        }
    }

    public Task<UserIdentity?> FindUserByIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            _users.TryGetValue(userId, out var user);
            return Task.FromResult<UserIdentity?>(user);
        }
    }

    public Task UpdatePasswordHashAsync(
        Guid userId,
        string passwordHash,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_users.TryGetValue(userId, out var user))
            {
                _users[userId] = user with
                {
                    PasswordHash = passwordHash,
                    UpdatedAt = updatedAt,
                    Version = user.Version + 1,
                };
            }
        }

        return Task.CompletedTask;
    }

    public Task CreateSessionAsync(
        SessionRecord session,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            _sessions[session.TokenHash] = session;
        }

        return Task.CompletedTask;
    }

    public Task<AuthenticatedSession?> FindActiveSessionAsync(
        string tokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (!_sessions.TryGetValue(tokenHash, out var session) ||
                session.ExpiresAt <= now ||
                !_users.TryGetValue(session.UserId, out var user) ||
                user.Status != AccountStatus.Active)
            {
                return Task.FromResult<AuthenticatedSession?>(null);
            }

            return Task.FromResult<AuthenticatedSession?>(
                new AuthenticatedSession(session.Id, user, session.ExpiresAt));
        }
    }

    public Task RevokeSessionAsync(
        string tokenHash,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_sessions.Remove(tokenHash, out var session)) _revokedSessions[tokenHash] = session;
        }

        return Task.CompletedTask;
    }

    public Task CreatePasswordResetTokenAsync(
        SecurityTokenRecord token,
        IdentityTokenDelivery? delivery,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            _passwordResetTokens[token.TokenHash] = new TokenState(token);
        }

        return Task.CompletedTask;
    }

    public Task<Guid?> GetPasswordResetUserIdAsync(
        string tokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (!_passwordResetTokens.TryGetValue(tokenHash, out var state) ||
                state.UsedAt is not null ||
                state.Token.ExpiresAt <= now)
            {
                return Task.FromResult<Guid?>(null);
            }

            return Task.FromResult<Guid?>(state.Token.UserId);
        }
    }

    public Task<bool> CompletePasswordResetAsync(
        string tokenHash,
        string newPasswordHash,
        DateTimeOffset usedAt,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (!_passwordResetTokens.TryGetValue(tokenHash, out var state) ||
                state.UsedAt is not null ||
                state.Token.ExpiresAt <= usedAt ||
                !_users.TryGetValue(state.Token.UserId, out var user) ||
                user.Status is AccountStatus.Suspended or AccountStatus.Deactivated)
            {
                return Task.FromResult(false);
            }

            _passwordResetTokens[tokenHash] = state with { UsedAt = usedAt };
            _users[user.Id] = user with
            {
                PasswordHash = newPasswordHash,
                UpdatedAt = usedAt,
                Version = user.Version + 1,
            };

            RemoveSessionsForUser(user.Id);
            return Task.FromResult(true);
        }
    }

    public Task CreateEmailVerificationTokenAsync(
        SecurityTokenRecord token,
        IdentityTokenDelivery? delivery,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            _emailVerificationTokens[token.TokenHash] = new TokenState(token);
        }

        return Task.CompletedTask;
    }

    public Task<Guid?> GetEmailVerificationUserIdAsync(
        string tokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (!_emailVerificationTokens.TryGetValue(tokenHash, out var state) ||
                state.UsedAt is not null ||
                state.Token.ExpiresAt <= now)
            {
                return Task.FromResult<Guid?>(null);
            }

            return Task.FromResult<Guid?>(state.Token.UserId);
        }
    }

    public Task<bool> VerifyEmailAsync(
        string tokenHash,
        DateTimeOffset usedAt,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (!_emailVerificationTokens.TryGetValue(tokenHash, out var state) ||
                state.UsedAt is not null ||
                state.Token.ExpiresAt <= usedAt ||
                !_users.TryGetValue(state.Token.UserId, out var user) ||
                user.Status == AccountStatus.Deactivated)
            {
                return Task.FromResult(false);
            }

            _emailVerificationTokens[tokenHash] = state with { UsedAt = usedAt };
            _users[user.Id] = user with
            {
                EmailVerified = true,
                Status = user.Status == AccountStatus.PendingVerification
                    ? AccountStatus.Active
                    : user.Status,
                UpdatedAt = usedAt,
                Version = user.Version + 1,
            };

            return Task.FromResult(true);
        }
    }

    public Task<UserIdentity?> UpdateProfileAsync(
        Guid userId,
        string displayName,
        string? avatarUrl,
        string locale,
        string timezone,
        long expectedVersion,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (!_users.TryGetValue(userId, out var user) ||
                user.Version != expectedVersion || user.Status == AccountStatus.Deactivated)
            {
                return Task.FromResult<UserIdentity?>(null);
            }

            var updated = user with
            {
                DisplayName = displayName,
                AvatarUrl = avatarUrl,
                Locale = locale,
                Timezone = timezone,
                UpdatedAt = updatedAt,
                Version = user.Version + 1,
            };
            _users[userId] = updated;
            return Task.FromResult<UserIdentity?>(updated);
        }
    }

    public Task<bool> DeactivateUserAsync(
        Guid userId,
        DateTimeOffset deactivatedAt,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (!_users.TryGetValue(userId, out var user))
            {
                return Task.FromResult(false);
            }

            _users[userId] = user with
            {
                Status = AccountStatus.Deactivated,
                UpdatedAt = deactivatedAt,
                Version = user.Version + 1,
            };
            RemoveSessionsForUser(userId);
            return Task.FromResult(true);
        }
    }

    private readonly List<IdentityDomainEvent> _events = [];

    public Task<IdentityOperation<IdentityEventPage>> ReadEventsAsync(Guid userId, long? after,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            var subjectEvents = _events.Where(value => value.EntityId == userId).ToArray();
            var latest = subjectEvents.LongLength;
            if (after is < 0 || after > latest)
                return Task.FromResult(IdentityOperation<IdentityEventPage>.Failure("invalid_identity_cursor"));
            var events = after is null ? [] : subjectEvents.Where(value => value.Sequence > after).Take(100).ToArray();
            var cursor = after is null ? latest : events.LastOrDefault()?.Sequence ?? after.Value;
            return Task.FromResult(IdentityOperation<IdentityEventPage>.Success(new(cursor, latest, cursor < latest, events)));
        }
    }

    public Task AppendDomainEventAsync(Guid userId, string eventType, string correlationId,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            var user = _users[userId];
            var sequence = _events.LongCount(value => value.EntityId == userId) + 1;
            _events.Add(new(Guid.NewGuid(), sequence, eventType, userId, null, null, "User", userId,
                user.Version, new Dictionary<string,string>(), correlationId, clock.UtcNow));
        }
        return Task.CompletedTask;
    }

    public Task AppendAuditAsync(
        Guid? actorId,
        string eventType,
        string entityType,
        Guid? entityId,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    private void RemoveSessionsForUser(Guid userId)
    {
        var hashes = _sessions
            .Where(pair => pair.Value.UserId == userId)
            .Select(pair => pair.Key)
            .ToArray();

        foreach (var hash in hashes)
        {
            if (_sessions.Remove(hash, out var session)) _revokedSessions[hash] = session;
        }
    }
}
