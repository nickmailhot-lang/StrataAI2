using System.Security.Claims;
using StrataAI.Application.Identity;

namespace StrataAI.Api.Auth;

internal sealed class HttpCommandActorContext(IHttpContextAccessor contexts) : ICommandActorContext
{
    public bool HasHttpRequest => contexts.HttpContext is not null;
    public Guid? AuthenticatedUserId => Guid.TryParse(contexts.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    public string? SessionTokenHash => contexts.HttpContext?.Items[typeof(ICommandActorContext)] as string;
}
