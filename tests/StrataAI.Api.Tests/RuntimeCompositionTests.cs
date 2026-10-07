using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.Identity;
using StrataAI.Application.Onboarding;
using StrataAI.Application.Organizations;
using StrataAI.Application.Runtime;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.Persistence;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed class RuntimeCompositionTests
{
    // ARCH-05-FR-009: resolve the actual API's startup graph, without substituting
    // stores or connecting to a database. Database/RLS behavior has separate gates.
    [Fact]
    public async Task ARCH_05_Production_API_resolves_implemented_durable_boundaries_without_Demo_fallback()
    {
        await using var app = new RuntimeCompositionFactory("production");
        using var client = app.CreateClient();
        var services = app.Services;
        Assert.Equal(RuntimeMode.Production, services.GetRequiredService<RuntimeDescriptor>().Mode);
        Assert.IsType<PostgresConnectionFactory>(services.GetRequiredService<PostgresConnectionFactory>());
        var boundaries = new (Type Contract, string Implementation)[]
        {
            (typeof(IIdentityStore), "PostgresIdentityStore"),
            (typeof(IIdentityUnitOfWork), "PostgresIdentityUnitOfWork"),
            (typeof(IIdentityProfileReplayStore), "PostgresIdentityProfileReplayStore"),
            (typeof(IIdentityLoginReplayStore), "PostgresIdentityLoginReplayStore"),
            (typeof(IIdentityTokenConsumptionReplayStore), "PostgresIdentityTokenConsumptionReplayStore"),
            (typeof(IOrganizationStore), "PostgresOrganizationStore"),
            (typeof(IOrganizationUnitOfWork), "PostgresOrganizationUnitOfWork"),
            (typeof(IOrganizationMetadataEventReader), "PostgresOrganizationMetadataEventReader"),
            (typeof(IOrganizationLifecycleEventReader), "PostgresOrganizationLifecycleEventReader"),
            (typeof(IOrganizationDeletionJobPublisher), "PostgresOrganizationDeletionJobPublisher"),
            (typeof(IOrganizationDeletionObservationReader), "PostgresOrganizationDeletionObservationReader"),
            (typeof(IInvitationStore), "PostgresInvitationStore"),
            (typeof(IInvitationHistoryStore), "PostgresInvitationStore"),
            (typeof(IInvitationRecipientEventReader), "PostgresInvitationRecipientEventReader"),
            (typeof(IWorkManagementStore), "PostgresWorkManagementStore"),
            (typeof(IWorkManagementUnitOfWork), "PostgresWorkManagementUnitOfWork"),
            (typeof(IWorkEventStore), "PostgresWorkEventStore"),
            (typeof(IWorkEventReader), "PostgresWorkEventReader"),
            (typeof(INotificationInboxStore), "PostgresWorkNotificationStore"),
            (typeof(INotificationRealtimeStore), "PostgresWorkNotificationStore"),
            (typeof(IWatchSubscriptionStore), "PostgresWatchSubscriptionStore"),
            (typeof(ICardReminderStore), "PostgresCardReminderStore"),
            (typeof(IBackgroundJobStore), "PostgresBackgroundJobStore"),
        };
        foreach (var (contract, implementation) in boundaries)
        {
            var resolved = services.GetRequiredService(contract);
            Assert.Equal(implementation, resolved.GetType().Name);
            Assert.StartsWith("StrataAI.Infrastructure.", resolved.GetType().Namespace!);
        }
        Assert.Null(services.GetService<IDemoDataStore>());
        Assert.Null(services.GetService<IDemoOrganizationDeletionSimulation>());
        Assert.DoesNotContain(services.GetServices<IHostedService>(), host => host.GetType().Name.Contains("Demo", StringComparison.Ordinal));
        var policy = services.GetRequiredService<IdentityPolicy>();
        Assert.False(policy.AllowSelfRegistration); Assert.True(policy.RequireVerifiedEmail);
        // Disabled optional transports are absent; they are never replaced with
        // a Demo mail sender. Enabled durable transport is covered separately.
        Assert.Null(services.GetService<IIdentityEmailProvider>());
        Assert.Null(services.GetService<IInvitationMailPublisher>());
        using var health = await client.GetAsync("/healthz", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Fact]
    public async Task ARCH_05_Production_API_does_not_map_any_sample_catalog_operation()
    {
        await using var app = new RuntimeCompositionFactory("production");
        using var client = app.CreateClient();
        foreach (var (method, route) in new[] { (HttpMethod.Get, "/api/demo/state"),
            (HttpMethod.Delete, "/api/demo/state"), (HttpMethod.Post, "/api/demo/reset") })
        {
            using var request = new HttpRequestMessage(method, route);
            request.Headers.Add("X-StrataAI-Request", "1");
            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.DoesNotContain("Quail Ridge Demo", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }
        var runtime = await client.GetFromJsonAsync<JsonElement>("/api/runtime", TestContext.Current.CancellationToken);
        Assert.Equal("production", runtime.GetProperty("mode").GetString());
    }

    [Fact]
    public async Task ARCH_05_enabled_Production_mail_resolves_durable_intent_and_keeps_transport_out_of_API()
    {
        await using var app = new RuntimeCompositionFactory("production", enableMail: true);
        using var client = app.CreateClient();
        var services = app.Services;
        Assert.True(services.GetRequiredService<IdentityPolicy>().EmailDeliveryEnabled);
        Assert.Equal("PostgresInvitationMailPublisher", services.GetRequiredService<IInvitationMailPublisher>().GetType().Name);
        var signer = services.GetRequiredService<IIdentityDeliveryTokenSigner>();
        Assert.Equal("IdentityDeliveryTokenSigner", signer.GetType().Name);
        Assert.Same(signer, services.GetRequiredService<IInvitationDeliveryTokenSigner>());
        Assert.Null(services.GetService<IIdentityEmailProvider>());
        Assert.Null(services.GetService<IDemoDataStore>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("hybrid")]
    public async Task ARCH_05_missing_or_unknown_mode_refuses_real_API_startup(string? mode)
    {
        await using var app = new RuntimeCompositionFactory(mode);
        var failure = Assert.ThrowsAny<Exception>(() => app.CreateClient());
        Assert.Contains("STRATAAI_RUNTIME_MODE", failure.ToString());
    }

    private sealed class RuntimeCompositionFactory(string? mode, bool enableMail = false) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Development");

        protected override IHost CreateHost(IHostBuilder builder)
        {
            // A deliberately unreachable fixture endpoint: graph/route tests
            // must not depend on, or accidentally write to, an ambient database.
            builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["STRATAAI_RUNTIME_MODE"] = mode,
                ["ConnectionStrings:Postgres"] = "Host=127.0.0.1;Port=1;Database=fixture;Username=fixture;Password=fixture;Timeout=1",
                ["STRATAAI_AUTH_RETRY_CURRENT_KEY"] = "fixture",
                ["STRATAAI_AUTH_RETRY_KEYS"] = JsonSerializer.Serialize(new Dictionary<string, string>
                    { ["fixture"] = Convert.ToBase64String(new byte[32]) }),
                ["STRATAAI_IDENTITY_EMAIL_ENABLED"] = enableMail ? "true" : "false",
                ["STRATAAI_INVITATION_EMAIL_ENABLED"] = enableMail ? "true" : "false",
                ["STRATAAI_IDENTITY_EMAIL_FROM"] = "sender@strataai.test",
                ["STRATAAI_PUBLIC_ORIGIN"] = "https://strataai.test",
                ["STRATAAI_IDENTITY_EMAIL_ACCOUNT"] = "fixture",
                ["STRATAAI_IDENTITY_TOKEN_CURRENT_KEY"] = "fixture",
                ["STRATAAI_IDENTITY_TOKEN_KEYS"] = JsonSerializer.Serialize(new Dictionary<string, string>
                    { ["fixture"] = Convert.ToBase64String(new byte[32]) }),
                ["STRATAAI_ATTACHMENT_STORAGE_ENABLED"] = "false",
                ["STRATAAI_REALTIME_PUBLIC_ORIGIN"] = "http://localhost",
                ["Logging:LogLevel:Default"] = "Warning",
            }));
            return base.CreateHost(builder);
        }
    }
}
