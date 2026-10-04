using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Common;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;
public sealed partial class ApiHostTests
{
    [Fact]
    public async Task Global_search_cursors_bind_actor_query_dimensions_and_scope_and_expire()
    {
        var clock = new ReceiptTestClock();
        await using var app = new ApiFactory(configureServices: services =>
        {
            services.AddSingleton<IClock>(clock);
            services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        });
        var codec = app.Services.GetRequiredService<IGlobalSearchCursorCodec>();
        var binding = new GlobalSearchBinding(Guid.NewGuid(), "Roadmap", "Priority", "Alex", true, GlobalSearchLifecycleScope.Active);
        var position = new GlobalSearchPosition(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var token = codec.Encode(binding, position);
        Assert.True(codec.TryDecode(binding, token, out var decoded)); Assert.Equal(position, decoded);
        foreach (var changed in new[] { binding with { ActorId = Guid.NewGuid() }, binding with { Keyword = "Other" },
            binding with { Label = "Other" }, binding with { Member = "Other" }, binding with { MatchAll = false },
            binding with { Scope = GlobalSearchLifecycleScope.Archived } })
        { Assert.False(codec.TryDecode(changed, token, out var rejected)); Assert.Null(rejected); }
        foreach (var invalid in new[] { "", "invalid", token[..(token.Length / 2)], new string('x', 8193) })
        { Assert.False(codec.TryDecode(binding, invalid, out var rejected)); Assert.Null(rejected); }
        Assert.Throws<ArgumentException>(() => codec.Encode(binding, position with { OrganizationId = Guid.Empty }));
        Assert.Throws<ArgumentException>(() => codec.Encode(binding, position with { BoardId = null }));
        Assert.Throws<ArgumentException>(() => codec.Encode(binding with { Keyword = " padded " }, position));
        Assert.Throws<ArgumentException>(() => codec.Encode(binding with { Member = new string('x', 161) }, position));
        var multilingual = binding with { Keyword = new string('界', 160), Label = new string('é', 160), Member = new string('語', 160) };
        var multilingualToken = codec.Encode(multilingual, position);
        Assert.InRange(multilingualToken.Length, 1, 8192);
        Assert.True(codec.TryDecode(multilingual, multilingualToken, out var multilingualPosition));
        Assert.Equal(position, multilingualPosition);
        foreach (var boundary in new[] { position with { CardId = null }, position with { BoardId = null, CardId = null } })
        {
            var boundaryToken = codec.Encode(binding, boundary);
            Assert.True(codec.TryDecode(binding, boundaryToken, out var restored)); Assert.Equal(boundary, restored);
        }
        clock.UtcNow = clock.UtcNow.AddMinutes(15).AddTicks(-1);
        Assert.True(codec.TryDecode(binding, token, out _));
        clock.UtcNow = clock.UtcNow.AddTicks(1);
        Assert.False(codec.TryDecode(binding, token, out var expired)); Assert.Null(expired);
    }
}
