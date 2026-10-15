using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Infrastructure.Runtime;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed class ProductionReadinessTests
{
    [Fact]
    public async Task ARCH_08_unresponsive_database_handshake_refuses_readiness_within_probe_budget()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        await using var connections = new PostgresConnectionFactory(
            $"Host=127.0.0.1;Port={port};Database=readiness_fixture;Username=fixture;Password=fixture;SSL Mode=Disable;Timeout=30");
        var status = new ProductionRuntimeDependencyStatus(connections);
        var clock = Stopwatch.StartNew();
        var probe = status.IsReadyAsync(TestContext.Current.CancellationToken);
        using var peer = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken).AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.False(await probe.WaitAsync(TimeSpan.FromSeconds(6), TestContext.Current.CancellationToken));
        Assert.InRange(clock.Elapsed, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ARCH_08_caller_cancellation_is_not_reported_as_dependency_readiness()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        await using var connections = new PostgresConnectionFactory(
            $"Host=127.0.0.1;Port={port};Database=readiness_fixture;Username=fixture;Password=fixture;SSL Mode=Disable;Timeout=30");
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var probe = new ProductionRuntimeDependencyStatus(connections).IsReadyAsync(caller.Token);
        using var peer = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken).AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await caller.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => probe.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ARCH_08_pre_cancelled_probe_does_not_become_not_ready_success()
    {
        await using var connections = new PostgresConnectionFactory(
            "Host=127.0.0.1;Port=1;Database=readiness_fixture;Username=fixture;Password=fixture;SSL Mode=Disable;Timeout=30");
        using var caller = new CancellationTokenSource();
        await caller.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ProductionRuntimeDependencyStatus(connections).IsReadyAsync(caller.Token));
    }
}
