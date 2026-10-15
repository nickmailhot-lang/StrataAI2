using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using StrataAI.Infrastructure.Runtime;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed class ConsoleLoggingTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ARCH_08_Console_has_fixed_service_build_and_only_approved_scope_identifiers(bool worker)
    {
        var organization = Guid.NewGuid(); var scopes = new LoggerExternalScopeProvider();
        using var scope = scopes.Push(new Dictionary<string, object?>
        {
            ["CorrelationId"] = "request-reference_01", ["OrganizationId"] = organization,
            ["Password"] = "private-password", ["Payload"] = new ExplosiveObject(),
            ["Component"] = "private-component", ["BuildRevision"] = "private-revision",
        });
        using var unrelated = scopes.Push("private-raw-scope");
        using var json = Write(worker, scopes, new Dictionary<string, object?> { ["Content"] = "private-content" },
            new PrivateException(), (_, error) => error is null ? "Operation observed." : error.Message);
        var record = json.RootElement; var build = BuildIdentityReader.Read(typeof(Program).Assembly);
        Assert.Equal(12, record.EnumerateObject().Count());
        Assert.Equal(worker ? "strataai-worker" : "strataai-api", record.GetProperty("Component").GetString());
        Assert.Equal(build.Revision, record.GetProperty("BuildRevision").GetString());
        Assert.Equal(build.Version, record.GetProperty("BuildVersion").GetString());
        Assert.Equal("Warning", record.GetProperty("LogLevel").GetString());
        Assert.Equal("Fixture.Operations", record.GetProperty("Category").GetString());
        Assert.Equal(9, record.GetProperty("EventId").GetInt32()); Assert.Equal("Observed", record.GetProperty("EventName").GetString());
        Assert.Equal("request-reference_01", record.GetProperty("CorrelationId").GetString());
        Assert.Equal(organization, record.GetProperty("OrganizationId").GetGuid());
        Assert.Equal("Operation observed.", record.GetProperty("Message").GetString());
        Assert.True(record.GetProperty("ExceptionPresent").GetBoolean());
        Assert.Equal(TimeSpan.Zero, record.GetProperty("Timestamp").GetDateTimeOffset().Offset);
        Assert.DoesNotContain("private", record.GetRawText());
    }

    [Theory]
    [InlineData("")] [InlineData("has spaces")] [InlineData("secret@example.test")]
    [InlineData("line\nbreak")] [InlineData("é")] [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void ARCH_08_Invalid_correlation_never_enters_the_record(string value)
    {
        using var json = Write(false, null, new Dictionary<string, object?> { ["CorrelationId"] = value });
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("CorrelationId").ValueKind);
    }

    [Fact]
    public void ARCH_08_Entry_identifiers_override_outer_scope_and_keep_guid_types()
    {
        var scopes = new LoggerExternalScopeProvider(); var inner = Guid.NewGuid(); var organization = Guid.NewGuid();
        using var scope = scopes.Push(new Dictionary<string, object?> { ["CorrelationId"] = "outer-reference", ["OrganizationId"] = Guid.NewGuid() });
        using var json = Write(false, scopes, new Dictionary<string, object?> { ["CorrelationId"] = inner, ["TenantId"] = organization });
        Assert.Equal(inner.ToString("D"), json.RootElement.GetProperty("CorrelationId").GetString());
        Assert.Equal(organization, json.RootElement.GetProperty("OrganizationId").GetGuid());
    }

    [Fact]
    public void ARCH_08_Untrusted_tenant_strings_and_other_scope_objects_are_not_rendered()
    {
        using var json = Write(false, null, new Dictionary<string, object?> { ["TenantId"] = "private-tenant", ["Unexpected"] = new ExplosiveObject() });
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("OrganizationId").ValueKind);
        Assert.DoesNotContain("private", json.RootElement.GetRawText());
        using var other = Write(false, null, new ExplosiveObject());
        Assert.False(other.RootElement.GetProperty("ExceptionPresent").GetBoolean());
    }

    [Fact]
    public void ARCH_08_Message_newlines_quotes_and_unicode_stay_in_one_json_record()
    {
        using var output = new StringWriter(); var value = "Observed\n\"queued\" 🙂";
        var entry = Entry(value, null, (message, _) => message);
        Formatter(false).Write(entry, null, output);
        using var lines = new StringReader(output.ToString()); var first = lines.ReadLine();
        Assert.NotNull(first); Assert.Null(lines.ReadLine());
        using var json = JsonDocument.Parse(first); Assert.Equal(value, json.RootElement.GetProperty("Message").GetString());
    }

    [Fact]
    public void ARCH_08_Throwing_message_formatter_or_scope_keeps_fixed_non_sensitive_fallback()
    {
        using var json = Write(false, new BrokenScopes(), "safe", new PrivateException(), (_, _) => throw new InvalidOperationException("private-render-failure"));
        Assert.Equal("Log message unavailable.", json.RootElement.GetProperty("Message").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("CorrelationId").ValueKind);
        Assert.DoesNotContain("private", json.RootElement.GetRawText());
    }

    [Fact]
    public void ARCH_08_Console_transport_failure_never_escapes_or_recursively_logs()
    {
        using var output = new BrokenWriter(); var entry = Entry("Observed", null, (message, _) => message);
        Assert.Null(Record.Exception(() => Formatter(false).Write(entry, null, output)));
        Assert.Equal(1, output.Calls);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ARCH_08_Default_console_registration_uses_embedded_metadata_and_preserves_token_log_filter(bool worker)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Console:FormatterName"] = "simple", ["Console:FormatterOptions:BuildRevision"] = "private-runtime-spoof",
            ["Console:FormatterOptions:Component"] = "private-component",
        }).Build();
        var services = new ServiceCollection(); services.AddLogging(logging => logging.AddConfiguration(config).AddStrataAiConsoleLogging(typeof(Program).Assembly, worker));
        using var provider = services.BuildServiceProvider();
        Assert.Equal(StrataAiConsoleFormatter.FormatterName, provider.GetRequiredService<IOptions<ConsoleLoggerOptions>>().Value.FormatterName);
        var metadata = provider.GetRequiredService<StrataAiConsoleMetadata>();
        Assert.Equal(worker ? "strataai-worker" : "strataai-api", metadata.Component);
        Assert.Equal(BuildIdentityReader.Read(typeof(Program).Assembly), metadata.Build);
        Assert.Single(provider.GetServices<ConsoleFormatter>(), f => f.Name == StrataAiConsoleFormatter.FormatterName);
        Assert.Contains(provider.GetRequiredService<IOptions<LoggerFilterOptions>>().Value.Rules,
            r => r.CategoryName == "Microsoft.AspNetCore.Hosting.Diagnostics" && r.LogLevel == LogLevel.Warning);
    }

    private static StrataAiConsoleFormatter Formatter(bool worker) => new(new(worker ? "strataai-worker" : "strataai-api", BuildIdentityReader.Read(typeof(Program).Assembly)));
    private static LogEntry<T> Entry<T>(T state, Exception? error, Func<T, Exception?, string> formatter) =>
        new(LogLevel.Warning, "Fixture.Operations", new EventId(9, "Observed"), state, error, formatter);
    private static JsonDocument Write<T>(bool worker, IExternalScopeProvider? scopes, T state, Exception? error = null, Func<T, Exception?, string>? format = null)
    {
        using var output = new StringWriter(); var entry = Entry(state, error, format ?? ((_, _) => "Observed"));
        Formatter(worker).Write(entry, scopes, output); return JsonDocument.Parse(output.ToString());
    }
    private sealed class PrivateException : Exception
    {
        public override string Message => throw new InvalidOperationException("private-exception-message");
        public override string ToString() => throw new InvalidOperationException("private-exception-stack");
    }
    private sealed class ExplosiveObject { public override string ToString() => throw new InvalidOperationException("private-object"); }
    private sealed class BrokenWriter : StringWriter
    {
        public int Calls { get; private set; }
        public override void WriteLine(string? value) { Calls++; throw new IOException("private-console-failure"); }
    }
    private sealed class BrokenScopes : IExternalScopeProvider
    {
        public void ForEachScope<TState>(Action<object?, TState> callback, TState state) => throw new InvalidOperationException("private-scope-failure");
        public IDisposable Push(object? state) => throw new NotSupportedException();
    }
}
