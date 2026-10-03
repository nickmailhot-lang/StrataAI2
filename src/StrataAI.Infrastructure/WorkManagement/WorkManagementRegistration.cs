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
            services.AddSingleton<InMemoryWorkEventStore>();
            services.AddSingleton<IWorkEventStore>(provider => provider.GetRequiredService<InMemoryWorkEventStore>());
            services.AddSingleton<IWorkEventReader>(provider => provider.GetRequiredService<InMemoryWorkEventStore>());
            services.AddSingleton<IWorkManagementUnitOfWork, InMemoryWorkManagementUnitOfWork>();
            services.AddSingleton<InMemoryWorkNotificationStore>();
            services.AddSingleton<IWorkNotificationStore>(provider => provider.GetRequiredService<InMemoryWorkNotificationStore>());
            services.AddSingleton<INotificationInboxStore, InMemoryNotificationInboxStore>();
            services.AddSingleton<IWatchSubscriptionStore, InMemoryWatchSubscriptionStore>();
            services.AddSingleton<ICardReminderStore, InMemoryCardReminderStore>();
            services.AddSingleton<ICardReminderJobPublisher, InMemoryCardReminderJobPublisher>();
            services.AddSingleton<IAttachmentScanJobPublisher, InMemoryAttachmentScanJobPublisher>();
        }
        else
        {
            services.AddSingleton<IWorkManagementStore, PostgresWorkManagementStore>();
            services.AddSingleton<IWorkEventStore, PostgresWorkEventStore>();
            services.AddSingleton<IWorkEventReader, PostgresWorkEventReader>();
            services.AddSingleton<IWorkManagementUnitOfWork, PostgresWorkManagementUnitOfWork>();
            services.AddSingleton<PostgresWorkNotificationStore>();
            services.AddSingleton<IWorkNotificationStore>(provider => provider.GetRequiredService<PostgresWorkNotificationStore>());
            services.AddSingleton<INotificationInboxStore>(provider => provider.GetRequiredService<PostgresWorkNotificationStore>());
            services.AddSingleton<IWatchSubscriptionStore, PostgresWatchSubscriptionStore>();
            services.AddSingleton<ICardReminderStore, PostgresCardReminderStore>();
            services.AddSingleton<ICardReminderJobPublisher, PostgresCardReminderJobPublisher>();
            services.AddSingleton<IAttachmentScanJobPublisher, PostgresAttachmentScanJobPublisher>();
        }

        services.AddSingleton<IWorkManagementService>(provider => new TransactionalWorkManagementService(new WorkManagementService(provider.GetRequiredService<IWorkManagementStore>(), provider.GetRequiredService<StrataAI.Application.Organizations.IOrganizationStore>(), provider.GetRequiredService<StrataAI.Application.Common.IClock>(), provider.GetRequiredService<IWorkEventStore>(), provider.GetRequiredService<StrataAI.Application.Identity.IdentityPolicy>(), provider.GetRequiredService<IWorkNotificationStore>(), provider.GetRequiredService<CardWatchNotificationProducer>(), provider.GetRequiredService<CardReminderScheduling>(), provider.GetRequiredService<CardReminderContainerScheduling>()),
                provider.GetRequiredService<IWorkManagementStore>(), provider.GetRequiredService<IWorkManagementUnitOfWork>(),
                provider.GetRequiredService<StrataAI.Application.Organizations.IOrganizationStore>(), provider.GetRequiredService<IWorkCommandContext>(),
                provider.GetRequiredService<StrataAI.Application.Identity.ICommandActorAuthorization>()));
        services.AddSingleton<IWorkBoardAuthorization>(provider => (IWorkBoardAuthorization)provider.GetRequiredService<IWorkManagementService>());
        services.AddSingleton<WorkSynchronizationService>();
        services.AddSingleton<NotificationInboxService>();
        services.AddSingleton<WatchSubscriptionService>();
        services.AddSingleton<ICardDateStore>(provider => (ICardDateStore)provider.GetRequiredService<IWorkManagementStore>());
        services.AddSingleton<CardDateService>();
        services.AddSingleton<IChecklistStore>(provider => (IChecklistStore)provider.GetRequiredService<IWorkManagementStore>());
        services.AddSingleton<ChecklistService>();
        services.AddSingleton<AttachmentService>();
        services.AddSingleton<AttachmentLifecycleService>();
        services.AddSingleton<AttachmentDownloadAdmissionService>();
        services.AddSingleton<AttachmentDownloadService>();
        services.AddSingleton<AttachmentPreviewReadService>();
        services.AddSingleton<AttachmentFilePublicationService>();
        services.AddSingleton<AttachmentUploadAdmissionService>();
        services.AddSingleton<AttachmentFileUploadService>();
        services.AddSingleton<IAttachmentMetadataStore>(provider => (IAttachmentMetadataStore)provider.GetRequiredService<IWorkManagementStore>());
        services.AddSingleton<IAttachmentUploadIntentStore>(provider => (IAttachmentUploadIntentStore)provider.GetRequiredService<IWorkManagementStore>());
        services.AddSingleton<IBoardDatePolicyStore>(provider => (IBoardDatePolicyStore)provider.GetRequiredService<IWorkManagementStore>());
        services.AddSingleton<BoardDatePolicyService>();
        services.AddSingleton<CardReminderScheduling>();
        services.AddSingleton<CardReminderContainerScheduling>();
        services.AddSingleton<CardReminderService>();
        services.AddSingleton<ICardReminderEventPublisher, CardReminderEvents>();
        services.AddSingleton<CardWatchNotificationProducer>();
    }
}
