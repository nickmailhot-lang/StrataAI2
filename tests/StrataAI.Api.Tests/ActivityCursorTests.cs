using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;
public sealed partial class ApiHostTests
{
    [Fact]
    public async Task Activity_cursors_authenticate_scope_and_reject_noncanonical_positions()
    {
        await using var app = new ApiFactory();
        var codec = app.Services.GetRequiredService<IActivityCursorCodec>();
        var binding = new ActivityCursorBinding(Guid.NewGuid(), Guid.NewGuid(), ActivityTargetKind.Card, Guid.NewGuid());
        var at = new DateTimeOffset(DateTimeOffset.UtcNow.UtcTicks / 10 * 10, TimeSpan.Zero);
        var position = new ActivityCursor(at, Guid.NewGuid());
        var token = codec.Encode(binding, position);
        Assert.True(codec.TryDecode(binding, token, out var decoded)); Assert.Equal(position, decoded);
        foreach (var changed in new[] { binding with { OrganizationId = Guid.NewGuid() }, binding with { ViewerId = Guid.NewGuid() },
            binding with { Kind = ActivityTargetKind.Board }, binding with { TargetId = Guid.NewGuid() } })
        { Assert.False(codec.TryDecode(changed, token, out var rejected)); Assert.Null(rejected); }
        foreach (var invalid in new[] { "", "invalid", token[..(token.Length / 2)], new string('x', 2049) })
        { Assert.False(codec.TryDecode(binding, invalid, out var rejected)); Assert.Null(rejected); }
        Assert.Throws<ArgumentException>(() => codec.Encode(binding, position with { CreatedAt = at.AddTicks(1) }));
        Assert.Throws<ArgumentException>(() => codec.Encode(binding, position with { CreatedAt = at.ToOffset(TimeSpan.FromHours(1)) }));
        Assert.Throws<ArgumentException>(() => codec.Encode(binding, position with { EventId = Guid.Empty }));
    }
}
