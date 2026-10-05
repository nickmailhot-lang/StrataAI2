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
        services.AddDataProtection();
        services.AddSingleton<IActivityCursorCodec, DataProtectedActivityCursorCodec>();
        services.AddSingleton<IGlobalSearchCursorCodec, DataProtectedGlobalSearchCursorCodec>();
        services.AddSingleton<IOrganizationBoardCursorCodec, DataProtectedOrganizationBoardCursorCodec>();
        services.AddSingleton<GlobalSearchService>();
        services.AddSingleton<SearchInteractionEventProducer>();
        if (runtime.Mode == RuntimeMode.Demo)
        {
            services.AddSingleton<DemoWorkTransactionScope>();
            services.AddSingleton<InMemorySearchInteractionEventStore>();
            services.AddSingleton<ISearchInteractionEventStore>(provider => provider.GetRequiredService<InMemorySearchInteractionEventStore>());
            services.AddSingleton<IBoardFilterInteractionReplayStore>(provider => provider.GetRequiredService<InMemorySearchInteractionEventStore>());
            services.AddSingleton<StrataAI.Infrastructure.Identity.IDemoIdentityTransactionParticipant>(provider => provider.GetRequiredService<InMemorySearchInteractionEventStore>());
            services.AddSingleton<ICardMentionMemberStore, InMemoryCardMentionMemberStore>();
            services.AddSingleton<ICardMassMentionMemberStore, InMemoryCardMassMentionMemberStore>();
            services.AddSingleton<InMemoryCardMassMentionQuota>();
            services.AddSingleton<ICardMassMentionQuota>(provider => provider.GetRequiredService<InMemoryCardMassMentionQuota>());
            services.AddSingleton<IDemoWorkTransactionParticipant>(provider => provider.GetRequiredService<InMemoryCardMassMentionQuota>());
            services.AddSingleton<InMemoryWorkManagementStore>();
            services.AddSingleton<IWorkManagementStore>(provider => provider.GetRequiredService<InMemoryWorkManagementStore>());
            services.AddSingleton<ICardCommentStore>(provider => provider.GetRequiredService<InMemoryWorkManagementStore>());
            services.AddSingleton<ICommentMentionSnapshotStore>(provider => provider.GetRequiredService<InMemoryWorkManagementStore>());
            services.AddSingleton<IDemoWorkTransactionParticipant>(provider => provider.GetRequiredService<InMemoryWorkManagementStore>());
            services.AddSingleton<InMemoryWorkEventStore>();
            services.AddSingleton<IWorkEventStore>(provider => provider.GetRequiredService<InMemoryWorkEventStore>());
            services.AddSingleton<IWorkEventReader>(provider => provider.GetRequiredService<InMemoryWorkEventStore>());
            services.AddSingleton<IOrganizationBoardEventReader>(provider => provider.GetRequiredService<InMemoryWorkEventStore>());
            services.AddKeyedSingleton<IOrganizationBoardEventReader, InMemoryOrganizationBoardDiscoveryReader>(OrganizationBoardAudience.BoardDiscovery);
            services.AddSingleton<IActivityEventSourceStore>(provider => provider.GetRequiredService<InMemoryWorkEventStore>());
            services.AddSingleton<IActivityPrivateTargetStore>(provider => provider.GetRequiredService<InMemoryWorkEventStore>());
            services.AddSingleton<IActivityFeedStore, InMemoryActivityFeedStore>();
            services.AddSingleton<IDemoWorkTransactionParticipant>(provider => provider.GetRequiredService<InMemoryWorkEventStore>());
            services.AddSingleton<IWorkManagementUnitOfWork, InMemoryWorkManagementUnitOfWork>();
            services.AddSingleton<InMemoryWorkNotificationStore>();
            services.AddSingleton<IWorkNotificationStore>(provider => provider.GetRequiredService<InMemoryWorkNotificationStore>());
            services.AddSingleton<INotificationRealtimeStore>(provider => provider.GetRequiredService<InMemoryWorkNotificationStore>());
            services.AddSingleton<IDemoWorkTransactionParticipant>(provider => provider.GetRequiredService<InMemoryWorkNotificationStore>());
            services.AddSingleton<INotificationInboxStore, InMemoryNotificationInboxStore>();
            services.AddSingleton<InMemoryWatchSubscriptionStore>();
            services.AddSingleton<IWatchSubscriptionStore>(provider => provider.GetRequiredService<InMemoryWatchSubscriptionStore>());
            services.AddSingleton<InMemoryCardReminderStore>();
            services.AddSingleton<ICardReminderStore>(provider => provider.GetRequiredService<InMemoryCardReminderStore>());
            services.AddSingleton<ICardReminderJobPublisher, InMemoryCardReminderJobPublisher>();
            services.AddSingleton<IAttachmentScanJobPublisher, InMemoryAttachmentScanJobPublisher>();
        }
        else
        {
            services.AddSingleton<IWorkManagementStore, PostgresWorkManagementStore>();
            services.AddSingleton<ICardMentionMemberStore, PostgresCardMentionMemberStore>();
            services.AddSingleton<ICardMassMentionMemberStore, PostgresCardMassMentionMemberStore>();
            services.AddSingleton<ICardMassMentionQuota, PostgresCardMassMentionQuota>();
            services.AddSingleton<ICardCommentStore, PostgresCardCommentStore>();
            services.AddSingleton<ICommentMentionSnapshotStore, PostgresCommentMentionSnapshotStore>();
            services.AddSingleton<IWorkEventStore, PostgresWorkEventStore>();
            services.AddSingleton<PostgresSearchInteractionEventStore>();
            services.AddSingleton<ISearchInteractionEventStore>(provider => provider.GetRequiredService<PostgresSearchInteractionEventStore>());
            services.AddSingleton<IBoardFilterInteractionReplayStore>(provider => provider.GetRequiredService<PostgresSearchInteractionEventStore>());
            services.AddSingleton<IWorkEventReader, PostgresWorkEventReader>();
            services.AddSingleton<IOrganizationBoardEventReader, PostgresOrganizationBoardEventReader>();
            services.AddKeyedSingleton<IOrganizationBoardEventReader>(OrganizationBoardAudience.BoardDiscovery,
                (provider, _) => new PostgresOrganizationBoardEventReader(provider.GetRequiredService<StrataAI.Infrastructure.Persistence.PostgresConnectionFactory>(),
                    OrganizationBoardAudience.BoardDiscovery));
            services.AddSingleton<PostgresActivityEventSourceStore>();
            services.AddSingleton<IActivityEventSourceStore>(provider => provider.GetRequiredService<PostgresActivityEventSourceStore>());
            services.AddSingleton<IActivityPrivateTargetStore>(provider => provider.GetRequiredService<PostgresActivityEventSourceStore>());
            services.AddSingleton<IActivityFeedStore, PostgresActivityFeedStore>();
            services.AddSingleton<IWorkManagementUnitOfWork, PostgresWorkManagementUnitOfWork>();
            services.AddSingleton<PostgresWorkNotificationStore>();
            services.AddSingleton<IWorkNotificationStore>(provider => provider.GetRequiredService<PostgresWorkNotificationStore>());
            services.AddSingleton<INotificationInboxStore>(provider => provider.GetRequiredService<PostgresWorkNotificationStore>());
            services.AddSingleton<INotificationRealtimeStore>(provider => provider.GetRequiredService<PostgresWorkNotificationStore>());
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
        services.AddSingleton<CardMassMentionPlanning>();
        services.AddSingleton<WorkSynchronizationService>();
        services.AddSingleton<OrganizationBoardSynchronizationService>();
        services.AddSingleton<TransactionalOrganizationBoardSynchronization>();
        services.AddKeyedSingleton<TransactionalOrganizationBoardSynchronization>(OrganizationBoardAudience.BoardDiscovery,
            (provider, _) => new(new OrganizationBoardSynchronizationService(
                provider.GetRequiredKeyedService<IOrganizationBoardEventReader>(OrganizationBoardAudience.BoardDiscovery),
                provider.GetRequiredService<IOrganizationBoardCursorCodec>()),
                provider.GetRequiredService<IWorkManagementUnitOfWork>(), provider.GetRequiredService<IWorkManagementStore>(),
                provider.GetRequiredService<StrataAI.Application.Organizations.IOrganizationStore>()));
        services.AddSingleton<ActivitySourceScopeResolver>();
        services.AddSingleton<ActivityFeedService>();
        services.AddSingleton<NotificationInboxService>();
        services.AddSingleton<WatchSubscriptionService>();
        services.AddSingleton<ICardDateStore>(provider => (ICardDateStore)provider.GetRequiredService<IWorkManagementStore>());
        services.AddSingleton<CardDateService>();
        services.AddSingleton<IChecklistStore>(provider => (IChecklistStore)provider.GetRequiredService<IWorkManagementStore>());
        services.AddSingleton<ChecklistService>();
        services.AddSingleton<CardCommentService>();
        services.AddSingleton<ArchivedCardDetailService>();
        services.AddSingleton<CardMentionOptionsService>();
        services.AddSingleton<CardCommentMentionPlanning>();
        services.AddSingleton<AttachmentService>();
        services.AddSingleton<AttachmentLifecycleService>();
        services.AddSingleton<ICardAttachmentCoverStore>(provider => (ICardAttachmentCoverStore)provider.GetRequiredService<IWorkManagementStore>());
        services.AddSingleton<CardAttachmentCoverService>();
        services.AddSingleton<CardCoverAdmissionService>();
        services.AddSingleton<CardCoverReadService>();
        services.AddSingleton<AttachmentDownloadAdmissionService>();
        services.AddSingleton<AttachmentDownloadService>();
        services.AddSingleton<AttachmentPreviewReadService>();
        services.AddSingleton<BoardBackgroundImageSelectionService>();
        services.AddSingleton<BoardBackgroundImageAdmissionService>();
        services.AddSingleton<BoardBackgroundImageReadService>();
        services.AddSingleton<AttachmentFilePublicationService>();
        services.AddSingleton<AttachmentUploadAdmissionService>();
        services.AddSingleton<AttachmentFileUploadService>();
        services.AddSingleton<IAttachmentMetadataStore>(provider => (IAttachmentMetadataStore)provider.GetRequiredService<IWorkManagementStore>());
        services.AddSingleton<IAttachmentUploadIntentStore>(provider => (IAttachmentUploadIntentStore)provider.GetRequiredService<IWorkManagementStore>());
        services.AddSingleton<IBoardDatePolicyStore>(provider => (IBoardDatePolicyStore)provider.GetRequiredService<IWorkManagementStore>());
        services.AddSingleton<BoardDatePolicyService>();
        services.AddSingleton<ICardReminderMoveEligibility, CardReminderMoveEligibility>();
        services.AddSingleton<CardReminderScheduling>();
        services.AddSingleton<CardReminderContainerScheduling>();
        services.AddSingleton<CardReminderService>();
        services.AddSingleton<ICardReminderEventPublisher, CardReminderEvents>();
        services.AddSingleton<CardWatchNotificationProducer>();
    }
}
