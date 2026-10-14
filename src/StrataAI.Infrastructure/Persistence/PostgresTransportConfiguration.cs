using System.Net;
using Npgsql;

namespace StrataAI.Infrastructure.Persistence;

internal static class PostgresTransportConfiguration
{
    internal static string Normalize(string connectionString)
    {
        NpgsqlConnectionStringBuilder settings;
        try { settings = new NpgsqlConnectionStringBuilder(connectionString); }
        catch (ArgumentException)
        {
            // Parser errors can contain supplied values. Never retain their
            // messages or inner exceptions in host startup diagnostics.
            throw new InvalidOperationException("PostgreSQL connection configuration is invalid.");
        }

        if (!settings.ShouldSerialize("SSL Mode")) settings.SslMode = SslMode.VerifyFull;
        if (settings.SslMode == SslMode.Disable)
        {
            if (string.IsNullOrWhiteSpace(settings.Host)
                || !settings.Host.Split(',', StringSplitOptions.TrimEntries).All(IsLocalEndpoint))
                throw new InvalidOperationException("PostgreSQL SSL Mode=Disable requires the private local Compose service, a loopback endpoint or a Unix socket.");
        }
        else if (settings.SslMode is not (SslMode.Require or SslMode.VerifyCA or SslMode.VerifyFull))
            throw new InvalidOperationException("PostgreSQL SSL Mode must require encryption; Allow and Prefer are not supported.");

        // Npgsql 10 otherwise prefers GSS encryption over TLS. This transport
        // contract requires TLS rather than selecting an ambient Kerberos path.
        if (settings.ShouldSerialize("GSS Encryption Mode") && settings.GssEncryptionMode != GssEncryptionMode.Disable)
            throw new InvalidOperationException("PostgreSQL GSS Encryption Mode must be Disable for the configured TLS transport.");
        settings.GssEncryptionMode = GssEncryptionMode.Disable;
        return settings.ConnectionString;
    }

    internal static SslMode ParseStructuredMode(string value) => value.Trim().ToLowerInvariant() switch
    {
        "require" => SslMode.Require,
        "verifyca" => SslMode.VerifyCA,
        "verifyfull" => SslMode.VerifyFull,
        "disable" => SslMode.Disable,
        _ => throw new InvalidOperationException("STRATAAI_DATABASE_SSL_MODE must be Require, VerifyCA, VerifyFull or explicit local Disable."),
    };

    private static bool IsLocalEndpoint(string host) => host.Equals("postgres", StringComparison.OrdinalIgnoreCase)
        || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || host.StartsWith('/')
        || IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);
}
