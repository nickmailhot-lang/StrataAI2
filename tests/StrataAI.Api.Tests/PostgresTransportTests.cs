using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StrataAI.Infrastructure.Identity;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Infrastructure.Runtime;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed class PostgresTransportTests
{
    private const string Secret = "transport-fixture-secret";

    [Theory]
    [InlineData("database.example.test")]
    [InlineData("postgres")]
    [InlineData("127.0.0.1")]
    public async Task ARCH_12_Postgres_omitted_TLS_requires_verified_server_identity(string host)
    {
        await using var factory = new PostgresConnectionFactory(Connection(host));
        Assert.Equal(SslMode.VerifyFull, Settings(factory).SslMode);
        Assert.Equal(GssEncryptionMode.Disable, Settings(factory).GssEncryptionMode);
    }

    [Theory]
    [InlineData("Require", SslMode.Require)]
    [InlineData("VerifyCA", SslMode.VerifyCA)]
    [InlineData("VerifyFull", SslMode.VerifyFull)]
    public async Task ARCH_12_Postgres_explicit_encrypted_configuration_preserves_credentials_and_options(string mode, SslMode expected)
    {
        var input = new NpgsqlConnectionStringBuilder(Connection("database.example.test"))
        { SslMode = expected, RootCertificate = "/run/secrets/database-ca.pem", ApplicationName = "transport-fixture", MaxPoolSize = 1,
            PersistSecurityInfo = true }; // Fixture-only opt-in to inspect preservation of quoted credentials.
        input.Password = Secret + ";quoted value";
        await using var factory = new PostgresConnectionFactory(input.ConnectionString);
        var actual = Settings(factory);
        Assert.Equal(expected, actual.SslMode);
        Assert.Equal(input.Password, actual.Password);
        Assert.Equal(input.RootCertificate, actual.RootCertificate);
        Assert.Equal(input.ApplicationName, actual.ApplicationName);
        Assert.Equal(1, actual.MaxPoolSize);
        Assert.Equal(mode, expected.ToString());
    }

    [Theory]
    [InlineData("Prefer")]
    [InlineData("Allow")]
    [InlineData("Disable")]
    public void ARCH_12_Postgres_external_plaintext_or_fallback_refuses_before_network_without_credentials(string mode)
    {
        var failure = Assert.Throws<InvalidOperationException>(() => new PostgresConnectionFactory(Connection("database.example.test") + ";SSL Mode=" + mode));
        Assert.DoesNotContain(Secret, failure.ToString());
        Assert.DoesNotContain("database.example.test", failure.ToString());
        Assert.Null(failure.InnerException);
    }

    [Theory]
    [InlineData("postgres")]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("/var/run/postgresql")]
    public async Task ARCH_10_Postgres_local_plaintext_requires_explicit_Disable(string host)
    {
        await using var factory = new PostgresConnectionFactory(Connection(host) + ";SSL Mode=Disable");
        Assert.Equal(SslMode.Disable, Settings(factory).SslMode);
    }

    [Theory]
    [InlineData("postgres.example.test")]
    [InlineData("127.0.0.1,database.example.test")]
    [InlineData("postgres,database.example.test")]
    public void ARCH_12_Postgres_plaintext_local_exception_cannot_cover_external_or_mixed_hosts(string host)
    {
        Assert.Throws<InvalidOperationException>(() => new PostgresConnectionFactory(Connection(host) + ";SSL Mode=Disable"));
    }

    [Fact]
    public void ARCH_12_Postgres_identity_delivery_uses_the_same_external_transport_guard()
    {
        var failure = Assert.Throws<InvalidOperationException>(() => new PostgresIdentityDeliveryStore(Connection("mail-database.example.test") + ";SSL Mode=Disable"));
        Assert.DoesNotContain(Secret, failure.ToString());
    }

    [Theory]
    [InlineData("Prefer")]
    [InlineData("Require")]
    public void ARCH_12_Postgres_explicit_GSS_cannot_replace_the_required_TLS_transport(string mode)
    {
        Assert.Throws<InvalidOperationException>(() => new PostgresConnectionFactory(Connection("database.example.test") + ";GSS Encryption Mode=" + mode));
    }

    [Fact]
    public void ARCH_12_Postgres_malformed_transport_does_not_expose_parser_values()
    {
        var failure = Assert.Throws<InvalidOperationException>(() => new PostgresConnectionFactory(
            Connection("database.example.test") + ";SSL Mode=" + Secret));
        Assert.DoesNotContain(Secret, failure.ToString());
        Assert.Null(failure.InnerException);
    }

    [Theory]
    [InlineData(null, SslMode.VerifyFull)]
    [InlineData("VerifyFull", SslMode.VerifyFull)]
    [InlineData("Require", SslMode.Require)]
    [InlineData("Disable", SslMode.Disable)]
    public async Task ARCH_12_Postgres_structured_runtime_credentials_forward_explicit_transport(string? mode, SslMode expected)
    {
        var values = new Dictionary<string, string?>
        {
            ["STRATAAI_RUNTIME_MODE"] = "production", ["STRATAAI_DATABASE_HOST"] = "postgres",
            ["STRATAAI_DATABASE_NAME"] = "fixture", ["STRATAAI_DATABASE_USERNAME"] = "fixture",
            ["STRATAAI_DATABASE_PASSWORD"] = Secret,
            ["STRATAAI_DATABASE_SSL_MODE"] = mode,
        };
        var services = new ServiceCollection();
        services.AddStrataAiRuntime(new ConfigurationBuilder().AddInMemoryCollection(values).Build(), typeof(PostgresTransportTests).Assembly);
        await using var provider = services.BuildServiceProvider();
        Assert.Equal(expected, Settings(provider.GetRequiredService<PostgresConnectionFactory>()).SslMode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Prefer")]
    [InlineData("unexpected")]
    [InlineData("3")]
    [InlineData("Require;Password=transport-fixture-secret")]
    public void ARCH_12_Postgres_invalid_structured_transport_refuses_without_echoing_configuration(string mode)
    {
        var values = new Dictionary<string, string?>
        {
            ["STRATAAI_RUNTIME_MODE"] = "production", ["STRATAAI_DATABASE_HOST"] = "postgres",
            ["STRATAAI_DATABASE_NAME"] = "fixture", ["STRATAAI_DATABASE_USERNAME"] = "fixture",
            ["STRATAAI_DATABASE_PASSWORD"] = Secret, ["STRATAAI_DATABASE_SSL_MODE"] = mode,
        };
        var failure = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddStrataAiRuntime(
            new ConfigurationBuilder().AddInMemoryCollection(values).Build(), typeof(PostgresTransportTests).Assembly));
        Assert.DoesNotContain(Secret, failure.ToString());
        Assert.Null(failure.InnerException);
    }

    [Fact]
    public async Task ARCH_12_Postgres_missing_TLS_never_falls_back_after_server_refuses_encryption()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var observed = Task.Run(async () =>
        {
            using var peer = await listener.AcceptTcpClientAsync(deadline.Token);
            var stream = peer.GetStream();
            var request = new byte[8];
            await stream.ReadExactlyAsync(request, deadline.Token);
            Assert.Equal(new byte[] { 0, 0, 0, 8, 4, 210, 22, 47 }, request);
            await stream.WriteAsync(new byte[] { (byte)'N' }, deadline.Token);
            return await stream.ReadAsync(new byte[1], deadline.Token);
        }, deadline.Token);
        await using var factory = new PostgresConnectionFactory(Connection("127.0.0.1") + $";Port={port};Timeout=3;GSS Encryption Mode=Disable");
        await Assert.ThrowsAnyAsync<NpgsqlException>(async () => await factory.OpenConnectionAsync(deadline.Token));
        Assert.Equal(0, await observed);
    }

    private static string Connection(string host) => new NpgsqlConnectionStringBuilder
    { Host = host, Database = "fixture", Username = "fixture", Password = Secret }.ConnectionString;

    private static NpgsqlConnectionStringBuilder Settings(PostgresConnectionFactory factory)
    {
        // Inspect the actual factory-owned source, without starting an ambient DB.
        var field = typeof(PostgresConnectionFactory).GetField("_dataSource", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return new NpgsqlConnectionStringBuilder(((NpgsqlDataSource)field.GetValue(factory)!).ConnectionString);
    }
}
