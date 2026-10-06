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
            services.AddSingleton<InMemoryInvitationStore>();
            services.AddSingleton<IInvitationStore>(provider => provider.GetRequiredService<InMemoryInvitationStore>());
            services.AddSingleton<StrataAI.Infrastructure.Organizations.IDemoOrganizationTransactionParticipant>(provider => provider.GetRequiredService<InMemoryInvitationStore>());
        }
        else
        {
            services.AddSingleton<IInvitationStore, PostgresInvitationStore>();
        }

        services.AddSingleton<IInvitationService, InvitationService>();
        services.AddSingleton<BoardInvitationService>();
        services.AddSingleton<IInvitationHistoryStore>(provider => (IInvitationHistoryStore)provider.GetRequiredService<IInvitationStore>());
        services.AddSingleton<InvitationHistoryService>();
        if (InvitationMailRegistration.IsEnabled(configuration, runtime))
            services.AddSingleton<IInvitationMailPublisher, PostgresInvitationMailPublisher>();
    }
}
