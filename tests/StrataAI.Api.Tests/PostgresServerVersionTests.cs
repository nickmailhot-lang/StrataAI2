using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Npgsql;
using StrataAI.Application.Runtime;
using StrataAI.Infrastructure.Persistence;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed class PostgresServerVersionTests
{
    [Theory]
    [InlineData("15.8", false, false)]
    [InlineData("16.4", false, false)]
    [InlineData("17.4", true, false)]
    [InlineData("18.1", true, false)]
    [InlineData("15.8", false, true)]
    [InlineData("16.4", false, true)]
    [InlineData("17.4", true, true)]
    [InlineData("18.1", true, true)]
    public async Task ARCH_04_FR_001_Runtime_connections_refuse_older_servers_before_security_or_schema_queries(
        string version, bool supported, bool readiness)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var queries = 0;
        var server = ServeAsync(listener, version, () => Interlocked.Increment(ref queries), deadline.Token);
        try
        {
            var settings = new NpgsqlConnectionStringBuilder
            {
                Host = "127.0.0.1", Port = ((IPEndPoint)listener.LocalEndpoint).Port,
                Username = "version_fixture", Database = "version_fixture", SslMode = SslMode.Disable,
                Pooling = false, Timeout = 2, CommandTimeout = 2,
            };
            // This protocol peer supplies built-in boolean results, not catalog
            // type loading. Production retains its normal server compatibility.
            settings["Server Compatibility Mode"] = "NoTypeLoading";
            await using var factory = new PostgresConnectionFactory(settings.ConnectionString);
            async Task Open()
            {
                await using var connection = readiness
                    ? await factory.OpenReadinessConnectionAsync(deadline.Token)
                    : await factory.OpenConnectionAsync(deadline.Token);
                Assert.Equal(Version.Parse(version), connection.PostgreSqlVersion);
            }
            if (supported)
            {
                await Open();
                Assert.Equal(2, queries); // Existing role and complete schema checks still execute.
            }
            else
            {
                await Assert.ThrowsAsync<RuntimeDatabaseSchemaException>(Open);
                Assert.Equal(0, queries);
            }
        }
        finally
        {
            await deadline.CancelAsync();
            listener.Stop();
            try { await server; }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested) { }
        }
    }

    // Minimal trusted loopback protocol peer, not a database/SQL substitute.
    // Real Npgsql parses server_version and runs the actual connection factory.
    // SQL answers only establish that supported versions still reach both guards.
    private static async Task ServeAsync(TcpListener listener, string version, Action query, CancellationToken ct)
    {
        using var client = await listener.AcceptTcpClientAsync(ct);
        await using var stream = client.GetStream();
        var startup = await PacketAsync(stream, ct);
        Assert.Equal(196608, BinaryPrimitives.ReadInt32BigEndian(startup));
        await SendAsync(stream, 'R', Int32(0), ct);
        foreach (var (name, value) in new[] { ("server_version", version), ("server_encoding", "UTF8"),
                     ("client_encoding", "UTF8"), ("integer_datetimes", "on"),
                     ("standard_conforming_strings", "on"), ("TimeZone", "UTC") })
            await SendAsync(stream, 'S', Encoding.UTF8.GetBytes(name + '\0' + value + '\0'), ct);
        await SendAsync(stream, 'K', [.. Int32(42), .. Int32(7)], ct);
        await SendAsync(stream, 'Z', [(byte)'I'], ct);
        while (true)
        {
            var type = new byte[1];
            if (await stream.ReadAsync(type, ct) == 0) return;
            await PacketAsync(stream, ct);
            switch ((char)type[0])
            {
                case 'P': query(); await SendAsync(stream, '1', [], ct); break;
                case 'B': await SendAsync(stream, '2', [], ct); break;
                case 'D': await DescriptionAsync(stream, ct); break;
                case 'E': await ResultAsync(stream, ct); break;
                case 'S': await SendAsync(stream, 'Z', [(byte)'I'], ct); break;
                case 'H': break;
                case 'X': return;
                case 'Q':
                    query(); await DescriptionAsync(stream, ct); await ResultAsync(stream, ct);
                    await SendAsync(stream, 'Z', [(byte)'I'], ct); break;
                default: throw new InvalidOperationException("Unexpected PostgreSQL fixture protocol.");
            }
        }
    }

    private static Task DescriptionAsync(Stream stream, CancellationToken ct) => SendAsync(stream, 'T',
        [.. Int16(1), .. Encoding.UTF8.GetBytes("allowed\0"), .. Int32(0), .. Int16(0),
         .. Int32(16), .. Int16(1), .. Int32(-1), .. Int16(1)], ct);

    private static async Task ResultAsync(Stream stream, CancellationToken ct)
    {
        await SendAsync(stream, 'D', [.. Int16(1), .. Int32(1), 1], ct);
        await SendAsync(stream, 'C', Encoding.UTF8.GetBytes("SELECT 1\0"), ct);
    }

    private static async Task<byte[]> PacketAsync(Stream stream, CancellationToken ct)
    {
        var length = new byte[4];
        await stream.ReadExactlyAsync(length, ct);
        var size = BinaryPrimitives.ReadInt32BigEndian(length);
        Assert.InRange(size, 4, 65536);
        var payload = new byte[size - 4];
        await stream.ReadExactlyAsync(payload, ct);
        return payload;
    }

    private static async Task SendAsync(Stream stream, char type, byte[] payload, CancellationToken ct)
    {
        await stream.WriteAsync(new byte[] { (byte)type }, ct);
        await stream.WriteAsync(Int32(payload.Length + 4), ct);
        await stream.WriteAsync(payload, ct);
    }

    private static byte[] Int32(int value) { var result = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(result, value); return result; }
    private static byte[] Int16(short value) { var result = new byte[2]; BinaryPrimitives.WriteInt16BigEndian(result, value); return result; }
}
