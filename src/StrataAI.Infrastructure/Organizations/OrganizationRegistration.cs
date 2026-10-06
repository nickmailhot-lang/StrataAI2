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
        if (runtime.Mode == RuntimeMode.Demo)
        {
            services.TryAddSingleton<InMemoryAccountOrganizationGate>();
            services.AddSingleton<InMemoryOrganizationStore>();
            services.AddSingleton<IOrganizationStore>(provider => provider.GetRequiredService<InMemoryOrganizationStore>());
            services.AddSingleton<IDemoOrganizationTransactionParticipant>(provider => provider.GetRequiredService<InMemoryOrganizationStore>());
            services.AddSingleton<IOrganizationUnitOfWork, InMemoryOrganizationUnitOfWork>();
            services.AddSingleton<InMemoryOrganizationMetadataReplayStore>();
            services.AddSingleton<IOrganizationMetadataReplayStore>(provider => provider.GetRequiredService<InMemoryOrganizationMetadataReplayStore>());
            services.AddSingleton<IDemoOrganizationTransactionParticipant>(provider => provider.GetRequiredService<InMemoryOrganizationMetadataReplayStore>());
        }
        else
        {
            services.AddSingleton<IOrganizationStore, PostgresOrganizationStore>();
            services.AddSingleton<IOrganizationUnitOfWork, PostgresOrganizationUnitOfWork>();
            services.AddSingleton<IOrganizationMetadataReplayStore, PostgresOrganizationMetadataReplayStore>();
        }

        services.AddSingleton<IOrganizationService, OrganizationService>();
    }
}
