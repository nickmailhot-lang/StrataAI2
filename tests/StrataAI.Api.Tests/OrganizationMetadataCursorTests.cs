using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Common;
using StrataAI.Application.Organizations;
using Xunit;

namespace StrataAI.Api.Tests;
public sealed partial class ApiHostTests
{
    [Fact]
    public async Task PRD_03_Metadata_cursor_binds_actor_organization_membership_and_revision_and_expires()
    {
        var clock = new ReceiptTestClock();
        await using var app = new ApiFactory(configureServices: services =>
        {
            services.AddSingleton<IClock>(clock);
            services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        });
        var codec = app.Services.GetRequiredService<IOrganizationMetadataCursorCodec>();
        var binding = new OrganizationMetadataCursorBinding(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1);
        var token = codec.Encode(binding, long.MaxValue);
        Assert.True(codec.TryDecode(binding, token, out var position)); Assert.Equal(long.MaxValue, position);
        foreach (var changed in new[] { binding with { ActorId = Guid.NewGuid() }, binding with { OrganizationId = Guid.NewGuid() },
            binding with { MembershipId = Guid.NewGuid() }, binding with { MembershipVersion = 2 } })
        { Assert.False(codec.TryDecode(changed, token, out var rejected)); Assert.Equal(0, rejected); }
        foreach (var malformed in new[] { "invalid", "", token[..(token.Length / 2)], new string('x', 4097) })
        { Assert.False(codec.TryDecode(binding, malformed, out var rejected)); Assert.Equal(0, rejected); }
        var foreign = app.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("StrataAI2.OrganizationBoardCursor", "v1");
        Assert.False(codec.TryDecode(binding, foreign.Protect("{}"), out _));
        Assert.Throws<ArgumentException>(() => codec.Encode(binding, -1));
        Assert.Throws<ArgumentException>(() => codec.Encode(binding with { MembershipVersion = 0 }, 0));
        clock.UtcNow = clock.UtcNow.AddMinutes(16);
        Assert.False(codec.TryDecode(binding, token, out var expired)); Assert.Equal(0, expired);
    }
}
