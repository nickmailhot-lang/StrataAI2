using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Runtime;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Infrastructure.BackgroundJobs;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Runtime;

public static class RuntimeConfiguration
{
    public static RuntimeDescriptor AddStrataAiRuntime(
        this IServiceCollection services,
        IConfiguration configuration,
        System.Reflection.Assembly hostAssembly)
    {
        var mode = ParseMode(configuration["STRATAAI_RUNTIME_MODE"]);
        var identity = BuildIdentityReader.Read(hostAssembly);
        var descriptor = new RuntimeDescriptor(mode, identity.Revision, identity.Version);

        services.AddSingleton(descriptor);

        if (mode == RuntimeMode.Demo)
        {
            services.AddSingleton<IDemoDataStore, DemoDataStore>();
            services.AddSingleton<IRuntimeDependencyStatus, DemoRuntimeDependencyStatus>();
            return descriptor;
        }

        var connectionString = configuration.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Production mode requires ConnectionStrings:Postgres.");
        }

        services.AddSingleton(new PostgresConnectionFactory(connectionString));
        services.AddSingleton<PostgresBackgroundJobStore>();
        services.AddSingleton<IBackgroundJobStore>(provider => provider.GetRequiredService<PostgresBackgroundJobStore>());
        services.AddSingleton<IRuntimeDependencyStatus, ProductionRuntimeDependencyStatus>();

        return descriptor;
    }

    private static RuntimeMode ParseMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                "STRATAAI_RUNTIME_MODE is required and must be 'demo' or 'production'.");
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "demo" => RuntimeMode.Demo,
            "production" => RuntimeMode.Production,
            _ => throw new InvalidOperationException(
                "STRATAAI_RUNTIME_MODE must be either 'demo' or 'production'."),
        };
    }
}
