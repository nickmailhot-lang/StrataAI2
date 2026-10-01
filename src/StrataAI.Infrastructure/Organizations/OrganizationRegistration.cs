using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Organizations;
using StrataAI.Application.Runtime;

namespace StrataAI.Infrastructure.Organizations;

public static class OrganizationRegistration
{
    public static void AddStrataAiOrganizations(
        this IServiceCollection services,
        RuntimeDescriptor runtime)
    {
        if (runtime.Mode == RuntimeMode.Demo)
        {
            services.AddSingleton<IOrganizationStore, InMemoryOrganizationStore>();
            services.AddSingleton<IOrganizationUnitOfWork, InMemoryOrganizationUnitOfWork>();
        }
        else
        {
            services.AddSingleton<IOrganizationStore, PostgresOrganizationStore>();
            services.AddSingleton<IOrganizationUnitOfWork, PostgresOrganizationUnitOfWork>();
        }

        services.AddSingleton<IOrganizationService, OrganizationService>();
    }
}
