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
        services.AddSingleton<IInvitationRecipientCursorCodec, DataProtectedInvitationRecipientCursorCodec>();
        if (runtime.Mode == RuntimeMode.Demo)
        {
            services.AddSingleton<InMemoryInvitationRecipientJournal>();
            services.AddSingleton<StrataAI.Infrastructure.Identity.IDemoIdentityTransactionParticipant>(p => p.GetRequiredService<InMemoryInvitationRecipientJournal>());
            services.AddSingleton<InMemoryIssuerAuthorityProjection>();
            services.AddSingleton<Func<IDemoIssuerAuthorityProjection>>(p => () => p.GetRequiredService<InMemoryIssuerAuthorityProjection>());
            services.AddSingleton<StrataAI.Infrastructure.WorkManagement.IDemoWorkTransactionParticipant>(p => p.GetRequiredService<InMemoryInvitationRecipientJournal>());
            services.AddSingleton<InMemoryBoardAuthorityProjection>();
            services.AddSingleton<Func<IDemoBoardAuthorityProjection>>(p => () => p.GetRequiredService<InMemoryBoardAuthorityProjection>());
            services.AddSingleton<IInvitationRecipientEventReader>(p => p.GetRequiredService<InMemoryInvitationRecipientJournal>());
            services.AddSingleton<StrataAI.Infrastructure.Organizations.IDemoOrganizationTransactionParticipant>(p => p.GetRequiredService<InMemoryInvitationRecipientJournal>());
            services.AddSingleton<InMemoryInvitationAuditProjection>();
            // Resolve the projection at audit append, after the Organization
            // store is constructed; invitation/work stores depend on that store.
            services.AddSingleton<Func<IDemoInvitationAuditProjection>>(p => () => p.GetRequiredService<InMemoryInvitationAuditProjection>());
            services.AddSingleton<InMemoryInvitationStore>();
            services.AddSingleton<IInvitationStore>(provider => provider.GetRequiredService<InMemoryInvitationStore>());
            services.AddSingleton<StrataAI.Infrastructure.Organizations.IDemoOrganizationTransactionParticipant>(provider => provider.GetRequiredService<InMemoryInvitationStore>());
        }
        else
        {
            services.AddSingleton<IInvitationStore, PostgresInvitationStore>();
            services.AddSingleton<IInvitationRecipientEventReader, PostgresInvitationRecipientEventReader>();
            services.AddSingleton<IInvitationRecipientAuthorityDependencyPublisher, PostgresInvitationRecipientAuthorityDependencyPublisher>();
        }

        services.AddSingleton<InvitationRecipientSynchronizationService>();
        services.AddSingleton<TransactionalInvitationRecipientSynchronization>();
        services.AddSingleton<IInvitationService, InvitationService>();
        services.AddSingleton<BoardInvitationService>();
        services.AddSingleton<IInvitationHistoryStore>(provider => (IInvitationHistoryStore)provider.GetRequiredService<IInvitationStore>());
        services.AddSingleton<InvitationHistoryService>();
        if (InvitationMailRegistration.IsEnabled(configuration, runtime))
            services.AddSingleton<IInvitationMailPublisher, PostgresInvitationMailPublisher>();
    }
}
