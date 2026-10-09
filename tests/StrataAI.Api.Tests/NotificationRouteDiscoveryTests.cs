using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    // PRD-17-TC-05/07/08/09, AC-NOTIFY-17-02/03: moving routes recover
    // without weakening fresh authority or applying a failed read command.
    [Theory]
    [InlineData("inbox", false)]
    [InlineData("sync", false)]
    [InlineData("read", false)]
    [InlineData("inbox", true)]
    [InlineData("sync", true)]
    [InlineData("read", true)]
    public async Task Notification_route_discovery_restarts_without_retiring_authority_or_applying_unadmitted_reads(string operation, bool continuousMovement)
    {
        var ct = TestContext.Current.CancellationToken;
        RouteDiscoveryInbox? projection = null;
        await using var app = new ApiFactory(configureServices: services =>
        {
            var original = services.Last(service => service.ServiceType == typeof(INotificationInboxStore));
            services.Remove(original);
            services.Add(new ServiceDescriptor(typeof(INotificationInboxStore), provider =>
            {
                var inner = (INotificationInboxStore)(original.ImplementationInstance
                    ?? original.ImplementationFactory?.Invoke(provider)
                    ?? ActivatorUtilities.GetServiceOrCreateInstance(provider, original.ImplementationType!));
                return projection = new RouteDiscoveryInbox(inner);
            }, original.Lifetime));
        });
        using var owner = app.CreateClient(); using var recipient = app.CreateClient();
        var f = await NotificationFixture(app, owner, recipient, ct);
        using var created = await Mutate(owner, HttpMethod.Post, $"/lists/{f.List}/cards", new { title = "Moving notification" });
        var card = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var assignment = await Mutate(owner, HttpMethod.Put, $"/cards/{card}/members/{f.Recipient}?version=1", new { });
        Assert.Equal(HttpStatusCode.OK, assignment.StatusCode);
        using var b = await Mutate(owner, HttpMethod.Post, "/boards", new { organizationId = f.Organization, name = "Current route" });
        var board = (await b.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var grant = await Mutate(owner, HttpMethod.Patch, $"/boards/{board}/members/{f.Recipient}", new { role = "MEMBER" });
        Assert.Equal(HttpStatusCode.OK, grant.StatusCode);
        using var l = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/lists", new { name = "Current List" });
        var list = (await l.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var moved = await Mutate(owner, HttpMethod.Post, $"/cards/{card}/move", new { sourceBoardId = f.Board, destinationListId = list, expectedVersion = 2 });
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        var path = $"/organizations/{f.Organization}/notifications";
        var before = (await recipient.GetFromJsonAsync<NotificationInboxPage>(path, ct))!;
        Assert.NotEmpty(before.Items);
        Assert.All(before.Items, row => Assert.Null(row.ReadAt));
        var ids = before.Items.Select(row => row.Id).ToArray();
        Assert.NotNull(projection);
        // Retain a real, formerly valid discovery projection; subsequent reads
        // still consult the actual moved Card, source notices and permissions.
        projection.Arm(f.Board, operation == "inbox", continuousMovement ? 3 : 1);
        using var response = operation == "read"
            ? await Mutate(recipient, HttpMethod.Post, path + "/read", new { ids })
            : await recipient.GetAsync(path + (operation == "sync" ? "/sync?after=0" : ""), ct);
        Assert.Equal(continuousMovement ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(continuousMovement ? 3 : 1, projection.StaleDiscoveries);
        projection.Disarm();
        var after = (await recipient.GetFromJsonAsync<NotificationInboxPage>(path, ct))!;
        Assert.Equal(before.Items.Select(row => row.Id), after.Items.Select(row => row.Id));
        Assert.Equal(before.Items.Select(row => row.CreatedAt), after.Items.Select(row => row.CreatedAt));
        Assert.All(after.Items, row =>
        {
            Assert.Equal(board, row.CurrentBoardId);
            Assert.Equal($"/app/{f.Organization:D}/boards/{board:D}/cards/{card:D}", row.EntityLink);
            if (operation == "read" && !continuousMovement) Assert.NotNull(row.ReadAt);
            else Assert.Null(row.ReadAt);
        });
        using var removed = await Mutate(owner, HttpMethod.Delete, $"/boards/{board}/members/{f.Recipient}", new { });
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Empty((await recipient.GetFromJsonAsync<NotificationInboxPage>(path, ct))!.Items);
        using var denied = await Mutate(recipient, HttpMethod.Post, path + "/read", new { ids });
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
    }

    private sealed class RouteDiscoveryInbox(INotificationInboxStore inner) : INotificationInboxStore
    {
        private Guid formerBoard;
        private int remaining;
        private bool listDiscovery;
        private bool nextDiscovery;
        public int StaleDiscoveries { get; private set; }
        public void Arm(Guid board, bool list, int count)
        { formerBoard = board; listDiscovery = list; remaining = count; nextDiscovery = true; StaleDiscoveries = 0; }
        public void Disarm() => remaining = 0;
        private IReadOnlyList<CardNotification> Former(IReadOnlyList<CardNotification> rows)
        { remaining--; StaleDiscoveries++; return rows.Select(row => row with { CurrentBoardId = formerBoard }).ToArray(); }
        public async Task<IReadOnlyList<CardNotification>> ListVisibleAsync(Guid org, Guid recipient, NotificationCursor? after, bool verified, CancellationToken ct)
        {
            var rows = await inner.ListVisibleAsync(org, recipient, after, verified, ct);
            return remaining > 0 && listDiscovery ? Former(rows) : rows;
        }
        public async Task<IReadOnlyList<CardNotification>> FindVisibleAsync(Guid org, Guid recipient, IReadOnlyCollection<Guid> ids, bool verified, CancellationToken ct)
        {
            var rows = await inner.FindVisibleAsync(org, recipient, ids, verified, ct);
            if (remaining <= 0 || listDiscovery) return rows;
            if (nextDiscovery) { nextDiscovery = false; return Former(rows); }
            nextDiscovery = true; return rows;
        }
        public Task<IReadOnlyList<NotificationReadAcknowledgment>> MarkReadAsync(Guid org, Guid recipient, IReadOnlyCollection<Guid> ids, DateTimeOffset now, CancellationToken ct)
            => inner.MarkReadAsync(org, recipient, ids, now, ct);
    }
}
