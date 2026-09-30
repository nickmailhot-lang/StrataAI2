using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Runtime;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

public static class WorkManagementRegistration
{
    public static void AddStrataAiWorkManagement(
        this IServiceCollection services,
        RuntimeDescriptor runtime)
    {
        if (runtime.Mode == RuntimeMode.Demo)
        {
            services.AddSingleton<IWorkManagementStore, InMemoryWorkManagementStore>();
            services.AddSingleton<IWorkEventStore, InMemoryWorkEventStore>();
            services.AddSingleton<IWorkManagementUnitOfWork, InMemoryWorkManagementUnitOfWork>();
        }
        else
        {
            services.AddSingleton<IWorkManagementStore, PostgresWorkManagementStore>();
            services.AddSingleton<IWorkEventStore, PostgresWorkEventStore>();
            services.AddSingleton<IWorkManagementUnitOfWork, PostgresWorkManagementUnitOfWork>();
        }

        services.AddSingleton<IWorkManagementService>(provider => new TransactionalWorkManagementService(new WorkManagementService(provider.GetRequiredService<IWorkManagementStore>(), provider.GetRequiredService<StrataAI.Application.Organizations.IOrganizationStore>(), provider.GetRequiredService<StrataAI.Application.Common.IClock>(), provider.GetRequiredService<IWorkEventStore>()),
                provider.GetRequiredService<IWorkManagementStore>(), provider.GetRequiredService<IWorkManagementUnitOfWork>(),
                provider.GetRequiredService<StrataAI.Application.Organizations.IOrganizationStore>(), provider.GetRequiredService<IWorkCommandContext>()));
    }
}
