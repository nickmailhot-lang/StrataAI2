using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Common;
using StrataAI.Application.Organizations;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Theory]
    [InlineData(OrganizationRole.Owner)]
    [InlineData(OrganizationRole.Admin)]
    public async Task PRD_27_configuration_cursor_binds_current_admin_epoch_and_expires(OrganizationRole role)
    {
        var clock = new ConfigurationCursorClock();
        await using var app = new ApiFactory(configureServices: services =>
        {
            services.AddSingleton<IClock>(clock);
            services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        });
        var codec = app.Services.GetRequiredService<IOrganizationConfigurationCursorCodec>();
        var binding = new OrganizationConfigurationCursorBinding(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, role);
        var token = codec.Encode(binding, long.MaxValue);
        Assert.True(codec.TryDecode(binding, token, out var position)); Assert.Equal(long.MaxValue, position);
        foreach (var changed in new[] { binding with { OrganizationId = Guid.NewGuid() }, binding with { ActorId = Guid.NewGuid() },
            binding with { MembershipId = Guid.NewGuid() }, binding with { MembershipVersion = 2 },
            binding with { Role = role == OrganizationRole.Owner ? OrganizationRole.Admin : OrganizationRole.Owner } })
        { Assert.False(codec.TryDecode(changed, token, out var rejected)); Assert.Equal(0, rejected); }
        foreach (var malformed in new[] { "", "invalid", token[..(token.Length / 2)], new string('x', 4097) })
        { Assert.False(codec.TryDecode(binding, malformed, out var rejected)); Assert.Equal(0, rejected); }
        var metadata = app.Services.GetRequiredService<IOrganizationMetadataCursorCodec>();
        var publicBinding = new OrganizationMetadataCursorBinding(binding.OrganizationId, binding.ActorId, binding.MembershipId, binding.MembershipVersion);
        Assert.False(codec.TryDecode(binding, metadata.Encode(publicBinding, 1), out _));
        Assert.False(metadata.TryDecode(publicBinding, token, out _));
        Assert.Throws<ArgumentException>(() => codec.Encode(binding with { Role = OrganizationRole.Member }, 0));
        Assert.Throws<ArgumentException>(() => codec.Encode(binding with { MembershipVersion = 0 }, 0));
        Assert.Throws<ArgumentException>(() => codec.Encode(binding, -1));
        clock.UtcNow = clock.UtcNow.AddMinutes(15);
        Assert.False(codec.TryDecode(binding, token, out var expired)); Assert.Equal(0, expired);
    }

    private sealed class ConfigurationCursorClock : IClock
    { public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero); }
}
