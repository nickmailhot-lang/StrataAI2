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
            services.AddSingleton<IWorkManagementUnitOfWork, InMemoryWorkManagementUnitOfWork>();
        }
        else
        {
            services.AddSingleton<IWorkManagementStore, PostgresWorkManagementStore>();
            services.AddSingleton<IWorkManagementUnitOfWork, PostgresWorkManagementUnitOfWork>();
        }

        services.AddSingleton<WorkManagementService>();
        services.AddSingleton<IWorkManagementService>(provider => new TransactionalWorkManagementService(provider.GetRequiredService<WorkManagementService>(),
                provider.GetRequiredService<IWorkManagementStore>(), provider.GetRequiredService<IWorkManagementUnitOfWork>(),
                provider.GetRequiredService<StrataAI.Application.Organizations.IOrganizationStore>(), provider.GetRequiredService<IWorkCommandContext>()));
    }
}
