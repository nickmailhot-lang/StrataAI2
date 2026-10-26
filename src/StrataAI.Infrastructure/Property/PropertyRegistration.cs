using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Property;
using StrataAI.Application.Runtime;
using StrataAI.Infrastructure.Organizations;

namespace StrataAI.Infrastructure.Property;

public static class PropertyRegistration
{
    public static void AddStrataAiProperty(this IServiceCollection services, RuntimeDescriptor runtime) {
        services.AddSingleton<StrataLotService>();
        if (runtime.Mode == RuntimeMode.Demo) {
            services.AddSingleton<InMemoryStrataLotStore>();
            services.AddSingleton<IStrataLotStore>(p => p.GetRequiredService<InMemoryStrataLotStore>());
            services.AddSingleton<IDemoOrganizationTransactionParticipant>(p => p.GetRequiredService<InMemoryStrataLotStore>());
        }
        else services.AddSingleton<IStrataLotStore, PostgresStrataLotStore>();
    }
}
