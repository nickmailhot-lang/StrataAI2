using StrataAI.Application.Common;

namespace StrataAI.Application.Identity;

public interface ICommandActorContext
{
    bool HasHttpRequest { get; }
    Guid? AuthenticatedUserId { get; }
    string? SessionTokenHash { get; }
}

public interface ICommandActorAuthorization
{
    Task<bool> VerifyAsync(Guid actorId, CancellationToken cancellationToken = default);
}

public sealed class CommandActorAuthorization(
    ICommandActorContext context, IIdentityStore identities, IClock clock,
    IdentityPolicy policy) : ICommandActorAuthorization
{
    public async Task<bool> VerifyAsync(Guid actorId, CancellationToken cancellationToken = default)
    {
        if (context.HasHttpRequest && (context.AuthenticatedUserId != actorId || context.SessionTokenHash is null))
            return false;
        // Inside a PostgreSQL command this locks the account before the session.
        // Deactivation takes the same order, avoiding user/session lock inversion.
        var user = await identities.FindUserByIdAsync(actorId, cancellationToken);
        if (user is not { Status: AccountStatus.Active } || (policy.RequireVerifiedEmail && !user.EmailVerified))
            return false;
        // Trusted in-process service calls still require an active account. HTTP
        // calls additionally require the original authenticated session to remain valid.
        if (!context.HasHttpRequest) return true;
        var session = await identities.FindActiveSessionAsync(context.SessionTokenHash!, clock.UtcNow, cancellationToken);
        return session?.User.Id == actorId && session.User.Status == AccountStatus.Active && session.ExpiresAt > clock.UtcNow;
    }
}
