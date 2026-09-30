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
        }
        else
        {
            services.AddSingleton<IWorkManagementStore, PostgresWorkManagementStore>();
        }

        services.AddSingleton<IWorkManagementService, WorkManagementService>();
    }
}
