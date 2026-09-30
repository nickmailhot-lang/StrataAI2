using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Onboarding;
using StrataAI.Application.Runtime;
using StrataAI.Application.Organizations;

namespace StrataAI.Infrastructure.Onboarding;

public static class OnboardingRegistration
{
    public static void AddStrataAiOnboarding(
        this IServiceCollection services,
        RuntimeDescriptor runtime)
    {
        if (runtime.Mode == RuntimeMode.Demo)
        {
            services.AddSingleton<IInvitationStore, InMemoryInvitationStore>();
        }
        else
        {
            services.AddSingleton<IInvitationStore, PostgresInvitationStore>();
        }

        services.AddSingleton<IInvitationService, InvitationService>();
    }
}
