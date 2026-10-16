using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    // PRD-17-TC-05/08/09/10: rediscover Card eligibility only after all Board
    // gates remain valid; actual Board withdrawal retains its denial contract.
    [Theory]
    [InlineData("inbox", false, false)]
    [InlineData("sync", false, false)]
    [InlineData("read", false, false)]
    [InlineData("inbox", true, false)]
    [InlineData("sync", true, false)]
    [InlineData("read", true, false)]
    [InlineData("inbox", false, true)]
    [InlineData("sync", false, true)]
    [InlineData("read", false, true)]
    [InlineData("inbox", true, true)]
    [InlineData("sync", true, true)]
    [InlineData("read", true, true)]
    public async Task Notification_eligibility_discovery_releases_stale_plans_without_disclosure_or_partial_read(
        string operation, bool continuousChange, bool removeBoardAccess)
    {
        var ct = TestContext.Current.CancellationToken;
        EligibilityDiscoveryInbox? projection = null;
        await using var app = new ApiFactory(configureServices: services =>
        {
            var original = services.Last(service => service.ServiceType == typeof(INotificationInboxStore));
            services.Remove(original);
            services.Add(new ServiceDescriptor(typeof(INotificationInboxStore), provider =>
            {
                var inner = (INotificationInboxStore)(original.ImplementationInstance
                    ?? original.ImplementationFactory?.Invoke(provider)
                    ?? ActivatorUtilities.GetServiceOrCreateInstance(provider, original.ImplementationType!));
                return projection = new EligibilityDiscoveryInbox(inner);
            }, original.Lifetime));
            var transactions = services.Last(service => service.ServiceType == typeof(IWorkManagementUnitOfWork));
            services.Remove(transactions);
            services.Add(new ServiceDescriptor(typeof(IWorkManagementUnitOfWork), provider =>
            {
                var inner = (IWorkManagementUnitOfWork)(transactions.ImplementationInstance
                    ?? transactions.ImplementationFactory?.Invoke(provider)
                    ?? ActivatorUtilities.GetServiceOrCreateInstance(provider, transactions.ImplementationType!));
                return new EligibilityDiscoveryUnitOfWork(inner, () => projection);
            }, transactions.Lifetime));
        });
        using var owner = app.CreateClient(); using var recipient = app.CreateClient();
        var f = await NotificationFixture(app, owner, recipient, ct);
        using var secondBoard = await Mutate(owner, HttpMethod.Post, "/boards",
            new { organizationId = f.Organization, name = "Surviving Board" });
        Assert.Equal(HttpStatusCode.Created, secondBoard.StatusCode);
        var board = (await secondBoard.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var grant = await Mutate(owner, HttpMethod.Patch, $"/boards/{board}/members/{f.Recipient}", new { role = "MEMBER" });
        Assert.Equal(HttpStatusCode.OK, grant.StatusCode);
        using var secondList = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/lists", new { name = "Surviving List" });
        Assert.Equal(HttpStatusCode.Created, secondList.StatusCode);
        var list = (await secondList.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        var cards = new List<Guid>();
        foreach (var parent in new[] { f.List, list })
        {
            using var created = await Mutate(owner, HttpMethod.Post, $"/lists/{parent}/cards", new { title = "Private notification source" });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var card = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid(); cards.Add(card);
            using var assigned = await Mutate(owner, HttpMethod.Put, $"/cards/{card}/members/{f.Recipient}?version=1", new { });
            Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);
        }
        var path = $"/organizations/{f.Organization}/notifications";
        var before = (await recipient.GetFromJsonAsync<NotificationInboxPage>(path, ct))!;
        Assert.Equal(2, before.Items.Count);
        var hidden = Assert.Single(before.Items, row => row.EntityId == cards[0]);
        var survivor = Assert.Single(before.Items, row => row.EntityId == cards[1]);
        Assert.NotNull(projection);
        projection.Capture();
        using var withdrawal = removeBoardAccess
            ? await Mutate(owner, HttpMethod.Delete, $"/boards/{f.Board}/members/{f.Recipient}", new { })
            : await Mutate(owner, HttpMethod.Post, $"/cards/{cards[0]}/archive", new { version = 2 });
        Assert.Equal(removeBoardAccess ? HttpStatusCode.NoContent : HttpStatusCode.OK, withdrawal.StatusCode);
        projection.Arm(operation == "inbox", continuousChange ? 3 : 1);
        using var response = operation == "read"
            ? await Mutate(recipient, HttpMethod.Post, path + "/read", new { ids = before.Items.Select(row => row.Id).ToArray() })
            : await recipient.GetAsync(path + (operation == "sync" ? "/sync?after=0" : ""), ct);
        Assert.Equal(removeBoardAccess ? HttpStatusCode.NotFound : continuousChange ? HttpStatusCode.ServiceUnavailable
            : operation == "read" ? HttpStatusCode.NotFound : HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(continuousChange && !removeBoardAccess ? 3 : 1, projection.StaleDiscoveries);
        Assert.Equal(0, projection.ReadWrites);
        var body = await response.Content.ReadAsStringAsync(ct);
        Assert.DoesNotContain(hidden.Id.ToString(), body); Assert.DoesNotContain(cards[0].ToString(), body);
        if (!continuousChange && !removeBoardAccess && operation != "read")
        {
            using var result = JsonDocument.Parse(body);
            if (operation == "inbox") Assert.Equal(survivor.Id, Assert.Single(result.RootElement.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
            else
            {
                Assert.Equal("2", result.RootElement.GetProperty("cursor").GetString());
                Assert.Equal(survivor.Id, Assert.Single(result.RootElement.GetProperty("events").EnumerateArray()).GetProperty("entityId").GetGuid());
            }
        }
        projection.Disarm();
        var current = Assert.Single((await recipient.GetFromJsonAsync<NotificationInboxPage>(path, ct))!.Items);
        Assert.Equal(survivor, current); Assert.Null(current.ReadAt);
    }

    private sealed class EligibilityDiscoveryInbox(INotificationInboxStore inner) : INotificationInboxStore
    {
        private IReadOnlyList<CardNotification> latest = [], captured = [];
        private int remaining;
        private bool listDiscovery, nextDiscovery;
        public int StaleDiscoveries { get; private set; }
        public int ReadWrites { get; private set; }
        public void Capture() => captured = latest.ToArray();
        public void Arm(bool list, int count) { listDiscovery = list; remaining = count; nextDiscovery = true; }
        public void BeginAttempt() => nextDiscovery = true;
        public void Disarm() => remaining = 0;
        private IReadOnlyList<CardNotification> Stale(IReadOnlyCollection<Guid>? ids = null)
        { remaining--; StaleDiscoveries++; return captured.Where(row => ids is null || ids.Contains(row.Id)).ToArray(); }
        public async Task<IReadOnlyList<CardNotification>> ListVisibleAsync(Guid org, Guid recipient, NotificationCursor? after, bool verified, CancellationToken ct)
        {
            latest = await inner.ListVisibleAsync(org, recipient, after, verified, ct);
            return remaining > 0 && listDiscovery ? Stale() : latest;
        }
        public async Task<IReadOnlyList<CardNotification>> FindVisibleAsync(Guid org, Guid recipient, IReadOnlyCollection<Guid> ids, bool verified, CancellationToken ct)
        {
            var rows = await inner.FindVisibleAsync(org, recipient, ids, verified, ct);
            if (remaining <= 0 || listDiscovery) return rows;
            if (nextDiscovery) { nextDiscovery = false; return Stale(ids); }
            return rows;
        }
        public Task<IReadOnlyList<NotificationReadAcknowledgment>> MarkReadAsync(Guid org, Guid recipient,
            IReadOnlyCollection<Guid> ids, DateTimeOffset now, CancellationToken ct)
        { ReadWrites++; return inner.MarkReadAsync(org, recipient, ids, now, ct); }
    }

    private sealed class EligibilityDiscoveryUnitOfWork(IWorkManagementUnitOfWork inner,
        Func<EligibilityDiscoveryInbox?> projection) : IWorkManagementUnitOfWork
    {
        public Task<WorkOperation<T>> ExecuteReadAsync<T>(Guid organizationId, Guid? actorId, string scopeFailureCode,
            Func<Task<bool>> authorize, Func<Task<WorkOperation<T>>> operation, CancellationToken cancellationToken = default)
            => inner.ExecuteReadAsync(organizationId, actorId, scopeFailureCode, authorize, operation, cancellationToken);
        public Task<WorkOperation<T>> ExecuteAsync<T>(Guid organizationId, WorkCommand command,
            Func<T?, Task<bool>> authorizeReplay, Func<Task<WorkOperation<T>>> operation, CancellationToken cancellationToken = default)
        {
            // Rediscovery starts another real transaction, including when the
            // previous attempt stopped before its post-lock visibility query.
            if (command.ScopeFailureCode == "notification_not_found") projection()?.BeginAttempt();
            return inner.ExecuteAsync(organizationId, command, authorizeReplay, operation, cancellationToken);
        }
    }
}
