using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StrataAI.Application.Organizations;
using StrataAI.Application.Runtime;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Infrastructure.WorkManagement;
using StrataAI.Infrastructure.Records;

namespace StrataAI.Infrastructure.Organizations;

public static class OrganizationRegistration
{
    public static void AddStrataAiOrganizations(
        this IServiceCollection services,
        RuntimeDescriptor runtime)
    {
        services.AddSingleton<IOrganizationMetadataCursorCodec, DataProtectedOrganizationMetadataCursorCodec>();
        services.AddSingleton<IOrganizationConfigurationCursorCodec, DataProtectedOrganizationConfigurationCursorCodec>();
        services.AddSingleton<IOrganizationConfigurationActivityCursorCodec, DataProtectedOrganizationConfigurationActivityCursorCodec>();
        services.AddSingleton<OrganizationOperationalLifecycleCommandScope>();
        if (runtime.Mode == RuntimeMode.Demo)
        {
            services.TryAddSingleton<InMemoryAccountOrganizationGate>();
            services.AddSingleton<InMemoryOrganizationStore>();
            services.AddSingleton<IOrganizationStore>(provider => provider.GetRequiredService<InMemoryOrganizationStore>());
            services.AddSingleton<IDemoOrganizationTransactionParticipant>(provider => provider.GetRequiredService<InMemoryOrganizationStore>());
            services.AddSingleton<InMemoryOrganizationConfigurationStore>();
            services.AddSingleton<IOrganizationConfigurationStore>(provider => provider.GetRequiredService<InMemoryOrganizationConfigurationStore>());
            services.AddSingleton<IOrganizationConfigurationEventReader>(provider => provider.GetRequiredService<InMemoryOrganizationConfigurationStore>());
            services.AddSingleton<IOrganizationConfigurationAttributionReader>(provider => provider.GetRequiredService<InMemoryOrganizationConfigurationStore>());
            services.AddSingleton<IOrganizationConfigurationIntakeStore>(provider => provider.GetRequiredService<InMemoryWorkManagementStore>());
            services.AddSingleton<IDemoOrganizationTransactionParticipant>(provider => provider.GetRequiredService<InMemoryOrganizationConfigurationStore>());
            services.AddSingleton<StrataAI.Infrastructure.Records.InMemoryRetentionPolicyStore>();
            services.AddSingleton<StrataAI.Application.Records.IRetentionPolicyStore>(provider => provider.GetRequiredService<StrataAI.Infrastructure.Records.InMemoryRetentionPolicyStore>());
            services.AddSingleton<IDemoOrganizationTransactionParticipant>(provider => provider.GetRequiredService<StrataAI.Infrastructure.Records.InMemoryRetentionPolicyStore>());
            services.AddSingleton<InMemoryOrganizationMetadataJournal>();
            services.AddSingleton<Func<InMemoryOrganizationMetadataJournal>>(provider => () => provider.GetRequiredService<InMemoryOrganizationMetadataJournal>());
            services.AddSingleton<IOrganizationMetadataEventReader>(provider => provider.GetRequiredService<InMemoryOrganizationMetadataJournal>());
            services.AddSingleton<IDemoOrganizationTransactionParticipant>(provider => provider.GetRequiredService<InMemoryOrganizationMetadataJournal>());
            services.AddSingleton<IOrganizationUnitOfWork, InMemoryOrganizationUnitOfWork>();
            services.AddSingleton<InMemoryOrganizationOperationalLifecycleStore>();
            services.AddSingleton<IOrganizationOperationalLifecycleStore>(provider => provider.GetRequiredService<InMemoryOrganizationOperationalLifecycleStore>());
            services.AddSingleton<IOrganizationOperationalLifecycleEventReader>(provider => provider.GetRequiredService<InMemoryOrganizationOperationalLifecycleStore>());
            services.AddSingleton<IDemoOrganizationTransactionParticipant>(provider => provider.GetRequiredService<InMemoryOrganizationOperationalLifecycleStore>());
            services.AddSingleton<IOrganizationOperationalLifecycleUnitOfWork, InMemoryOrganizationOperationalLifecycleUnitOfWork>();
            services.AddSingleton<InMemoryOrganizationDeletionJobPublisher>();
            services.AddSingleton<InMemoryOrganizationDeletionGraphSimulation>();
            services.AddSingleton<IOrganizationDeletionGraphSimulation>(provider => provider.GetRequiredService<InMemoryOrganizationDeletionGraphSimulation>());
            services.AddSingleton<IDemoOrganizationTransactionParticipant>(provider => provider.GetRequiredService<InMemoryOrganizationDeletionGraphSimulation>());
            services.AddSingleton<IOrganizationLifecycleEventReader, InMemoryOrganizationLifecycleEventReader>();
            services.AddSingleton<IDemoOrganizationDeletionSimulation, InMemoryOrganizationDeletionSimulation>();
            services.AddSingleton<IDemoOrganizationDeletionCompletionPublisher, InMemoryOrganizationDeletionCompletionPublisher>();
            services.AddSingleton(new DemoOrganizationDeletionSimulationOptions());
            services.AddHostedService<DemoOrganizationDeletionSimulationHost>();
            services.AddSingleton<IOrganizationDeletionObservationReader, InMemoryOrganizationDeletionObservationReader>();
            services.AddSingleton<IOrganizationDeletionJobPublisher>(provider => provider.GetRequiredService<InMemoryOrganizationDeletionJobPublisher>());
            services.AddSingleton<IDemoOrganizationTransactionParticipant>(provider => provider.GetRequiredService<InMemoryOrganizationDeletionJobPublisher>());
            services.AddSingleton<InMemoryOrganizationRemovalReplayStore>();
            services.AddSingleton<IOrganizationRemovalReplayStore>(provider => provider.GetRequiredService<InMemoryOrganizationRemovalReplayStore>());
            services.AddSingleton<IDemoOrganizationTransactionParticipant>(provider => provider.GetRequiredService<InMemoryOrganizationRemovalReplayStore>());
            services.AddSingleton<InMemoryOrganizationDepartureReplayStore>();
            services.AddSingleton<IOrganizationDepartureReplayStore>(provider => provider.GetRequiredService<InMemoryOrganizationDepartureReplayStore>());
            services.AddSingleton<IDemoOrganizationTransactionParticipant>(provider => provider.GetRequiredService<InMemoryOrganizationDepartureReplayStore>());
            services.AddSingleton<InMemoryOrganizationDeletionReplayStore>();
            services.AddSingleton<IOrganizationDeletionReplayStore>(provider => provider.GetRequiredService<InMemoryOrganizationDeletionReplayStore>());
            services.AddSingleton<IDemoOrganizationTransactionParticipant>(provider => provider.GetRequiredService<InMemoryOrganizationDeletionReplayStore>());
            services.AddSingleton<InMemoryOrganizationCreationReplayStore>();
            services.AddSingleton<IOrganizationCreationReplayStore>(provider => provider.GetRequiredService<InMemoryOrganizationCreationReplayStore>());
            services.AddSingleton<IDemoOrganizationTransactionParticipant>(provider => provider.GetRequiredService<InMemoryOrganizationCreationReplayStore>());
            services.AddSingleton<InMemoryOrganizationMetadataReplayStore>();
            services.AddSingleton<IOrganizationMetadataReplayStore>(provider => provider.GetRequiredService<InMemoryOrganizationMetadataReplayStore>());
            services.AddSingleton<IDemoOrganizationTransactionParticipant>(provider => provider.GetRequiredService<InMemoryOrganizationMetadataReplayStore>());
        }
        else
        {
            services.AddSingleton<IOrganizationMetadataEventReader, PostgresOrganizationMetadataEventReader>();
            services.AddSingleton<IOrganizationLifecycleEventReader, PostgresOrganizationLifecycleEventReader>();
            services.AddSingleton<IOrganizationStore, PostgresOrganizationStore>();
            services.AddSingleton<IOrganizationConfigurationStore, PostgresOrganizationConfigurationStore>();
            services.AddSingleton<StrataAI.Application.Records.IRetentionPolicyStore, StrataAI.Infrastructure.Records.PostgresRetentionPolicyStore>();
            services.AddSingleton<PostgresOrganizationConfigurationEventReader>();
            services.AddSingleton<IOrganizationConfigurationEventReader>(provider => provider.GetRequiredService<PostgresOrganizationConfigurationEventReader>());
            services.AddSingleton<IOrganizationConfigurationAttributionReader>(provider => provider.GetRequiredService<PostgresOrganizationConfigurationEventReader>());
            services.AddSingleton<IOrganizationConfigurationIntakeStore, PostgresOrganizationConfigurationIntakeStore>();
            services.AddSingleton<IOrganizationUnitOfWork, PostgresOrganizationUnitOfWork>();
            services.AddSingleton<IOrganizationOperationalLifecycleUnitOfWork, PostgresOrganizationOperationalLifecycleUnitOfWork>();
            services.AddSingleton<IOrganizationOperationalLifecycleStore, PostgresOrganizationOperationalLifecycleStore>();
            services.AddSingleton<IOrganizationOperationalLifecycleEventReader, PostgresOrganizationOperationalLifecycleEventReader>();
            services.AddSingleton<IOrganizationRemovalReplayStore, PostgresOrganizationRemovalReplayStore>();
            services.AddSingleton<IOrganizationDepartureReplayStore, PostgresOrganizationDepartureReplayStore>();
            services.AddSingleton<IOrganizationMetadataReplayStore, PostgresOrganizationMetadataReplayStore>();
            services.AddSingleton<IOrganizationCreationReplayStore, PostgresOrganizationCreationReplayStore>();
            services.AddSingleton<IOrganizationDeletionReplayStore, PostgresOrganizationDeletionReplayStore>();
            services.AddSingleton<IOrganizationDeletionJobPublisher, PostgresOrganizationDeletionJobPublisher>();
            services.AddSingleton<IOrganizationDeletionObservationReader, PostgresOrganizationDeletionObservationReader>();
        }

        services.AddSingleton<OrganizationMetadataSynchronizationService>();
        services.AddSingleton<OrganizationOperationalLifecycleService>();
        services.AddSingleton<OrganizationOperationalLifecycleDirectoryService>();
        services.AddSingleton<OrganizationOperationalLifecycleReplayService>();
        services.AddSingleton<OrganizationConfigurationService>();
        services.AddSingleton<StrataAI.Application.Records.RetentionPolicyService>();
        services.AddStrataAiRetentionSources(runtime);
        services.AddSingleton<OrganizationConfigurationSynchronizationService>();
        services.AddSingleton<OrganizationConfigurationActivityService>();
        services.AddSingleton<TransactionalOrganizationMetadataSynchronization>();
        services.AddSingleton<IOrganizationService, OrganizationService>();
    }
}
