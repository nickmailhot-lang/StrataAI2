using Microsoft.Extensions.Configuration;
using StrataAI.Application.Runtime;
using StrataAI.Infrastructure.Onboarding;
using Xunit;

namespace StrataAI.Domain.Tests;

public sealed class InvitationMailConfigurationTests
{
    [Theory]
    [InlineData(null, "false", RuntimeMode.Demo, false)]
    [InlineData("false", "false", RuntimeMode.Production, false)]
    [InlineData("true", "true", RuntimeMode.Production, true)]
    public void Explicit_transport_configuration_controls_publication(string? flag, string identity, RuntimeMode mode, bool expected)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
            ["STRATAAI_INVITATION_EMAIL_ENABLED"] = flag, ["STRATAAI_IDENTITY_EMAIL_ENABLED"] = identity }).Build();
        Assert.Equal(expected, InvitationMailRegistration.IsEnabled(config, new(mode, "test", "test")));
    }

    [Theory]
    [InlineData("invalid", "true", RuntimeMode.Production)]
    [InlineData("true", "false", RuntimeMode.Production)]
    [InlineData("true", "true", RuntimeMode.Demo)]
    public void Misconfigured_invitation_delivery_fails_closed(string flag, string identity, RuntimeMode mode)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
            ["STRATAAI_INVITATION_EMAIL_ENABLED"] = flag, ["STRATAAI_IDENTITY_EMAIL_ENABLED"] = identity }).Build();
        Assert.Throws<InvalidOperationException>(() => InvitationMailRegistration.IsEnabled(config, new(mode, "test", "test")));
    }
}
