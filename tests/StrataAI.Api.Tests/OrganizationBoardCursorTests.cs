using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Common;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;
public sealed partial class ApiHostTests
{
    [Fact]
    public async Task Organization_Board_cursor_binds_actor_parent_membership_and_permission_generation_and_expires()
    {
        var clock = new ReceiptTestClock();
        await using var app = new ApiFactory(configureServices: services =>
        {
            services.AddSingleton<IClock>(clock);
            services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        });
        var codec = app.Services.GetRequiredService<IOrganizationBoardCursorCodec>();
        var binding = new OrganizationBoardCursorBinding(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 3, Guid.NewGuid(), 7);
        var token = codec.Encode(binding, long.MaxValue);
        Assert.True(codec.TryDecode(binding, token, out var restored));
        Assert.Equal(long.MaxValue, restored);
        foreach (var changed in new[] { binding with { ActorId = Guid.NewGuid() },
            binding with { OrganizationId = Guid.NewGuid() }, binding with { MembershipId = Guid.NewGuid() },
            binding with { OrganizationVersion = 4 }, binding with { PermissionGeneration = Guid.NewGuid() },
            binding with { PermissionRevision = 8 } })
        {
            Assert.False(codec.TryDecode(changed, token, out var refused));
            Assert.Equal(0, refused);
        }
        foreach (var malformed in new[] { "", "invalid", token[..(token.Length / 2)], new string('x', 4097) })
        {
            Assert.False(codec.TryDecode(binding, malformed, out var refused));
            Assert.Equal(0, refused);
        }
        var otherPurpose = app.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("StrataAI2.GlobalSearchCursor", "v1");
        Assert.False(codec.TryDecode(binding, otherPurpose.Protect("{}"), out _));
        Assert.Throws<ArgumentException>(() => codec.Encode(binding, -1));
        Assert.Throws<ArgumentException>(() => codec.Encode(binding with { PermissionGeneration = Guid.Empty }, 0));
        Assert.Throws<ArgumentException>(() => codec.Encode(binding with { PermissionRevision = 0 }, 0));
        var reader = binding with { Audience = OrganizationBoardAudience.BoardDiscovery, ReaderRevision = 1 };
        var readerToken = codec.Encode(reader, long.MaxValue);
        Assert.True(codec.TryDecode(reader, readerToken, out var readerPosition));
        Assert.Equal(long.MaxValue, readerPosition);
        Assert.False(codec.TryDecode(binding, readerToken, out var wrongAudience));
        Assert.Equal(0, wrongAudience);
        Assert.False(codec.TryDecode(reader, token, out wrongAudience));
        Assert.Equal(0, wrongAudience);
        Assert.False(codec.TryDecode(reader with { ReaderRevision = 2 }, readerToken, out var staleReader));
        Assert.Equal(0, staleReader);
        Assert.Throws<ArgumentException>(() => codec.Encode(reader with { ReaderRevision = 0 }, 0));
        Assert.Throws<ArgumentException>(() => codec.Encode(binding with { ReaderRevision = 1 }, 0));
        Assert.Throws<ArgumentException>(() => codec.Encode(binding with { Audience = (OrganizationBoardAudience)99 }, 0));
        clock.UtcNow = clock.UtcNow.AddMinutes(15).AddTicks(-1);
        Assert.True(codec.TryDecode(binding, token, out _));
        clock.UtcNow = clock.UtcNow.AddTicks(1);
        Assert.False(codec.TryDecode(binding, token, out var expired));
        Assert.Equal(0, expired);
    }
}
