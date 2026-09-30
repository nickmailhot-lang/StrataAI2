using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Runtime;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Runtime;

public static class RuntimeConfiguration
{
    public static RuntimeDescriptor AddStrataAiRuntime(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var mode = ParseMode(configuration["STRATAAI_RUNTIME_MODE"]);
        var revision = configuration["STRATAAI_BUILD_REVISION"] ?? "development";
        var version = configuration["STRATAAI_BUILD_VERSION"] ?? "0.0.0-dev";
        var descriptor = new RuntimeDescriptor(mode, revision, version);

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
