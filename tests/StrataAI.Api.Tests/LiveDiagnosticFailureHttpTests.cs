using System.Net.WebSockets;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StrataAI.Api.Auth;
using StrataAI.Application.Identity;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Theory]
    [InlineData("session")]
    [InlineData("read")]
    public async Task PRD_02_live_diagnostic_failure_aborts_transport_without_disclosing_a_profile(string fault)
    {
        WireDiagnosticIdentity? identities = null;
        var logger = new LiveDiagnosticFailureTests.FailureLogger<IdentityRealtimeHub>(true);
        await using var app = new ApiFactory(configureServices: services =>
        {
            var original = services.Single(item => item.ServiceType == typeof(IIdentityService)); services.Remove(original);
            services.AddSingleton<IIdentityService>(provider =>
            {
                var service = DispatchProxy.Create<IIdentityService, WireDiagnosticIdentity>();
                identities = (WireDiagnosticIdentity)(object)service;
                identities.Inner = (IIdentityService)original.ImplementationFactory!(provider); return service;
            });
            services.AddSingleton<ILogger<IdentityRealtimeHub>>(logger);
        });
        using var client = app.CreateClient();
        var cookie = await RegisterAndLogin(client);
        using var socket = await LiveSocket(app, cookie, "/me/live");
        identities!.Fault = fault;
        await SendFrame(socket, new { type = 4, invocationId = "diagnostic-failure", target = "Watch", arguments = new string?[] { null } });
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        var closed = false;
        try
        {
            while (!closed)
            {
                var frame = await Frame(socket, deadline.Token);
                if (frame is null) { closed = true; break; }
                var type = frame.Value.GetProperty("type").GetInt32();
                Assert.NotEqual(2, type); // No captured or fallback profile may be emitted.
                var text = frame.Value.GetRawText();
                Assert.DoesNotContain("private-storage-detail", text);
                Assert.DoesNotContain("private-logging-detail", text);
                closed = type == 7;
                // A completion error alone is not proof of connection abort.
            }
        }
        catch (WebSocketException) { closed = true; }
        Assert.True(closed); Assert.Equal(1, logger.Attempts);
        Assert.Single(logger.Entries);
    }

    public class WireDiagnosticIdentity : DispatchProxy
    {
        public IIdentityService Inner { get; set; } = null!;
        public string? Fault { get; set; }
        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            if (Fault == "session" && method!.Name == nameof(IIdentityService.AuthenticateSessionAsync))
                return Task.FromException<AuthenticatedSession?>(new IOException("private-storage-detail"));
            if (Fault == "read" && method!.Name == nameof(IIdentityService.ReadEventsAsync))
                return Task.FromException<IdentityOperation<IdentitySyncSnapshot>>(new IOException("private-storage-detail"));
            return method!.Invoke(Inner, arguments);
        }
    }
}
