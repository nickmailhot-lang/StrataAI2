using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Identity;
using StrataAI.Application.Runtime;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Identity;

public static class IdentityRegistration
{
    public static void AddStrataAiIdentity(
        this IServiceCollection services,
        IConfiguration configuration,
        RuntimeDescriptor runtime)
    {
        var emailEnabled=services.AddIdentityDeliveryTokens(configuration,runtime);
        var allowSelfRegistration = GetBoolean(
            configuration["STRATAAI_AUTH_ALLOW_SELF_REGISTRATION"],
            runtime.Mode == RuntimeMode.Demo);

        var requireVerifiedEmail = GetBoolean(
            configuration["STRATAAI_AUTH_REQUIRE_VERIFIED_EMAIL"],
            runtime.Mode == RuntimeMode.Production);

        var minimumPasswordLength = GetInteger(
            configuration["STRATAAI_AUTH_MIN_PASSWORD_LENGTH"],
            12,
            minimum: 8,
            maximum: 128);

        var sessionHours = GetInteger(
            configuration["STRATAAI_AUTH_SESSION_HOURS"],
            12,
            minimum: 1,
            maximum: 720);

        var tokenMinutes = GetInteger(
            configuration["STRATAAI_AUTH_SECURITY_TOKEN_MINUTES"],
            30,
            minimum: 5,
            maximum: 1440);

        services.AddSingleton(
            new IdentityPolicy(
                allowSelfRegistration,
                requireVerifiedEmail,
                minimumPasswordLength,
                TimeSpan.FromHours(sessionHours),
                TimeSpan.FromMinutes(tokenMinutes),emailEnabled));

        services.AddSingleton<IPasswordHashService, AspNetPasswordHashService>();
        services.AddSingleton<ISecureTokenService, SecureTokenService>();

        if (runtime.Mode == RuntimeMode.Demo)
        {
            services.AddSingleton<IIdentityStore, InMemoryIdentityStore>();
        }
        else
        {
            services.AddSingleton<IIdentityStore, PostgresIdentityStore>();
        }

        services.AddSingleton<IIdentityService, IdentityService>();
    }

    private static bool GetBoolean(string? value, bool fallback) =>
        bool.TryParse(value, out var parsed) ? parsed : fallback;

    private static int GetInteger(
        string? value,
        int fallback,
        int minimum,
        int maximum)
    {
        if (!int.TryParse(value, out var parsed))
        {
            return fallback;
        }

        return Math.Clamp(parsed, minimum, maximum);
    }
}
