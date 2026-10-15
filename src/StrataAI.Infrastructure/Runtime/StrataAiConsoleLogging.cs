using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace StrataAI.Infrastructure.Runtime;

public sealed record StrataAiConsoleMetadata(string Component, EmbeddedBuildIdentity Build);

public static class StrataAiConsoleLogging
{
    public static ILoggingBuilder AddStrataAiConsoleLogging(this ILoggingBuilder logging, Assembly hostAssembly, bool worker = false)
    {
        // Build identity is immutable host metadata, never a formatter setting
        // supplied through environment configuration or a caller's log scope.
        logging.Services.AddSingleton(new StrataAiConsoleMetadata(worker ? "strataai-worker" : "strataai-api", BuildIdentityReader.Read(hostAssembly)));
        logging.AddConsoleFormatter<StrataAiConsoleFormatter, ConsoleFormatterOptions>();
        logging.AddConsole(options => options.FormatterName = StrataAiConsoleFormatter.FormatterName);
        // Request-start messages may contain transport tokens in query strings.
        logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
        return logging;
    }
}

// ARCH-08-FR-003: one JSON record with stable service/build metadata. Application
// messages must still use the approved safe templates; this is not a body scrubber.
public sealed class StrataAiConsoleFormatter(StrataAiConsoleMetadata metadata) : ConsoleFormatter(FormatterName)
{
    public const string FormatterName = "strataai-json";

    public override void Write<TState>(in LogEntry<TState> entry, IExternalScopeProvider? scopes, TextWriter writer)
    {
        string? correlation = null;
        Guid? organization = null;
        void ReadIdentifiers(object? state)
        {
            try
            {
                if (state is not IEnumerable<KeyValuePair<string, object?>> fields) return;
                foreach (var field in fields)
                {
                    if (field.Key == "CorrelationId")
                    {
                        if (field.Value is Guid id && id != Guid.Empty) correlation = id.ToString("D");
                        else if (field.Value is string value && value.Length is > 0 and <= 64
                            && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')) correlation = value;
                    }
                    else if ((field.Key is "OrganizationId" or "TenantId") && field.Value is Guid id && id != Guid.Empty)
                        organization = id;
                }
            }
            catch { /* A diagnostic scope cannot replace a product outcome. */ }
        }
        try { scopes?.ForEachScope((scope, _) => ReadIdentifiers(scope), 0); }
        catch { /* Preserve fixed metadata even when scope enumeration fails. */ }
        ReadIdentifiers(entry.State);
        string message;
        // Do not pass an exception to a formatter that might render its private
        // provider message, stack, token, query or payload. Keep only its presence.
        try { message = entry.Formatter(entry.State, null) ?? "Log message unavailable."; }
        catch { message = "Log message unavailable."; }
        try
        {
            writer.WriteLine(JsonSerializer.Serialize(new
            {
                Timestamp = DateTimeOffset.UtcNow,
                LogLevel = entry.LogLevel.ToString(),
                metadata.Component,
                BuildRevision = metadata.Build.Revision,
                BuildVersion = metadata.Build.Version,
                entry.Category,
                EventId = entry.EventId.Id,
                EventName = entry.EventId.Name,
                CorrelationId = correlation,
                OrganizationId = organization,
                Message = message,
                ExceptionPresent = entry.Exception is not null,
            }));
        }
        catch { /* Console output is optional; never recursively log its failure. */ }
    }
}
