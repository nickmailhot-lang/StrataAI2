using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Identity;
using StrataAI.Application.Runtime;
using StrataAI.Infrastructure.Identity;
using Xunit;

namespace StrataAI.Domain.Tests;

// PRD-02 / PRD-24 / ARCH-12: supplied security policy is authoritative.
public sealed class IdentityPolicyConfigurationTests
{
    [Theory]
    [InlineData("STRATAAI_AUTH_ALLOW_SELF_REGISTRATION", "invalid-policy-value")]
    [InlineData("STRATAAI_AUTH_REQUIRE_VERIFIED_EMAIL", "invalid-policy-value")]
    [InlineData("STRATAAI_AUTH_REQUIRE_VERIFIED_EMAIL", "")]
    [InlineData("STRATAAI_AUTH_MIN_PASSWORD_LENGTH", "invalid-policy-value")]
    [InlineData("STRATAAI_AUTH_MIN_PASSWORD_LENGTH", "7")]
    [InlineData("STRATAAI_AUTH_MIN_PASSWORD_LENGTH", "129")]
    [InlineData("STRATAAI_AUTH_SESSION_HOURS", "invalid-policy-value")]
    [InlineData("STRATAAI_AUTH_SESSION_HOURS", "0")]
    [InlineData("STRATAAI_AUTH_SESSION_HOURS", "721")]
    [InlineData("STRATAAI_AUTH_SECURITY_TOKEN_MINUTES", "invalid-policy-value")]
    [InlineData("STRATAAI_AUTH_SECURITY_TOKEN_MINUTES", "4")]
    [InlineData("STRATAAI_AUTH_SECURITY_TOKEN_MINUTES", "1441")]
    [InlineData("STRATAAI_AUTH_SECURITY_TOKEN_MINUTES", "")]
    public void ARCH_12_PRD_02_supplied_invalid_identity_policy_refuses_both_modes(string key, string value)
    {
        foreach (var mode in new[] { RuntimeMode.Demo, RuntimeMode.Production })
        {
            var services = new ServiceCollection();
            var config = Configuration(new Dictionary<string, string?> { [key] = value });
            var error = Assert.Throws<InvalidOperationException>(() =>
                services.AddStrataAiIdentity(config, new(mode, "test", "test")));
            Assert.Contains(key, error.Message);
            Assert.DoesNotContain("invalid-policy-value", error.Message);
            Assert.DoesNotContain(services, d => d.ServiceType == typeof(IIdentityStore));
        }
    }

    [Theory]
    [InlineData(RuntimeMode.Demo, true, false)]
    [InlineData(RuntimeMode.Production, false, true)]
    public void ARCH_12_PRD_02_omitted_settings_preserve_existing_mode_defaults(RuntimeMode mode, bool registration, bool verification)
    {
        var services = new ServiceCollection();
        services.AddStrataAiIdentity(Configuration([]), new(mode, "test", "test"));
        using var provider = services.BuildServiceProvider();
        var policy = provider.GetRequiredService<IdentityPolicy>();
        Assert.Equal(registration, policy.AllowSelfRegistration);
        Assert.Equal(verification, policy.RequireVerifiedEmail);
        Assert.Equal(12, policy.MinimumPasswordLength);
        Assert.Equal(TimeSpan.FromHours(12), policy.SessionLifetime);
        Assert.Equal(TimeSpan.FromMinutes(30), policy.SecurityTokenLifetime);
    }

    [Theory]
    [InlineData(8, 1, 5)]
    [InlineData(128, 720, 1440)]
    public void ARCH_12_PRD_02_valid_policy_boundaries_are_applied_exactly(int password, int hours, int minutes)
    {
        foreach (var mode in new[] { RuntimeMode.Demo, RuntimeMode.Production })
        {
            var services = new ServiceCollection();
            services.AddStrataAiIdentity(Configuration(new Dictionary<string, string?>
            {
                ["STRATAAI_AUTH_ALLOW_SELF_REGISTRATION"] = "false",
                ["STRATAAI_AUTH_REQUIRE_VERIFIED_EMAIL"] = "true",
                ["STRATAAI_AUTH_MIN_PASSWORD_LENGTH"] = password.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["STRATAAI_AUTH_SESSION_HOURS"] = hours.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["STRATAAI_AUTH_SECURITY_TOKEN_MINUTES"] = minutes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            }), new(mode, "test", "test"));
            using var provider = services.BuildServiceProvider();
            var policy = provider.GetRequiredService<IdentityPolicy>();
            Assert.False(policy.AllowSelfRegistration);
            Assert.True(policy.RequireVerifiedEmail);
            Assert.Equal(password, policy.MinimumPasswordLength);
            Assert.Equal(TimeSpan.FromHours(hours), policy.SessionLifetime);
            Assert.Equal(TimeSpan.FromMinutes(minutes), policy.SecurityTokenLifetime);
        }
    }

    private static IConfiguration Configuration(Dictionary<string, string?> settings)
    {
        settings["STRATAAI_AUTH_RETRY_CURRENT_KEY"] = "fixture";
        settings["STRATAAI_AUTH_RETRY_KEYS"] = System.Text.Json.JsonSerializer.Serialize(
            new Dictionary<string, string> { ["fixture"] = Convert.ToBase64String(new byte[32]) });
        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }
}
