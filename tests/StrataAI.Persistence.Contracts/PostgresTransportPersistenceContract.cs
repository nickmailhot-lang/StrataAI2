using System.Security.Authentication;
using Npgsql;
using StrataAI.Infrastructure.Persistence;

internal static class PostgresTransportPersistenceContract
{
    // ARCH-12-FR-005: real TLS negotiation through the application's factory,
    // restricted login and current schema, rather than inspecting a builder.
    public static async Task RunAsync(CancellationToken cancellationToken)
    {
        var connectionString = Environment.GetEnvironmentVariable("STRATAAI_CONTRACT_API_CONNECTION")
            ?? throw new InvalidOperationException("Contract restricted API connection is required.");
        var root = Environment.GetEnvironmentVariable("STRATAAI_CONTRACT_TLS_ROOT")
            ?? throw new InvalidOperationException("Contract TLS root is required.");
        var untrustedRoot = Environment.GetEnvironmentVariable("STRATAAI_CONTRACT_TLS_UNTRUSTED_ROOT")
            ?? throw new InvalidOperationException("Contract unrelated TLS root is required.");
        var settings = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Host = "localhost", RootCertificate = root, Pooling = false, Timeout = 5,
        };
        settings.Remove("SSL Mode"); // Exercise the application default, not an explicit override.
        await using (var factory = new PostgresConnectionFactory(settings.ConnectionString))
        await using (var connection = await factory.OpenConnectionAsync(cancellationToken))
        await using (var command = new NpgsqlCommand(
            "SELECT ssl AND version IN ('TLSv1.2', 'TLSv1.3') FROM pg_stat_ssl WHERE pid = pg_backend_pid()", connection))
        {
            if (await command.ExecuteScalarAsync(cancellationToken) is not true)
                throw new InvalidOperationException("Default PostgreSQL transport did not negotiate TLS.");
        }
        Console.WriteLine("Default PostgreSQL TLS accepts the trusted matching certificate under the restricted runtime login.");

        var mismatch = new NpgsqlConnectionStringBuilder(settings.ConnectionString) { Host = "127.0.0.1" };
        await ExpectCertificateRefusalAsync(mismatch.ConnectionString, "hostname mismatch", cancellationToken);
        var untrusted = new NpgsqlConnectionStringBuilder(settings.ConnectionString) { RootCertificate = untrustedRoot };
        await ExpectCertificateRefusalAsync(untrusted.ConnectionString, "untrusted issuer", cancellationToken);
        Console.WriteLine("PostgreSQL TLS contract passed: encrypted success, hostname mismatch and untrusted issuer (3 cases).");
    }

    private static async Task ExpectCertificateRefusalAsync(string connectionString, string caseName, CancellationToken cancellationToken)
    {
        await using var factory = new PostgresConnectionFactory(connectionString);
        try
        {
            await using var connection = await factory.OpenConnectionAsync(cancellationToken);
        }
        catch (NpgsqlException error) when (error.InnerException is AuthenticationException)
        {
            Console.WriteLine($"Default PostgreSQL TLS rejects {caseName} during certificate authentication.");
            return;
        }
        // Timeouts, failed SQL/login, unavailable servers and unrelated failures
        // must not be mistaken for certificate verification.
        throw new InvalidOperationException($"Default PostgreSQL TLS accepted {caseName}.");
    }
}
