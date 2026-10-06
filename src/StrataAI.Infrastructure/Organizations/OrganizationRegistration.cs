using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StrataAI.Application.Organizations;
using StrataAI.Application.Runtime;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Organizations;

public static class OrganizationRegistration
{
    public static void AddStrataAiOrganizations(
        this IServiceCollection services,
        RuntimeDescriptor runtime)
    {
        services.AddSingleton<IOrganizationMetadataCursorCodec, DataProtectedOrganizationMetadataCursorCodec>();
        if (runtime.Mode == RuntimeMode.Demo)
        {
            services.TryAddSingleton<InMemoryAccountOrganizationGate>();
            services.AddSingleton<InMemoryOrganizationStore>();
            services.AddSingleton<IOrganizationStore>(provider => provider.GetRequiredService<InMemoryOrganizationStore>());
            services.AddSingleton<IDemoOrganizationTransactionParticipant>(provider => provider.GetRequiredService<InMemoryOrganizationStore>());
            services.AddSingleton<IOrganizationUnitOfWork, InMemoryOrganizationUnitOfWork>();
            services.AddSingleton<InMemoryOrganizationDeletionJobPublisher>();
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
            services.AddSingleton<OrganizationMetadataSynchronizationService>();
            services.AddSingleton<TransactionalOrganizationMetadataSynchronization>();
            services.AddSingleton<IOrganizationStore, PostgresOrganizationStore>();
            services.AddSingleton<IOrganizationUnitOfWork, PostgresOrganizationUnitOfWork>();
            services.AddSingleton<IOrganizationRemovalReplayStore, PostgresOrganizationRemovalReplayStore>();
            services.AddSingleton<IOrganizationDepartureReplayStore, PostgresOrganizationDepartureReplayStore>();
            services.AddSingleton<IOrganizationMetadataReplayStore, PostgresOrganizationMetadataReplayStore>();
            services.AddSingleton<IOrganizationCreationReplayStore, PostgresOrganizationCreationReplayStore>();
            services.AddSingleton<IOrganizationDeletionReplayStore, PostgresOrganizationDeletionReplayStore>();
            services.AddSingleton<IOrganizationDeletionJobPublisher, PostgresOrganizationDeletionJobPublisher>();
            services.AddSingleton<IOrganizationDeletionObservationReader, PostgresOrganizationDeletionObservationReader>();
        }

        services.AddSingleton<IOrganizationService, OrganizationService>();
    }
}
