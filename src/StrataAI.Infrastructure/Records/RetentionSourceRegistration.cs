using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Organizations;
using StrataAI.Application.Records;
using StrataAI.Application.Runtime;
using StrataAI.Infrastructure.Organizations;
using StrataAI.Infrastructure.WorkManagement;

namespace StrataAI.Infrastructure.Records;

public static class RetentionSourceRegistration
{
    public static void AddStrataAiRetentionSources(this IServiceCollection services, RuntimeDescriptor runtime)
    {
        if (runtime.Mode == RuntimeMode.Demo)
        {
            services.AddSingleton<InMemoryRetentionHoldStore>();
            services.AddSingleton<IRetentionHoldStore>(p => p.GetRequiredService<InMemoryRetentionHoldStore>());
            services.AddSingleton<IDemoOrganizationTransactionParticipant>(p => p.GetRequiredService<InMemoryRetentionHoldStore>());
            services.AddSingleton<Func<InMemoryRetentionHoldStore>>(p => () => p.GetRequiredService<InMemoryRetentionHoldStore>());
            services.AddSingleton<Func<InMemoryRetentionSourceStore>>(p => () => p.GetRequiredService<InMemoryRetentionSourceStore>());
            services.AddSingleton<IRetentionWorkDisposition, InMemoryRetentionWorkDisposition>();
            services.AddSingleton<InMemoryRetentionSourceStore>();
            services.AddSingleton<IRetentionSourceCommandStore>(p => p.GetRequiredService<InMemoryRetentionSourceStore>());
            services.AddSingleton<IRetentionSourceReferenceReader>(p => p.GetRequiredService<InMemoryRetentionSourceStore>());
            services.AddSingleton<IRetentionProducerReferences>(p => p.GetRequiredService<InMemoryRetentionSourceStore>());
            services.AddSingleton<IRetentionWorkSourceReferences>(p => p.GetRequiredService<InMemoryRetentionSourceStore>());
            services.AddSingleton<IDemoOrganizationTransactionParticipant>(p => p.GetRequiredService<InMemoryRetentionSourceStore>());
        }
        else
        {
            services.AddSingleton<IRetentionWorkDisposition, PostgresRetentionWorkDisposition>();
            services.AddSingleton<PostgresRetentionSourceStore>();
            services.AddSingleton<IRetentionSourceCommandStore>(p => p.GetRequiredService<PostgresRetentionSourceStore>());
            services.AddSingleton<IRetentionSourceReferenceReader>(p => p.GetRequiredService<PostgresRetentionSourceStore>());
            services.AddSingleton<IRetentionProducerReferences, PostgresRetentionProducerReferences>();
            services.AddSingleton<IRetentionWorkSourceReferences, PostgresRetentionAuthorizationReferences>();
        }
        services.AddSingleton<RetentionProducerAuthorization>();
        services.AddSingleton<RetentionWorkAuthorization>();
        services.AddSingleton<IRetentionHoldScopeAuthorization>(p => p.GetRequiredService<RetentionWorkAuthorization>());
        services.AddSingleton<RetentionSourceReviewService>();
        services.AddSingleton<RetentionSourceClassificationService>();
    }
}
