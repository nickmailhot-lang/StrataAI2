using StrataAI.Application.Common;
using StrataAI.Application.Runtime;
using StrataAI.Infrastructure.Runtime;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Worker;
using StrataAI.Application.Identity;
using StrataAI.Infrastructure.Identity;
using StrataAI.Application.Onboarding;
using StrataAI.Infrastructure.Onboarding;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Infrastructure.WorkManagement;
using Microsoft.Extensions.DependencyInjection.Extensions;

if (args.Contains("--decode-private-attachment-preview", StringComparer.Ordinal))
{
    Environment.ExitCode = args.Length == 1
        ? await AttachmentPreviewProcessProtocol.RunChildAsync(Console.OpenStandardInput(), Console.OpenStandardOutput()) : 2;
    return;
}

// Release-image verification is an explicit command, before configuration,
// host startup, credentials, database connections or any durable job execution.
if (args.Contains("--verify-attachment-preview-runtime", StringComparer.Ordinal))
{
    Environment.ExitCode = args.Length == 1 ? await AttachmentPreviewRuntimeVerification.RunAsync() : 2;
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IClock, SystemClock>();
var runtime = builder.Services.AddStrataAiRuntime(builder.Configuration, typeof(Program).Assembly);
var attachmentsEnabled = builder.Services.AddAttachmentRuntime(builder.Configuration, runtime, worker: true, environmentName: builder.Environment.EnvironmentName);
if (attachmentsEnabled) builder.Services.AddSingleton(AttachmentPreviewRuntimeVerification.CurrentExecutable());
builder.Services.AddHostedService<WorkerHeartbeat>();

if (builder.Services.AddIdentityDeliveryTokens(builder.Configuration,runtime))
{
    var apiKey=builder.Configuration["STRATAAI_IDENTITY_EMAIL_API_KEY"];
    if (string.IsNullOrWhiteSpace(apiKey)) throw new InvalidOperationException("Identity email Worker requires a provider API key.");
    var identityDb=builder.Configuration.GetConnectionString("IdentityDeliveryPostgres");
    if (string.IsNullOrWhiteSpace(identityDb)) throw new InvalidOperationException("Identity email Worker requires its restricted IdentityDeliveryPostgres connection.");
    Uri? endpoint=null;
    var testEndpoint=builder.Configuration["STRATAAI_IDENTITY_EMAIL_TEST_ENDPOINT"];
    if (!string.IsNullOrWhiteSpace(testEndpoint))
    {
        if (!builder.Environment.IsEnvironment("IntegrationTest") || !Uri.TryCreate(testEndpoint,UriKind.Absolute,out endpoint) ||
            endpoint.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(endpoint.UserInfo) || !string.IsNullOrEmpty(endpoint.Query) || !string.IsNullOrEmpty(endpoint.Fragment))
            throw new InvalidOperationException("Identity provider test endpoint requires the isolated IntegrationTest environment.");
    }
    builder.Services.AddHttpClient("identity-email",client=>
    { client.Timeout=TimeSpan.FromSeconds(30); client.MaxResponseContentBufferSize=65536; })
        .ConfigurePrimaryHttpMessageHandler(()=>new HttpClientHandler { AllowAutoRedirect=false })
        .RedactLoggedHeaders(_=>true);
    builder.Services.AddSingleton<IIdentityEmailProvider>(provider=>new ResendIdentityEmailProvider(
        provider.GetRequiredService<IHttpClientFactory>().CreateClient("identity-email"),apiKey,endpoint));
    builder.Services.AddSingleton<IIdentityDeliveryStore>(_=>new PostgresIdentityDeliveryStore(identityDb));
    builder.Services.AddSingleton<IdentityDeliveryProcessor>();
    builder.Services.AddSingleton<IIdentityDeliveryDiagnostics,IdentityDeliveryDiagnostics>();
    builder.Services.AddHostedService<IdentityEmailWorker>();
}

if (runtime.Mode == RuntimeMode.Production)
{
    builder.Services.AddSingleton<StrataAI.Application.Organizations.IOrganizationDeletionPageStore, StrataAI.Infrastructure.Organizations.PostgresOrganizationDeletionPageStore>();
    builder.Services.AddSingleton<IBackgroundJobHandler, StrataAI.Application.Organizations.OrganizationDeletionPageHandler>();
    builder.Services.AddSingleton<StrataAI.Application.Organizations.IOrganizationLifecycleDeliveryStore, StrataAI.Infrastructure.Organizations.PostgresOrganizationLifecycleDeliveryStore>();
    builder.Services.AddSingleton<IBackgroundJobHandler, StrataAI.Application.Organizations.OrganizationLifecycleDeliveryHandler>();
    builder.Services.AddSingleton<StrataAI.Application.Organizations.IOrganizationMetadataDeliveryStore, StrataAI.Infrastructure.Organizations.PostgresOrganizationMetadataDeliveryStore>();
    builder.Services.AddSingleton<IBackgroundJobHandler, StrataAI.Application.Organizations.OrganizationMetadataDeliveryHandler>();
    builder.Services.AddSingleton<IInvitationRecipientAuthorityDeliveryStore, PostgresInvitationRecipientAuthorityDeliveryStore>();
    builder.Services.AddSingleton<IBackgroundJobHandler, InvitationRecipientAuthorityDeliveryHandler>();
    builder.Services.AddSingleton<IIdentityRetryCleanupStore, PostgresIdentityRetryCleanupStore>();
    builder.Services.AddHostedService<IdentityRetryCleanupWorker>();
    builder.Services.AddSingleton<StrataAI.Application.WorkManagement.IWorkEventDeliveryStore, StrataAI.Infrastructure.WorkManagement.PostgresWorkEventDeliveryStore>();
    builder.Services.AddSingleton<IBackgroundJobHandler, StrataAI.Application.WorkManagement.WorkEventDeliveryHandler>();
    var reminderRequireVerified = !bool.TryParse(builder.Configuration["STRATAAI_AUTH_REQUIRE_VERIFIED_EMAIL"], out var reminderVerified) || reminderVerified;
    builder.Services.AddSingleton<StrataAI.Application.WorkManagement.ICardReminderDeliveryStore>(provider =>
        new StrataAI.Infrastructure.WorkManagement.PostgresCardReminderDeliveryStore(provider.GetRequiredService<PostgresConnectionFactory>(), reminderRequireVerified));
    builder.Services.AddSingleton<IBackgroundJobHandler, StrataAI.Application.WorkManagement.CardReminderDeliveryHandler>();
}

var jobScope = builder.Configuration["STRATAAI_WORKER_ORGANIZATION_IDS"];
var deletionDiscoverySetting = builder.Configuration["STRATAAI_ORGANIZATION_DELETION_DISCOVERY_ENABLED"];
var deletionDiscovery = deletionDiscoverySetting is null ? runtime.Mode == RuntimeMode.Production
    : bool.TryParse(deletionDiscoverySetting, out var discoveryEnabled) ? discoveryEnabled
    : throw new InvalidOperationException("Organization deletion discovery setting must be true or false.");
if (deletionDiscovery && runtime.Mode != RuntimeMode.Production)
    throw new InvalidOperationException("Organization deletion discovery requires Production mode.");
var metadataDiscoverySetting = builder.Configuration["STRATAAI_ORGANIZATION_METADATA_DISCOVERY_ENABLED"];
var metadataDiscovery = metadataDiscoverySetting is null ? runtime.Mode == RuntimeMode.Production
    : bool.TryParse(metadataDiscoverySetting, out var metadataEnabled) ? metadataEnabled
    : throw new InvalidOperationException("Organization metadata discovery setting must be true or false.");
if (metadataDiscovery && runtime.Mode != RuntimeMode.Production)
    throw new InvalidOperationException("Organization metadata discovery requires Production mode.");
if (metadataDiscovery)
{
    builder.Services.AddSingleton<StrataAI.Application.Organizations.IOrganizationMetadataScopeReader,
        StrataAI.Infrastructure.Organizations.PostgresOrganizationMetadataScopeReader>();
    builder.Services.TryAddSingleton<IBackgroundJobDiagnostics, BackgroundJobDiagnostics>();
    builder.Services.AddHostedService<OrganizationMetadataDiscoveryWorker>();
}
var authorityDiscoverySetting = builder.Configuration["STRATAAI_INVITATION_RECIPIENT_AUTHORITY_DISCOVERY_ENABLED"];
var authorityDiscovery = authorityDiscoverySetting is null ? runtime.Mode == RuntimeMode.Production
    : bool.TryParse(authorityDiscoverySetting, out var authorityEnabled) ? authorityEnabled
    : throw new InvalidOperationException("Invitation recipient authority discovery setting must be true or false.");
if (authorityDiscovery && runtime.Mode != RuntimeMode.Production)
    throw new InvalidOperationException("Invitation recipient authority discovery requires Production mode.");
if (authorityDiscovery)
{
    builder.Services.AddSingleton<IInvitationRecipientAuthorityScopeReader, PostgresInvitationRecipientAuthorityScopeReader>();
    builder.Services.TryAddSingleton<IBackgroundJobDiagnostics, BackgroundJobDiagnostics>();
    builder.Services.AddHostedService<InvitationRecipientAuthorityDiscoveryWorker>();
}
var issuerAuthoritySetting = builder.Configuration["STRATAAI_INVITATION_ISSUER_AUTHORITY_DISCOVERY_ENABLED"];
var issuerAuthority = issuerAuthoritySetting is null ? runtime.Mode == RuntimeMode.Production
    : bool.TryParse(issuerAuthoritySetting, out var issuerEnabled) ? issuerEnabled
    : throw new InvalidOperationException("Invitation issuer authority discovery setting must be true or false.");
if (issuerAuthority && runtime.Mode != RuntimeMode.Production)
    throw new InvalidOperationException("Invitation issuer authority discovery requires Production mode.");
if (issuerAuthority)
{
    builder.Services.AddSingleton<IInvitationIssuerAuthorityDeliveryStore, PostgresInvitationIssuerAuthorityDeliveryStore>();
    builder.Services.AddHostedService<InvitationIssuerAuthorityWorker>();
}
if (InvitationMailRegistration.IsEnabled(builder.Configuration, runtime))
{
    if (string.IsNullOrWhiteSpace(jobScope))
        throw new InvalidOperationException("Invitation delivery requires explicit Worker Organization scope.");
    var requireVerifiedEmail = !bool.TryParse(builder.Configuration["STRATAAI_AUTH_REQUIRE_VERIFIED_EMAIL"], out var verified) || verified;
    builder.Services.AddSingleton<IInvitationDeliveryStore>(provider => new PostgresInvitationDeliveryStore(
        provider.GetRequiredService<PostgresConnectionFactory>(), requireVerifiedEmail));
    builder.Services.AddSingleton<IBackgroundJobHandler, InvitationEmailHandler>();
}
if (!string.IsNullOrWhiteSpace(jobScope))
{
    if (runtime.Mode != RuntimeMode.Production)
        throw new InvalidOperationException("Organization job execution requires Production mode.");
    var organizationIds = jobScope.Split(',', StringSplitOptions.TrimEntries)
        .Select(value => Guid.TryParse(value, out var id) && id != Guid.Empty
            ? id : throw new InvalidOperationException("Worker Organization scope contains an invalid ID."))
        .Distinct().ToArray();
    if (organizationIds.Length > 100)
        throw new InvalidOperationException("Worker Organization scope exceeds 100 IDs.");
    builder.Services.AddSingleton(new OrganizationJobScope(organizationIds));
    builder.Services.AddSingleton<BackgroundJobProcessor>();
    builder.Services.TryAddSingleton<IBackgroundJobDiagnostics, BackgroundJobDiagnostics>();
    builder.Services.AddHostedService<OrganizationJobWorker>();
    if (attachmentsEnabled) builder.Services.AddHostedService<AttachmentPreviewBackfillWorker>();
    if (attachmentsEnabled) builder.Services.AddHostedService<AttachmentScanRecoveryWorker>();
}

if (deletionDiscovery)
{
    builder.Services.AddSingleton<StrataAI.Application.Organizations.IOrganizationDeletionScopeReader,
        StrataAI.Infrastructure.Organizations.PostgresOrganizationDeletionScopeReader>();
    if (string.IsNullOrWhiteSpace(jobScope))
    {
        builder.Services.AddSingleton<BackgroundJobProcessor>();
        builder.Services.TryAddSingleton<IBackgroundJobDiagnostics, BackgroundJobDiagnostics>();
    }
    builder.Services.AddHostedService<OrganizationDeletionDiscoveryWorker>();
}

var app = builder.Build();
app.Services.InitializeAttachmentRuntime(attachmentsEnabled);

if ((deletionDiscovery || !string.IsNullOrWhiteSpace(jobScope)) && !app.Services.GetServices<IBackgroundJobHandler>().Any())
    throw new InvalidOperationException("Scoped job execution requires registered handlers.");

app.MapGet("/healthz", (IClock clock) => Results.Ok(new
{
    status = "ok",
    service = "strataai-worker",
    timestamp = clock.UtcNow,
}));

app.MapGet(
    "/readyz",
    async (IRuntimeDependencyStatus status, CancellationToken cancellationToken) =>
    {
        var ready = await status.IsReadyAsync(cancellationToken);
        return ready
            ? Results.Ok(new { status = "ready", mode = status.Mode.ToString().ToLowerInvariant() })
            : Results.Json(
                new { status = "not-ready" },
                statusCode: StatusCodes.Status503ServiceUnavailable);
    });

app.MapGet("/runtime", (RuntimeDescriptor descriptor) => Results.Ok(new
{
    service = "strataai-worker",
    mode = descriptor.Mode.ToString().ToLowerInvariant(),
    revision = descriptor.BuildRevision,
    version = descriptor.BuildVersion,
}));

await app.RunAsync();

internal sealed class WorkerHeartbeat(
    ILogger<WorkerHeartbeat> logger,
    IClock clock,
    RuntimeDescriptor runtime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["Component"] = "StrataAI.Worker",
            ["BuildRevision"] = runtime.BuildRevision,
            ["RuntimeMode"] = runtime.Mode.ToString(),
        });

        logger.LogInformation(
            "StrataAI Worker started at {Timestamp}.",
            clock.UtcNow);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
