using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Runtime;
using StrataAI.Infrastructure.Identity;
using StrataAI.Infrastructure.Runtime;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed class ProductionOriginTests
{
    private static readonly string[] Keys = ["STRATAAI_PUBLIC_ORIGIN", "STRATAAI_REALTIME_PUBLIC_ORIGIN"];

    public static IEnumerable<object?[]> InvalidOrigins()
    {
        string[] values = ["http://external.invalid", "http://localhost.external.invalid", "http://192.168.1.10",
            "http://0.0.0.0", "http://[::]", "ftp://localhost", "//external.invalid", "invalid-origin-secret",
            "https://user:invalid-origin-secret@external.invalid", "https://external.invalid/path",
            "https://external.invalid/?token=invalid-origin-secret", "https://external.invalid/#invalid-origin-secret",
            "http://localhost/path", "http://localhost/?token=invalid-origin-secret"];
        foreach (var key in Keys)
            foreach (var value in values) yield return [key, value];
    }

    public static IEnumerable<object?[]> ValidOrigins()
    {
        string?[] values = [null, "", " ", "https://external.invalid", "https://external.invalid:8443/",
            "http://localhost:8088", "http://127.0.0.1:8088/", "http://[::1]:8088/"];
        foreach (var key in Keys)
            foreach (var value in values) yield return [key, value];
    }

    [Theory]
    [MemberData(nameof(InvalidOrigins))]
    public void ARCH_12_Production_rejects_unsafe_origins_even_with_mail_disabled(string key, string value)
    {
        var configuration = Configuration("production", key, value);
        var failure = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddStrataAiRuntime(configuration, typeof(ProductionOriginTests).Assembly));
        Assert.Contains(key, failure.Message);
        Assert.Contains("HTTPS", failure.Message);
        Assert.DoesNotContain(value, failure.ToString());
        Assert.DoesNotContain("invalid-origin-secret", failure.ToString());
        Assert.Null(failure.InnerException);
    }

    [Theory]
    [MemberData(nameof(ValidOrigins))]
    public async Task ARCH_12_Production_accepts_HTTPS_or_documented_loopback_and_optional_absence(string key, string? value)
    {
        var services = new ServiceCollection();
        var runtime = services.AddStrataAiRuntime(Configuration("production", key, value), typeof(ProductionOriginTests).Assembly);
        await using var provider = services.BuildServiceProvider();
        Assert.Equal(RuntimeMode.Production, runtime.Mode);
        Assert.Null(provider.GetService<IDemoDataStore>());
    }

    [Theory]
    [InlineData("STRATAAI_PUBLIC_ORIGIN")]
    [InlineData("STRATAAI_REALTIME_PUBLIC_ORIGIN")]
    public void ARCH_12_HTTPS_override_cannot_hide_an_independently_insecure_origin(string unsafeKey)
    {
        var values = Values("production");
        foreach (var key in Keys) values[key] = key == unsafeKey ? "http://external.invalid" : "https://safe.invalid";
        var failure = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddStrataAiRuntime(
            new ConfigurationBuilder().AddInMemoryCollection(values).Build(), typeof(ProductionOriginTests).Assembly));
        Assert.Contains(unsafeKey, failure.Message);
    }

    [Theory]
    [InlineData("STRATAAI_PUBLIC_ORIGIN")]
    [InlineData("STRATAAI_REALTIME_PUBLIC_ORIGIN")]
    public async Task ARCH_05_Demo_remains_independent_of_Production_origin_policy(string key)
    {
        var values = Values("demo");
        values[key] = "http://external.invalid";
        values.Remove("ConnectionStrings:Postgres");
        var services = new ServiceCollection();
        var runtime = services.AddStrataAiRuntime(new ConfigurationBuilder().AddInMemoryCollection(values).Build(), typeof(ProductionOriginTests).Assembly);
        await using var provider = services.BuildServiceProvider();
        Assert.Equal(RuntimeMode.Demo, runtime.Mode);
        Assert.NotNull(provider.GetService<IDemoDataStore>());
    }

    [Fact]
    public void ARCH_12_Enabled_identity_mail_keeps_its_stricter_HTTPS_rule_for_local_links()
    {
        var values = Values("production");
        values["STRATAAI_PUBLIC_ORIGIN"] = "http://localhost:8088";
        values["STRATAAI_IDENTITY_EMAIL_ENABLED"] = "true";
        values["STRATAAI_IDENTITY_EMAIL_FROM"] = "fixture@example.test";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        var runtime = services.AddStrataAiRuntime(configuration, typeof(ProductionOriginTests).Assembly);
        var failure = Assert.Throws<InvalidOperationException>(() => services.AddIdentityDeliveryTokens(configuration, runtime));
        Assert.Contains("Identity delivery requires an HTTPS public origin", failure.Message);
    }

    private static IConfiguration Configuration(string mode, string key, string? value)
    {
        var values = Values(mode);
        values[key] = value;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static Dictionary<string, string?> Values(string mode) => new()
    {
        ["STRATAAI_RUNTIME_MODE"] = mode,
        ["STRATAAI_IDENTITY_EMAIL_ENABLED"] = "false",
        ["ConnectionStrings:Postgres"] = "Host=127.0.0.1;Database=fixture;Username=fixture;Password=fixture;SSL Mode=Disable",
    };
}
