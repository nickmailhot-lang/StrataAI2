using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Npgsql;
using StrataAI.Application.Runtime;
using StrataAI.Infrastructure.Persistence;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed class PostgresAdmissionRecoveryTests
{
    [Theory]
    [InlineData("transient-once", 2, 3, "success")]
    [InlineData("transient-always", 2, 2, "provider")]
    [InlineData("role-refusal", 1, 1, "role")]
    [InlineData("schema-refusal", 1, 2, "schema")]
    [InlineData("permission-refusal", 1, 1, "role")]
    [InlineData("role-after-reconnect", 2, 2, "role")]
    [InlineData("schema-after-reconnect", 2, 3, "schema")]
    public async Task ARCH_08_Connection_admission_recovers_once_and_rechecks_every_security_guard(
        string fault, int expectedConnections, int expectedQueries, string outcome)
    {
        foreach (var readiness in new[] { false, true })
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            var connections = 0; var queries = 0;
            var server = ServeAsync(listener, fault, () => Interlocked.Increment(ref connections),
                () => Interlocked.Increment(ref queries), deadline.Token);
            try
            {
                await using var factory = Factory(listener);
                async Task Open()
                {
                    await using var connection = readiness
                        ? await factory.OpenReadinessConnectionAsync(deadline.Token)
                        : await factory.OpenConnectionAsync(deadline.Token);
                }
                switch (outcome)
                {
                    case "success": await Open(); break;
                    case "provider": await Assert.ThrowsAsync<PostgresException>(Open); break;
                    case "role": await Assert.ThrowsAsync<RuntimeDatabaseRoleException>(Open); break;
                    case "schema": await Assert.ThrowsAsync<RuntimeDatabaseSchemaException>(Open); break;
                    default: throw new InvalidOperationException("Unknown fixture outcome.");
                }
                Assert.Equal(expectedConnections, connections);
                Assert.Equal(expectedQueries, queries);
            }
            finally
            {
                await deadline.CancelAsync();
                try { await server; }
                catch (OperationCanceledException) when (deadline.IsCancellationRequested) { }
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ARCH_08_Cancelled_admission_does_not_open_or_retry_a_connection(bool readiness)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        using var stop = new CancellationTokenSource(); stop.Cancel();
        await using var factory = Factory(listener);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await using var connection = readiness
                ? await factory.OpenReadinessConnectionAsync(stop.Token)
                : await factory.OpenConnectionAsync(stop.Token);
        });
        Assert.False(listener.Pending());
    }

    private static PostgresConnectionFactory Factory(TcpListener listener)
    {
        var settings = new NpgsqlConnectionStringBuilder
        {
            Host = "127.0.0.1", Port = ((IPEndPoint)listener.LocalEndpoint).Port,
            Username = "admission_fixture", Database = "admission_fixture", SslMode = SslMode.Disable,
            Pooling = false, Timeout = 2, CommandTimeout = 2,
        };
        settings["Server Compatibility Mode"] = "NoTypeLoading";
        return new(settings.ConnectionString);
    }

    // A bounded protocol peer drives real Npgsql and the real factory. It only
    // answers the two read-only admission guards; database/RLS recovery has a
    // separate real PostgreSQL acceptance scenario.
    private static async Task ServeAsync(TcpListener listener, string fault, Func<int> accepted, Action query, CancellationToken ct)
    {
        while (true)
        {
            using var client = await listener.AcceptTcpClientAsync(ct);
            var number = accepted(); await using var stream = client.GetStream();
            Assert.Equal(196608, BinaryPrimitives.ReadInt32BigEndian(await PacketAsync(stream, ct)));
            await SendAsync(stream, 'R', Int32(0), ct);
            foreach (var (name, value) in new[] { ("server_version", "17.4"), ("server_encoding", "UTF8"),
                ("client_encoding", "UTF8"), ("integer_datetimes", "on"), ("standard_conforming_strings", "on"), ("TimeZone", "UTC") })
                await SendAsync(stream, 'S', Encoding.UTF8.GetBytes(name + '\0' + value + '\0'), ct);
            await SendAsync(stream, 'K', [.. Int32(42), .. Int32(7)], ct);
            await SendAsync(stream, 'Z', [(byte)'I'], ct);
            var failed = false; var allowed = true;
            while (true)
            {
                var type = new byte[1]; if (await stream.ReadAsync(type, ct) == 0) break;
                var payload = await PacketAsync(stream, ct);
                if ((char)type[0] == 'X') break;
                if ((char)type[0] == 'S') { await SendAsync(stream, 'Z', [(byte)'I'], ct); failed = false; continue; }
                if (failed) continue;
                switch ((char)type[0])
                {
                    case 'P':
                        query(); var sql = Encoding.UTF8.GetString(payload).TrimStart('\0');
                        Assert.StartsWith("SELECT", sql.TrimStart());
                        var role = sql.Contains("runtime_database_role_is_safe", StringComparison.Ordinal);
                        var transient = fault == "transient-always" || number == 1 && fault is "transient-once" or "role-after-reconnect" or "schema-after-reconnect";
                        if (transient || fault == "permission-refusal")
                        {
                            failed = true;
                            var code = transient ? "57P01" : "42501";
                            await SendAsync(stream, 'E', Encoding.UTF8.GetBytes("SERROR\0C" + code + "\0Mprivate-wire-fixture\0\0"), ct);
                            break;
                        }
                        allowed = !(role && fault is "role-refusal" or "role-after-reconnect")
                            && !(!role && fault is "schema-refusal" or "schema-after-reconnect");
                        await SendAsync(stream, '1', [], ct); break;
                    case 'B': await SendAsync(stream, '2', [], ct); break;
                    case 'D':
                        await SendAsync(stream, 'T', [.. Int16(1), .. Encoding.UTF8.GetBytes("allowed\0"), .. Int32(0), .. Int16(0),
                            .. Int32(16), .. Int16(1), .. Int32(-1), .. Int16(1)], ct); break;
                    case 'E':
                        await SendAsync(stream, 'D', [.. Int16(1), .. Int32(1), allowed ? (byte)1 : (byte)0], ct);
                        await SendAsync(stream, 'C', Encoding.UTF8.GetBytes("SELECT 1\0"), ct); break;
                    case 'H': break;
                    default: throw new InvalidOperationException("Unexpected admission fixture protocol.");
                }
            }
        }
    }

    private static async Task<byte[]> PacketAsync(Stream stream, CancellationToken ct)
    {
        var length = new byte[4]; await stream.ReadExactlyAsync(length, ct);
        var size = BinaryPrimitives.ReadInt32BigEndian(length); Assert.InRange(size, 4, 65536);
        var payload = new byte[size - 4]; await stream.ReadExactlyAsync(payload, ct); return payload;
    }
    private static async Task SendAsync(Stream stream, char type, byte[] payload, CancellationToken ct)
    {
        await stream.WriteAsync(new[] { (byte)type }, ct); await stream.WriteAsync(Int32(payload.Length + 4), ct); await stream.WriteAsync(payload, ct);
    }
    private static byte[] Int32(int value) { var bytes = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(bytes, value); return bytes; }
    private static byte[] Int16(short value) { var bytes = new byte[2]; BinaryPrimitives.WriteInt16BigEndian(bytes, value); return bytes; }
}
