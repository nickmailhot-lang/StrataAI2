using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;

namespace StrataAI.Api.Auth;

public static class SecurityRateLimits
{
    public static void AddSecurityRateLimits(this IServiceCollection services, IConfiguration configuration)
    {
        var authPermits = ReadLimit(configuration, "STRATAAI_AUTH_REQUESTS_PER_MINUTE", 60);
        var invitationPermits = ReadLimit(configuration, "STRATAAI_INVITATION_REQUESTS_PER_MINUTE", 60);
        services.AddRateLimiter(options =>
        {
            options.AddPolicy("auth", context => CreatePartition(context, authPermits, false));
            options.AddPolicy("invitation", context => CreatePartition(context, invitationPermits, true));
            options.AddPolicy("client-events", context => CreatePartition(context, 64, true));
            options.OnRejected = async (rejected, cancellationToken) =>
            {
                var seconds = rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                    ? Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
                    : 60;
                rejected.HttpContext.Response.Headers["Retry-After"] = seconds.ToString(CultureInfo.InvariantCulture);
                rejected.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("SecurityRateLimits")
                    .LogWarning("RATE_LIMIT_TRIGGERED CorrelationId={CorrelationId}", rejected.HttpContext.TraceIdentifier);
                await Results.Problem(
                    statusCode: StatusCodes.Status429TooManyRequests,
                    title: "Too many requests. Please wait and retry.",
                    extensions: new Dictionary<string, object?> { ["code"] = "rate_limit_exceeded" })
                    .ExecuteAsync(rejected.HttpContext);
            };
        });
    }

    private static RateLimitPartition<string> CreatePartition(HttpContext context, int permits, bool byUser)
    {
        // Never accept arbitrary forwarding headers as identity. Nginx separately
        // limits actual edge peers; direct API clients are limited by socket peer.
        var key = (byUser ? context.User.FindFirstValue(ClaimTypes.NameIdentifier) : null)
            ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown-peer";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permits,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true,
        });
    }

    private static int ReadLimit(IConfiguration configuration, string name, int fallback)
    {
        var value = configuration[name];
        if (value is null) return fallback;
        if (int.TryParse(value, out var limit) && limit is >= 10 and <= 1000) return limit;
        throw new InvalidOperationException($"{name} must be an integer from 10 to 1000.");
    }
}
