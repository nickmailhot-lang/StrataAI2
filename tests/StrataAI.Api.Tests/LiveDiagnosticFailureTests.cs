using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using StrataAI.Api.Auth;
using StrataAI.Api.Onboarding;
using StrataAI.Api.Records;
using StrataAI.Api.WorkManagement;
using StrataAI.Application.Identity;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed class LiveDiagnosticFailureTests
{
    private static readonly IdentityPolicy Policy = new(false, true, 12, TimeSpan.FromHours(1), TimeSpan.FromMinutes(5));
    private static readonly Guid Actor = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    public static TheoryData<Type, string> Hubs => new()
    {
        { typeof(IdentityRealtimeHub), "identity_sync_unavailable" },
        { typeof(NotificationRealtimeHub), "notification_sync_unavailable" },
        { typeof(BoardStarRealtimeHub), "board_star_sync_unavailable" },
        { typeof(OrganizationBoardRealtimeHub), "organization_sync_unavailable" },
        { typeof(OrganizationMetadataRealtimeHub), "organization_sync_unavailable" },
        { typeof(OrganizationConfigurationRealtimeHub), "configuration_sync_unavailable" },
        { typeof(OrganizationLifecycleRealtimeHub), "organization_storage_unavailable" },
        { typeof(OrganizationOperationalLifecycleRealtimeHub), "organization_storage_unavailable" },
        { typeof(InvitationRecipientRealtimeHub), "invitation_sync_unavailable" },
        { typeof(RetentionHoldRealtimeHub), "retention_hold_source_unavailable" },
    };
    public static IEnumerable<object[]> FailureCases => Hubs.SelectMany(row => new[]
    {
        new object[] { row.Data.Item1, row.Data.Item2, false },
        new object[] { row.Data.Item1, row.Data.Item2, true },
    });

    [Theory]
    [MemberData(nameof(FailureCases))]
    public Task ARCH_08_live_session_failure_still_aborts_with_safe_code_when_diagnostic_writing_fails(
        Type hubType, string expected, bool writeFails) => Exercise(hubType, expected, writeFails, "session", false);

    [Theory]
    [MemberData(nameof(Hubs))]
    public Task PRD_22_caller_cancellation_is_not_replaced_by_diagnostic_or_dependency_failure(Type hubType, string expected)
        => Exercise(hubType, expected, true, "cancel", true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task PRD_02_identity_read_failure_still_aborts_without_profile_or_private_diagnostic(bool writeFails)
        => Exercise(typeof(IdentityRealtimeHub), "identity_sync_unavailable", writeFails, "read", false);

    private static async Task Exercise(Type hubType, string expected, bool writeFails, string fault, bool cancelled)
    {
        var identities = DispatchProxy.Create<IIdentityService, FailureIdentity>();
        ((FailureIdentity)(object)identities).Fault = fault;
        var context = new Caller();
        var log = (CaptureLog)Activator.CreateInstance(typeof(FailureLogger<>).MakeGenericType(hubType), [writeFails])!;
        var constructor = hubType.GetConstructors().Single();
        using var hub = (Hub)constructor.Invoke(constructor.GetParameters().Select(parameter =>
            parameter.ParameterType == typeof(IIdentityService) ? (object)identities :
            parameter.ParameterType == typeof(IdentityPolicy) ? Policy :
            parameter.ParameterType.IsGenericType && parameter.ParameterType.GetGenericTypeDefinition() == typeof(ILogger<>) ? log : null).ToArray());
        hub.Context = context;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        if (cancelled) stop.Cancel();
        var watch = hubType.GetMethod("Watch")!;
        var arguments = watch.GetParameters().Select(parameter =>
            parameter.ParameterType == typeof(CancellationToken) ? (object)stop.Token :
            parameter.ParameterType == typeof(Guid) ? Actor :
            parameter.ParameterType == typeof(long) ? 0L : null).ToArray();
        var stream = watch.Invoke(hub, arguments)!;
        // Execute the real async iterator; reflection only accommodates its
        // different public payload types and preserves the shared assertions.
        await (Task)typeof(LiveDiagnosticFailureTests).GetMethod(nameof(Verify), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(watch.ReturnType.GenericTypeArguments.Single())
            .Invoke(null, [stream, context, log, expected, stop.Token, cancelled])!;
    }

    private static async Task Verify<T>(IAsyncEnumerable<T> stream, Caller context, CaptureLog log, string expected,
        CancellationToken token, bool cancelled)
    {
        await using var iterator = stream.GetAsyncEnumerator(token);
        if (cancelled)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => { _ = await iterator.MoveNextAsync(); });
            Assert.False(context.Aborted); Assert.Equal(0, log.Attempts);
        }
        else
        {
            var error = await Assert.ThrowsAsync<HubException>(async () => { _ = await iterator.MoveNextAsync(); });
            Assert.Equal(expected, error.Message); Assert.True(context.Aborted); Assert.Equal(1, log.Attempts);
            var fields = Assert.Single(log.Entries);
            Assert.Equal(["CorrelationId", "{OriginalFormat}"], fields.Keys.Order(StringComparer.Ordinal));
            Assert.Equal("safe-live-reference", fields["CorrelationId"]);
            var diagnostic = string.Join(" ", fields.Values);
            Assert.DoesNotContain("private-session-cookie", diagnostic);
            Assert.DoesNotContain("private-storage-detail", diagnostic);
            Assert.DoesNotContain("private-logging-detail", diagnostic);
        }
        Assert.Empty(context.Items);
    }

    public class FailureIdentity : DispatchProxy
    {
        public string Fault { get; set; } = "session";
        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            if (method!.Name == nameof(IIdentityService.AuthenticateSessionAsync))
            {
                if (Fault == "cancel") return Task.FromCanceled<AuthenticatedSession?>((CancellationToken)arguments![1]!);
                if (Fault == "session") return Task.FromException<AuthenticatedSession?>(new IOException("private-storage-detail"));
                var now = DateTimeOffset.UtcNow;
                return Task.FromResult<AuthenticatedSession?>(new(Guid.NewGuid(), new(Actor, "private@example.test", "PRIVATE@EXAMPLE.TEST",
                    "Private actor", null, "en-CA", "UTC", AccountStatus.Active, true, "private-hash", now, now, 1), now.AddHours(1)));
            }
            if (method.Name == nameof(IIdentityService.ReadEventsAsync))
                return Task.FromException<IdentityOperation<IdentitySyncSnapshot>>(new IOException("private-storage-detail"));
            throw new InvalidOperationException("Unexpected identity fixture operation.");
        }
    }

    public abstract class CaptureLog
    {
        public int Attempts { get; protected set; }
        public List<Dictionary<string, object?>> Entries { get; } = [];
    }
    public sealed class FailureLogger<T>(bool writeFails) : CaptureLog, ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> formatter)
        {
            Attempts++;
            Assert.Equal(LogLevel.Warning, level); Assert.Null(error);
            Entries.Add(((IEnumerable<KeyValuePair<string, object?>>)state!).ToDictionary());
            if (writeFails) throw new InvalidOperationException("private-logging-detail");
        }
    }
    private sealed class Caller : HubCallerContext
    {
        public bool Aborted { get; private set; }
        public override string ConnectionId => "private-live-connection";
        public override string? UserIdentifier => Actor.ToString();
        public override ClaimsPrincipal? User => new(new ClaimsIdentity());
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = FeaturesForRequest();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() => Aborted = true;
        private static IFeatureCollection FeaturesForRequest()
        {
            var request = new DefaultHttpContext { TraceIdentifier = "safe-live-reference" };
            request.Request.Headers.Cookie = "strataai_session=private-session-cookie";
            var features = new FeatureCollection(); features.Set<IHttpContextFeature>(new HttpFeature { HttpContext = request }); return features;
        }
    }
    private sealed class HttpFeature : IHttpContextFeature { public HttpContext? HttpContext { get; set; } }
}
