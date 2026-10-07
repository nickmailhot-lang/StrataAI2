using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        // Standalone module composition needs the same transaction diagnostics
        // as the API/Worker hosts. AddLogging preserves host-configured providers.
        services.AddLogging();
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
        AddLoginRetrySecrets(services, configuration, runtime);

        if (runtime.Mode == RuntimeMode.Demo)
        {
            services.AddSingleton<DemoIdentityTransactionScope>();
            services.AddSingleton<DemoMentionHandleRegistry>();
            services.AddSingleton<IDemoIdentityTransactionParticipant>(provider => provider.GetRequiredService<DemoMentionHandleRegistry>());
            services.AddSingleton<IUserMentionHandleStore, InMemoryUserMentionHandleStore>();
            services.AddSingleton<IIdentityHandleClaimReplayStore, InMemoryIdentityHandleClaimReplayStore>();
            services.AddSingleton<IDemoIdentityTransactionParticipant>(provider => (IDemoIdentityTransactionParticipant)provider.GetRequiredService<IIdentityHandleClaimReplayStore>());
            services.AddSingleton<IInvitationRegistrationProofStore, InMemoryInvitationRegistrationProofStore>();
            services.TryAddSingleton<InMemoryAccountOrganizationGate>();
            services.AddSingleton<IAccountDeactivationOwnership, InMemoryAccountDeactivationOwnership>();
            services.AddSingleton<IIdentityStore>(provider =>
            {
                var store = new InMemoryIdentityStore(
                    provider.GetRequiredService<StrataAI.Application.Common.IClock>(),
                    provider.GetRequiredService<DemoMentionHandleRegistry>(),
                    provider.GetRequiredService<DemoIdentityTransactionScope>(),
                    () => provider.GetRequiredService<Func<StrataAI.Infrastructure.Onboarding.IDemoIssuerAuthorityProjection>>()());
                store.SeedTestAccount(provider.GetRequiredService<IPasswordHashService>());
                return store;
            });
            services.AddSingleton<IIdentityUnitOfWork, InMemoryIdentityUnitOfWork>();
            services.AddSingleton<IIdentityProfileReplayStore, InMemoryIdentityProfileReplayStore>();
            services.AddSingleton<IIdentityRevocationReplayStore, InMemoryIdentityRevocationReplayStore>();
            services.AddSingleton<IIdentityLoginReplayStore, InMemoryIdentityLoginReplayStore>();
            services.AddSingleton<IIdentityRegistrationReplayStore, InMemoryIdentityRegistrationReplayStore>();
            services.AddSingleton<IIdentityRecoveryRequestReplayStore, InMemoryIdentityRecoveryRequestReplayStore>();
            services.AddSingleton<IIdentityTokenConsumptionReplayStore, InMemoryIdentityTokenConsumptionReplayStore>();
            services.AddSingleton<IDemoIdentityTransactionParticipant>(provider => (IDemoIdentityTransactionParticipant)provider.GetRequiredService<IIdentityStore>());
            services.AddSingleton<IDemoIdentityTransactionParticipant>(provider => (IDemoIdentityTransactionParticipant)provider.GetRequiredService<IIdentityProfileReplayStore>());
            services.AddSingleton<IDemoIdentityTransactionParticipant>(provider => (IDemoIdentityTransactionParticipant)provider.GetRequiredService<IIdentityRevocationReplayStore>());
            services.AddSingleton<IDemoIdentityTransactionParticipant>(provider => (IDemoIdentityTransactionParticipant)provider.GetRequiredService<IIdentityLoginReplayStore>());
            services.AddSingleton<IDemoIdentityTransactionParticipant>(provider => (IDemoIdentityTransactionParticipant)provider.GetRequiredService<IIdentityRegistrationReplayStore>());
            services.AddSingleton<IDemoIdentityTransactionParticipant>(provider => (IDemoIdentityTransactionParticipant)provider.GetRequiredService<IIdentityRecoveryRequestReplayStore>());
            services.AddSingleton<IDemoIdentityTransactionParticipant>(provider => (IDemoIdentityTransactionParticipant)provider.GetRequiredService<IIdentityTokenConsumptionReplayStore>());
        }
        else
        {
            services.AddSingleton<IInvitationRegistrationProofStore, PostgresInvitationRegistrationProofStore>();
            services.AddSingleton<IAccountDeactivationOwnership, PostgresAccountDeactivationOwnership>();
            services.AddSingleton<IIdentityStore, PostgresIdentityStore>();
            services.AddSingleton<IUserMentionHandleStore, PostgresUserMentionHandleStore>();
            services.AddSingleton<IIdentityHandleClaimReplayStore, PostgresIdentityHandleClaimReplayStore>();
            services.AddSingleton<IIdentityUnitOfWork, PostgresIdentityUnitOfWork>();
            services.AddSingleton<IIdentityProfileReplayStore, PostgresIdentityProfileReplayStore>();
            services.AddSingleton<IIdentityRevocationReplayStore, PostgresIdentityRevocationReplayStore>();
            services.AddSingleton<IIdentityLoginReplayStore, PostgresIdentityLoginReplayStore>();
            services.AddSingleton<IIdentityRegistrationReplayStore, PostgresIdentityRegistrationReplayStore>();
            services.AddSingleton<IIdentityRecoveryRequestReplayStore, PostgresIdentityRecoveryRequestReplayStore>();
            services.AddSingleton<IIdentityTokenConsumptionReplayStore, PostgresIdentityTokenConsumptionReplayStore>();
        }

        services.AddSingleton<IdentityService>();
        services.AddSingleton<UserMentionHandleService>();
        services.AddSingleton<IdentityRevocationReplayExecutor>();
        services.AddSingleton<IIdentityService>(provider => new TransactionalIdentityService(
            provider.GetRequiredService<IdentityService>(), provider.GetRequiredService<IIdentityUnitOfWork>(),
            provider.GetRequiredService<IIdentityCommandContext>(), provider.GetRequiredService<IIdentityProfileReplayStore>(),
            provider.GetRequiredService<ISecureTokenService>(), provider.GetRequiredService<ICommandActorAuthorization>()));
    }

    private static void AddLoginRetrySecrets(IServiceCollection services, IConfiguration configuration, RuntimeDescriptor runtime)
    {
        services.AddSingleton<IIdentityRegistrationRetrySecrets>(provider => provider.GetRequiredService<IdentityLoginRetrySecrets>());
        services.AddSingleton<IIdentityRecoveryRetrySecrets>(provider => provider.GetRequiredService<IdentityLoginRetrySecrets>());
        services.AddSingleton<IIdentityTokenConsumptionRetrySecrets>(provider => provider.GetRequiredService<IdentityLoginRetrySecrets>());
        services.AddSingleton<IIdentityLoginRetrySecrets>(provider => provider.GetRequiredService<IdentityLoginRetrySecrets>());
        if (runtime.Mode == RuntimeMode.Demo)
        {
            services.AddSingleton<IdentityLoginRetrySecrets>(_ => new IdentityLoginRetrySecrets("demo-ephemeral",
                new Dictionary<string, string> { ["demo-ephemeral"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }));
            return;
        }
        IdentityLoginRetrySecrets signer;
        try
        {
            using var document = JsonDocument.Parse(configuration["STRATAAI_AUTH_RETRY_KEYS"] ?? "");
            var keys = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in document.RootElement.EnumerateObject()) keys.Add(entry.Name, entry.Value.GetString()!);
            signer = new IdentityLoginRetrySecrets(configuration["STRATAAI_AUTH_RETRY_CURRENT_KEY"] ?? "", keys);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException)
        { throw new InvalidOperationException("Production sign-in retry keys require a valid current version and a unique JSON key ring of base64 32-byte secrets."); }
        services.AddSingleton<IdentityLoginRetrySecrets>(_ => signer);
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
