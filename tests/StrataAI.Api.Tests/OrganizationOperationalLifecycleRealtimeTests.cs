using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Organizations;
using StrataAI.Application.Identity;
using Xunit;

namespace StrataAI.Api.Tests;
public sealed partial class ApiHostTests
{
    private const string OperationalLifecycleLive = "/organizations/live/operational-lifecycle";
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PRD_27_Demo_operational_lifecycle_live_current_member_receives_suspension_then_loses_access(bool membership)
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var member = app.CreateClient();
        await RegisterAndLogin(owner); var cookie = await RegisterAndLogin(member);
        var (actor, parent) = await ConfigurationParent(owner, "Private live lifecycle name", ct);
        var recipient = (await member.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        var organizations = app.Services.GetRequiredService<IOrganizationStore>();
        await organizations.AddOrRestoreMemberAsync(parent.Id, recipient, OrganizationRole.Member, DateTimeOffset.UtcNow, ct);
        using var socket = await LiveSocket(app, cookie, OperationalLifecycleLive);
        await SendFrame(socket, new { type = 4, invocationId = "lifecycle", target = "Watch", arguments = new object[] { parent.Id.ToString("D"), 0L } });
        var initial = await StreamItem(socket); Assert.Equal(recipient, initial.GetProperty("userId").GetGuid());
        Assert.Equal("ACTIVE", initial.GetProperty("page").GetProperty("status").GetString());
        var source = await app.Services.GetRequiredService<OrganizationOperationalLifecycleService>().ChangeAsync(parent.Id, actor,
            OrganizationStatus.Suspended, 1, Guid.NewGuid(), "Private live lifecycle correlation", ct); Assert.True(source.Succeeded);
        var changed = await StreamItem(socket); var page = changed.GetProperty("page");
        Assert.Equal("SUSPENDED", page.GetProperty("status").GetString()); Assert.Equal(recipient, page.GetProperty("actorId").GetGuid());
        var fact = Assert.Single(page.GetProperty("events").EnumerateArray()); Assert.Equal(source.Value!.EventId, fact.GetProperty("eventId").GetGuid());
        Assert.Equal(11, fact.EnumerateObject().Count()); Assert.DoesNotContain("Private live lifecycle", changed.GetRawText());
        if (membership) Assert.Equal(OrganizationRemoveMemberResult.Removed, await organizations.RemoveMemberAsync(parent.Id, recipient, DateTimeOffset.UtcNow, ct));
        else { using var logout = await Mutate(member, HttpMethod.Post, "/auth/logout", new { }); Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode); }
        await AssertOperationalLifecycleClosed(socket, ct);
    }
    [Fact]
    public async Task PRD_27_Demo_operational_lifecycle_live_reconnect_recovers_only_missed_reactivation()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory(); using var owner = app.CreateClient(); var cookie = await RegisterAndLogin(owner);
        var (actor, parent) = await ConfigurationParent(owner, "Reconnect lifecycle", ct); var service = app.Services.GetRequiredService<OrganizationOperationalLifecycleService>();
        Assert.True((await service.ChangeAsync(parent.Id, actor, OrganizationStatus.Suspended, 1, Guid.NewGuid(), "reconnect-suspend", ct)).Succeeded);
        using (var first = await LiveSocket(app, cookie, OperationalLifecycleLive))
        {
            await SendFrame(first, new { type = 4, invocationId = "before", target = "Watch", arguments = new object[] { parent.Id.ToString("D"), 0L } });
            Assert.Equal(2, Assert.Single((await StreamItem(first)).GetProperty("page").GetProperty("events").EnumerateArray()).GetProperty("version").GetInt64());
            first.Abort();
        }
        var source = await service.ChangeAsync(parent.Id, actor, OrganizationStatus.Active, 2, Guid.NewGuid(), "reconnect-reactivate", ct); Assert.True(source.Succeeded);
        using var resumed = await LiveSocket(app, cookie, OperationalLifecycleLive);
        await SendFrame(resumed, new { type = 4, invocationId = "after", target = "Watch", arguments = new object[] { parent.Id.ToString("D"), 2L } });
        var page = (await StreamItem(resumed)).GetProperty("page"); Assert.Equal("ACTIVE", page.GetProperty("status").GetString());
        Assert.Equal(source.Value!.EventId, Assert.Single(page.GetProperty("events").EnumerateArray()).GetProperty("eventId").GetGuid());
    }
    [Fact]
    public async Task PRD_27_Demo_operational_lifecycle_live_drains_fifty_source_pages_without_skipping()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory(); using var owner = app.CreateClient(); var cookie = await RegisterAndLogin(owner);
        var (actor, parent) = await ConfigurationParent(owner, "Live lifecycle pages", ct); var service = app.Services.GetRequiredService<OrganizationOperationalLifecycleService>();
        for (var version = 1; version <= 51; version++)
        {
            var changed = await service.ChangeAsync(parent.Id, actor, version % 2 == 1 ? OrganizationStatus.Suspended : OrganizationStatus.Active,
                version, Guid.NewGuid(), "live-pages", ct);
            Assert.True(changed.Succeeded, $"Lifecycle stream setup failed at revision {version}: {changed.ErrorCode}");
        }
        using var socket = await LiveSocket(app, cookie, OperationalLifecycleLive);
        await SendFrame(socket, new { type = 4, invocationId = "pages", target = "Watch", arguments = new object[] { parent.Id.ToString("D"), 0L } });
        var first = (await StreamItem(socket)).GetProperty("page"); Assert.Equal(50, first.GetProperty("events").GetArrayLength());
        Assert.Equal(51, first.GetProperty("nextVersion").GetInt64());
        var next = (await StreamItem(socket)).GetProperty("page"); Assert.Equal(51, next.GetProperty("afterVersion").GetInt64());
        Assert.Equal(52, Assert.Single(next.GetProperty("events").EnumerateArray()).GetProperty("version").GetInt64());
    }
    [Theory]
    [InlineData(null)]
    [InlineData("http://evil.example.test")]
    public async Task PRD_27_Demo_operational_lifecycle_live_rejects_missing_or_foreign_origin(string? origin)
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory(); using var client = app.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, OperationalLifecycleLive + "/negotiate?negotiateVersion=1");
        request.Headers.Add("X-StrataAI-Request", "1"); if (origin is not null) request.Headers.Add("Origin", origin);
        using var response = await client.SendAsync(request, ct); Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
    [Fact]
    public async Task PRD_27_Demo_operational_lifecycle_live_foreign_actor_receives_no_stream_item()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var outsider = app.CreateClient();
        await RegisterAndLogin(owner); var cookie = await RegisterAndLogin(outsider); var (_, parent) = await ConfigurationParent(owner, "Foreign live target", ct);
        using var socket = await LiveSocket(app, cookie, OperationalLifecycleLive);
        await SendFrame(socket, new { type = 4, invocationId = "foreign", target = "Watch", arguments = new object[] { parent.Id.ToString("D"), 0L } });
        await AssertOperationalLifecycleClosed(socket, ct);
    }
    [Fact]
    public async Task PRD_27_Demo_operational_lifecycle_live_limits_subscriptions_and_releases_cancelled_streams()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory(); using var owner = app.CreateClient(); var cookie = await RegisterAndLogin(owner);
        var (_, parent) = await ConfigurationParent(owner, "Live lifecycle subscription", ct); using var socket = await LiveSocket(app, cookie, OperationalLifecycleLive);
        var arguments = new object[] { parent.Id.ToString("D"), 0L };
        await SendFrame(socket, new { type = 4, invocationId = "first", target = "Watch", arguments }); await StreamItem(socket);
        await SendFrame(socket, new { type = 4, invocationId = "duplicate", target = "Watch", arguments });
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(8));
        var rejected = await Frame(socket, deadline.Token); Assert.Equal("duplicate", rejected!.Value.GetProperty("invocationId").GetString());
        Assert.Contains("subscription_limit", rejected.Value.GetProperty("error").GetString());
        await SendFrame(socket, new { type = 5, invocationId = "first" });
        var completed = await Frame(socket, deadline.Token); Assert.Equal("first", completed!.Value.GetProperty("invocationId").GetString());
        await SendFrame(socket, new { type = 4, invocationId = "replacement", target = "Watch", arguments });
        Assert.Empty((await StreamItem(socket)).GetProperty("page").GetProperty("events").EnumerateArray());
    }
    private static async Task AssertOperationalLifecycleClosed(WebSocket socket, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(8)); var closed = false;
        try
        {
            while (!closed)
            {
                var frame = await Frame(socket, deadline.Token);
                closed = frame is null || frame.Value.TryGetProperty("type", out var type) && type.GetInt32() is 3 or 7;
                if (frame is not null && !closed) Assert.NotEqual(2, frame.Value.GetProperty("type").GetInt32());
            }
        }
        catch (WebSocketException) { closed = true; }
        Assert.True(closed);
    }

    [Fact]
    public async Task PRD_27_Demo_operational_lifecycle_live_session_IO_withdrawal_discards_the_captured_page()
    {
        var ct = TestContext.Current.CancellationToken; LifecycleSessionProxy? proxy = null;
        await using var app = new ApiFactory(configureServices: services =>
        {
            var original = services.Single(item => item.ServiceType == typeof(IIdentityService)); services.Remove(original);
            services.AddSingleton<IIdentityService>(provider =>
            {
                var service = DispatchProxy.Create<IIdentityService, LifecycleSessionProxy>(); proxy = (LifecycleSessionProxy)(object)service;
                proxy.Inner = (IIdentityService)original.ImplementationFactory!(provider); return service;
            });
        });
        using var owner = app.CreateClient(); using var member = app.CreateClient(); await RegisterAndLogin(owner); var cookie = await RegisterAndLogin(member);
        var (actor, parent) = await ConfigurationParent(owner, "Session IO lifecycle", ct);
        var recipient = (await member.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        var organizations = app.Services.GetRequiredService<IOrganizationStore>();
        await organizations.AddOrRestoreMemberAsync(parent.Id, recipient, OrganizationRole.Member, DateTimeOffset.UtcNow, ct);
        using var socket = await LiveSocket(app, cookie, OperationalLifecycleLive);
        await SendFrame(socket, new { type = 4, invocationId = "fenced", target = "Watch", arguments = new object[] { parent.Id.ToString("D"), 0L } });
        await StreamItem(socket);
        var committed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        proxy!.Recipient = recipient; proxy.BeforeNextRead = () => committed.Task.WaitAsync(ct);
        proxy.AfterPageSessionIO = async () => Assert.Equal(OrganizationRemoveMemberResult.Removed,
            await organizations.RemoveMemberAsync(parent.Id, recipient, DateTimeOffset.UtcNow, ct));
        var source = await app.Services.GetRequiredService<OrganizationOperationalLifecycleService>().ChangeAsync(parent.Id, actor,
            OrganizationStatus.Suspended, 1, Guid.NewGuid(), "session-IO-suspend", ct);
        committed.SetResult(); Assert.True(source.Succeeded, $"Session fence setup failed: {source.ErrorCode}");
        await AssertOperationalLifecycleClosed(socket, ct); Assert.Equal(1, proxy.Withdrawals);
    }
    public class LifecycleSessionProxy : DispatchProxy
    {
        public IIdentityService Inner { get; set; } = null!;
        public Guid Recipient { get; set; }
        public Func<Task>? BeforeNextRead { get; set; }
        public Func<Task>? AfterPageSessionIO { get; set; }
        private int _calls; public int Withdrawals { get; private set; }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            var value = method!.Invoke(Inner, args);
            return method.Name == nameof(IIdentityService.AuthenticateSessionAsync)
                ? Fence((Task<AuthenticatedSession?>)value!) : value;
        }
        private async Task<AuthenticatedSession?> Fence(Task<AuthenticatedSession?> task)
        {
            var session = await task;
            if (session?.User.Id == Recipient && AfterPageSessionIO is { } withdraw)
            {
                var call = Interlocked.Increment(ref _calls);
                if (call == 1 && BeforeNextRead is { } before) await before();
                if (call == 2) { await withdraw(); Withdrawals++; }
            }
            return session;
        }
    }
}
