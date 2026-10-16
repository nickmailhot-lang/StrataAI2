using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StrataAI.Api;
using StrataAI.Api.WorkManagement;
using StrataAI.Application.Runtime;
using StrataAI.Application.Identity;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed class WorkRealtimeMiddlewareDiagnosticTests
{
    public static IEnumerable<object[]> Cases => new[] { "io", "cancel", "started", "role", "schema", "origin", "disabled" }
        .SelectMany(fault => new[] { new object[] { fault, false }, new object[] { fault, true } });

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task ARCH_08_live_middleware_preserves_outcome_and_refusals_when_logging_fails(string fault, bool writeFails)
    {
        using var services = new ServiceCollection().AddLogging().AddProblemDetails(options => options.CustomizeProblemDetails = ApiErrorContract.Customize).BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services, TraceIdentifier = "safe-live-reference" };
        if (fault == "started") context.Features.Set<IHttpResponseFeature>(new StartedResponse());
        context.Response.Body = new MemoryStream(); context.Response.StatusCode = 202;
        context.Request.Path = "/boards/live/negotiate";
        context.Request.Headers.Origin = fault == "origin" ? "https://untrusted.example.test" : "http://localhost";
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        if (fault == "cancel") caller.Cancel();
        context.RequestAborted = caller.Token;
        Exception error = fault switch
        {
            "cancel" => new OperationCanceledException(caller.Token),
            "role" => new RuntimeDatabaseRoleException(),
            "schema" => new RuntimeDatabaseSchemaException(),
            _ => new IOException("private-storage-detail"),
        };
        var calls = 0;
        var middleware = new WorkRealtimeOriginMiddleware(_ => { calls++; return Task.FromException(error); });
        var origin = new WorkRealtimeOrigin(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["STRATAAI_REALTIME_PUBLIC_ORIGIN"] = fault == "disabled" ? null : "http://localhost" }).Build());
        var logger = new LiveDiagnosticFailureTests.FailureLogger<WorkRealtimeOriginMiddleware>(writeFails);
        var unexpected = await Record.ExceptionAsync(() => middleware.InvokeAsync(context, origin, logger));
        if (fault is "cancel" or "started" or "role" or "schema")
        {
            Assert.Same(error, unexpected); Assert.Equal(202, context.Response.StatusCode);
            Assert.Equal(0, logger.Attempts); Assert.Equal(0, context.Response.Body.Length);
        }
        else
        {
            Assert.Null(unexpected);
            Assert.Equal(fault == "origin" ? 403 : 503, context.Response.StatusCode);
            context.Response.Body.Position = 0;
            var body = await JsonSerializer.DeserializeAsync<JsonElement>(context.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(fault == "origin" ? "realtime_origin_denied" : "realtime_unavailable", body.GetProperty("code").GetString());
            Assert.Equal("safe-live-reference", body.GetProperty("correlationId").GetString());
            Assert.DoesNotContain("private-", body.GetRawText());
            Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
            Assert.Equal(fault == "io" ? 1 : 0, logger.Attempts);
        }
        Assert.Equal(fault is "origin" or "disabled" ? 0 : 1, calls);
    }

    private sealed class StartedResponse : HttpResponseFeature { public override bool HasStarted => true; }
}

public sealed partial class ApiHostTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PRD_22_work_sync_failure_retains_503_and_private_problem_when_logging_fails(bool writeFails)
    {
        var logger = new LiveDiagnosticFailureTests.FailureLogger<WorkSynchronizationService>(writeFails);
        var unavailable = new UnavailableBoardScope();
        await using var app = new ApiFactory(configureServices: services =>
        {
            services.AddSingleton<IWorkBoardAuthorization>(unavailable);
            services.AddSingleton<ILogger<WorkSynchronizationService>>(logger);
        });
        using var client = app.CreateClient();
        using var response = await client.GetAsync($"/boards/{Guid.NewGuid()}/sync", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("work_sync_unavailable", body.GetProperty("code").GetString());
        Assert.Equal("Synchronization is temporarily unavailable.", body.GetProperty("message").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("correlationId").GetString()));
        Assert.DoesNotContain("private-", body.GetRawText());
        Assert.False(body.TryGetProperty("events", out _)); Assert.False(body.TryGetProperty("cursor", out _));
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        Assert.Equal(1, unavailable.Reads); Assert.Equal(1, logger.Attempts); Assert.Single(logger.Entries);
    }

    [Theory]
    [InlineData("session")]
    [InlineData("scope")]
    public async Task PRD_22_work_diagnostic_failure_aborts_transport_without_disclosing_events(string fault)
    {
        WireDiagnosticIdentity? identities = null;
        var unavailable = new UnavailableBoardScope();
        var logger = new LiveDiagnosticFailureTests.FailureLogger<WorkRealtimeHub>(true);
        await using var app = new ApiFactory(configureServices: services =>
        {
            var original = services.Single(item => item.ServiceType == typeof(IIdentityService)); services.Remove(original);
            services.AddSingleton<IIdentityService>(provider =>
            {
                var service = DispatchProxy.Create<IIdentityService, WireDiagnosticIdentity>();
                identities = (WireDiagnosticIdentity)(object)service;
                identities.Inner = (IIdentityService)original.ImplementationFactory!(provider); return service;
            });
            services.AddSingleton<IWorkBoardAuthorization>(unavailable);
            services.AddSingleton<ILogger<WorkRealtimeHub>>(logger);
        });
        using var client = app.CreateClient(); var cookie = await RegisterAndLogin(client);
        using var socket = await LiveSocket(app, cookie);
        identities!.Fault = fault == "session" ? "session" : null;
        await SendFrame(socket, new { type = 4, invocationId = "work-diagnostic-failure", target = "Watch", arguments = new[] { Guid.NewGuid().ToString(), "0" } });
        await AssertDiagnosticTransportAborted(socket);
        Assert.Equal(fault == "session" ? 0 : 1, unavailable.Reads);
        Assert.Equal(1, logger.Attempts); Assert.Single(logger.Entries);
    }

    private sealed class UnavailableBoardScope : IWorkBoardAuthorization
    {
        public int Reads { get; private set; }
        public Task<WorkOperation<BoardSyncScope>> GetSyncScopeAsync(Guid boardId, Guid? actorId, CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromException<WorkOperation<BoardSyncScope>>(new IOException("private-storage-detail"));
        }
    }
}
