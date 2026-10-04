using System.Security.Cryptography;
using System.Text.Json;
using StrataAI.Application.Common;
using StrataAI.Domain.WorkManagement;

namespace StrataAI.Application.Identity;

public sealed record ClaimMentionHandleInput(string Handle, long UserVersion, long HandleVersion);
public sealed record UserMentionHandleSetting(Guid UserId, string Handle, long UserVersion, long HandleVersion,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record HandleClaimAcknowledgment(Guid UserId, string Handle, long UserVersion, long HandleVersion, bool Changed);

public sealed class UserMentionHandleService(IIdentityUnitOfWork unit, ICommandActorAuthorization actors,
    IIdentityStore identities, IUserMentionHandleStore handles, IIdentityHandleClaimReplayStore replays, IClock clock)
{
    private sealed record State(UserIdentity User, UserMentionHandle Handle);
    private async Task<IdentityOperation<State>> Current(Guid user, CancellationToken ct)
    {
        if (!await actors.VerifyAsync(user, ct)) return IdentityOperation<State>.Failure("session_unavailable");
        var account = await identities.FindUserByIdAsync(user, ct);
        if (account is not { Status: AccountStatus.Active } || account.Id != user || account.Version < 1)
            return IdentityOperation<State>.Failure("account_unavailable");
        var handle = await handles.FindAsync(user, ct);
        if (handle is null || handle.UserId != user || handle.Version < 1)
            return IdentityOperation<State>.Failure("mention_handle_unavailable");
        if (!await actors.VerifyAsync(user, ct)) return IdentityOperation<State>.Failure("session_unavailable");
        return IdentityOperation<State>.Success(new(account, handle));
    }
    public Task<IdentityOperation<UserMentionHandleSetting>> GetAsync(Guid user, CancellationToken ct = default)
        => unit.ExecuteAsync(user, async () =>
        {
            var admitted = await Current(user, ct);
            return admitted.Succeeded ? IdentityOperation<UserMentionHandleSetting>.Success(new(user, admitted.Value!.Handle.Handle,
                admitted.Value.User.Version, admitted.Value.Handle.Version, admitted.Value.Handle.CreatedAt, admitted.Value.Handle.UpdatedAt))
                : IdentityOperation<UserMentionHandleSetting>.Failure(admitted.ErrorCode!);
        }, ct);
    public Task<IdentityOperation<HandleClaimAcknowledgment>> ClaimAsync(Guid user, Guid key, ClaimMentionHandleInput input,
        string correlationId, CancellationToken ct = default)
        => unit.ExecuteAsync(user, async () =>
        {
            if (key == Guid.Empty) return IdentityOperation<HandleClaimAcknowledgment>.Failure("invalid_idempotency_key");
            if (input is null) return IdentityOperation<HandleClaimAcknowledgment>.Failure("mention_handle_invalid");
            if (input.UserVersion < 1 || input.HandleVersion < 1) return IdentityOperation<HandleClaimAcknowledgment>.Failure("invalid_version");
            if (string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > 120)
                return IdentityOperation<HandleClaimAcknowledgment>.Failure("invalid_correlation_id");
            string normalized;
            try
            {
                normalized = MentionHandle.Normalize(input.Handle);
                if (normalized.StartsWith("u_", StringComparison.Ordinal) && normalized != MentionHandle.DefaultForUser(user))
                    return IdentityOperation<HandleClaimAcknowledgment>.Failure("mention_handle_invalid");
            }
            catch (ArgumentException) { return IdentityOperation<HandleClaimAcknowledgment>.Failure("mention_handle_invalid"); }
            var admitted = await Current(user, ct);
            if (!admitted.Succeeded) return IdentityOperation<HandleClaimAcknowledgment>.Failure(admitted.ErrorCode!);
            var state = admitted.Value!;
            var fingerprint = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
                new { Operation = "HANDLE_CLAIM", Handle = normalized, input.UserVersion, input.HandleVersion })));
            var prior = await replays.ReadAsync(user, key, ct);
            if (prior is not null)
            {
                if (prior.Expired) return IdentityOperation<HandleClaimAcknowledgment>.Failure("idempotency_key_expired");
                if (prior.Fingerprint != fingerprint) return IdentityOperation<HandleClaimAcknowledgment>.Failure("idempotency_key_reused");
                var delta = prior.Receipt.Changed ? 1 : 0;
                if (input.UserVersion > long.MaxValue - delta || input.HandleVersion > long.MaxValue - delta
                    || prior.Receipt.UserVersion != input.UserVersion + delta || prior.Receipt.HandleVersion != input.HandleVersion + delta
                    || state.User.Version < prior.Receipt.UserVersion || state.Handle.Version != prior.Receipt.HandleVersion || state.Handle.Handle != normalized)
                    return IdentityOperation<HandleClaimAcknowledgment>.Failure("mention_handle_unavailable");
                if (!await actors.VerifyAsync(user, ct)) return IdentityOperation<HandleClaimAcknowledgment>.Failure("session_unavailable");
                return IdentityOperation<HandleClaimAcknowledgment>.Success(new(user, state.Handle.Handle,
                    prior.Receipt.UserVersion, prior.Receipt.HandleVersion, prior.Receipt.Changed));
            }
            if (state.User.Version != input.UserVersion || state.Handle.Version != input.HandleVersion)
                return IdentityOperation<HandleClaimAcknowledgment>.Failure("version_conflict");
            if (state.Handle.Handle != normalized && state.User.Version == long.MaxValue)
                return IdentityOperation<HandleClaimAcknowledgment>.Failure("version_conflict");
            var at = clock.UtcNow;
            var changed = await handles.ClaimAsync(user, normalized, input.HandleVersion, at, ct);
            if (!changed.Succeeded) return IdentityOperation<HandleClaimAcknowledgment>.Failure(changed.ErrorCode!);
            var account = state.User;
            if (changed.Value!.Changed)
            {
                var updated = await identities.UpdateProfileAsync(user, account.DisplayName, account.AvatarUrl, account.Locale,
                    account.Timezone, input.UserVersion, at, ct);
                if (updated is null || updated.Id != user || updated.Version != input.UserVersion + 1)
                    return IdentityOperation<HandleClaimAcknowledgment>.Failure("version_conflict");
                account = updated;
                await identities.AppendAuditAsync(user, "USER_PROFILE_UPDATED", "User", user, correlationId, ct);
                await identities.AppendDomainEventAsync(user, "USER_PROFILE_UPDATED", correlationId, ct);
            }
            var receipt = new HandleClaimReceipt(account.Version, changed.Value.Current.Version, changed.Value.Changed);
            if (!await replays.TrySaveAsync(user, key, fingerprint, receipt, ct))
                return IdentityOperation<HandleClaimAcknowledgment>.Failure("identity_storage_unavailable");
            // Generic identity commands include deliberate logout. This producer
            // therefore owns its final current-session admission explicitly.
            if (!await actors.VerifyAsync(user, ct)) return IdentityOperation<HandleClaimAcknowledgment>.Failure("session_unavailable");
            ct.ThrowIfCancellationRequested();
            return IdentityOperation<HandleClaimAcknowledgment>.Success(new(user, changed.Value.Current.Handle,
                receipt.UserVersion, receipt.HandleVersion, receipt.Changed));
        }, ct);
}
