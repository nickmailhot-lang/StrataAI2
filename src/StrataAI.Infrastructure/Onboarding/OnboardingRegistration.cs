using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using StrataAI.Application.Onboarding;
using StrataAI.Application.Runtime;
using StrataAI.Application.Organizations;

namespace StrataAI.Infrastructure.Onboarding;

public static class OnboardingRegistration
{
    public static void AddStrataAiOnboarding(
        this IServiceCollection services,
        RuntimeDescriptor runtime, IConfiguration configuration)
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
        services.AddSingleton<IInvitationHistoryStore>(provider => (IInvitationHistoryStore)provider.GetRequiredService<IInvitationStore>());
        services.AddSingleton<InvitationHistoryService>();
        if (InvitationMailRegistration.IsEnabled(configuration, runtime))
            services.AddSingleton<IInvitationMailPublisher, PostgresInvitationMailPublisher>();
    }
}
