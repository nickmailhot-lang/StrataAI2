using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Common;
using StrataAI.Application.Onboarding;
using StrataAI.Application.Runtime;
using StrataAI.Infrastructure.Onboarding;

namespace StrataAI.Domain.Tests;

public sealed class InvitationRecipientReplayTests
{
    private static readonly InvitationRecipientCursorBinding Binding = new(Guid.NewGuid(), "RECIPIENT@EXAMPLE.TEST", 1);
    private static InvitationRecipientEvent Row(long sequence) => new(Guid.NewGuid(), "INVITATION_CREATED", sequence, DateTimeOffset.UtcNow);

    [Fact]
    public void PRD_60_Recipient_replay_keeps_contiguous_order_and_original_event_identity_across_pages()
    {
        var first = Row(1); var second = Row(2);
        var page = InvitationRecipientEventWindow.Build(0, 2, 1, [first, second]);
        Assert.True(page.HasMore); Assert.False(page.ResetRequired); Assert.Equal(1, page.Position);
        Assert.Same(first, Assert.Single(page.Events));
        var final = InvitationRecipientEventWindow.Build(page.Position, 2, 1, [second]);
        Assert.False(final.HasMore); Assert.Equal(2, final.Position); Assert.Same(second, Assert.Single(final.Events));
        Assert.Empty(InvitationRecipientEventWindow.Build(2, 2, 1, []).Events);
    }
    [Fact]
    public void PRD_60_Recipient_replay_discards_gaps_duplicates_reordering_future_positions_and_invalid_sources()
    {
        var row = Row(1);
        foreach (var rows in new IReadOnlyList<InvitationRecipientEvent>[] {
            [], [Row(2)], [row, row with { Sequence = 2 }], [Row(1), Row(3)],
            [row with { EventId = Guid.Empty }], [row with { EventType = "SECRET_SOURCE" }],
            [row with { CreatedAt = default }] })
        {
            var result = InvitationRecipientEventWindow.Build(0, 2, 2, rows);
            Assert.True(result.ResetRequired); Assert.Empty(result.Events); Assert.False(result.HasMore);
        }
        Assert.True(InvitationRecipientEventWindow.Build(3, 2, 1, []).ResetRequired);
        Assert.True(InvitationRecipientEventWindow.Build(long.MaxValue, long.MaxValue, 1, [Row(long.MaxValue)]).ResetRequired);
    }
    [Fact]
    public async Task PRD_60_Recipient_bootstrap_and_expired_or_wrong_scope_cursor_require_protected_discovery()
    {
        var clock = new Clock(); using var provider = CodecProvider(clock); var codec = provider.GetRequiredService<IInvitationRecipientCursorCodec>();
        var reader = new Reader(); var service = new InvitationRecipientSynchronizationService(reader, codec);
        var wrongActor = codec.Encode(Binding with { ActorId = Guid.NewGuid() }, 0);
        var wrongEmail = codec.Encode(Binding with { EmailNormalized = "OTHER@EXAMPLE.TEST" }, 0);
        var oldRevision = codec.Encode(Binding with { AccountVersion = 2 }, 0);
        var expired = codec.Encode(Binding, 0); clock.UtcNow = clock.UtcNow.AddMinutes(15);
        foreach (var cursor in new string?[] { null, "corrupt", wrongActor, wrongEmail, oldRevision, expired })
        {
            var result = await service.ReadAsync(Binding.ActorId, cursor, cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(result.Succeeded); Assert.True(result.Value!.ResetRequired); Assert.Empty(result.Value.Events);
            Assert.True(codec.TryDecode(Binding, result.Value.Cursor, out var position)); Assert.Equal(2, position);
        }
        Assert.Equal(0, reader.Reads);
    }
    [Theory]
    [InlineData("withdraw")]
    [InlineData("email")]
    [InlineData("revision")]
    public async Task PRD_60_Recipient_final_account_or_email_change_discards_read_payload(string change)
    {
        using var provider = CodecProvider(new Clock()); var codec = provider.GetRequiredService<IInvitationRecipientCursorCodec>();
        var reader = new Reader(); var cursor = codec.Encode(Binding, 0);
        reader.OnRead = () => reader.Scope = change switch { "withdraw" => null,
            "email" => Binding with { EmailNormalized = "OTHER@EXAMPLE.TEST" }, _ => Binding with { AccountVersion = 2 } };
        var result = await new InvitationRecipientSynchronizationService(reader, codec).ReadAsync(Binding.ActorId, cursor,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(result.Succeeded); Assert.Null(result.Value); Assert.Equal("account_unavailable", result.ErrorCode);
    }
    [Fact]
    public void PRD_60_Recipient_cursor_is_protected_and_actor_email_revision_bound_and_expires()
    {
        var clock = new Clock(); using var provider = CodecProvider(clock); var codec = provider.GetRequiredService<IInvitationRecipientCursorCodec>();
        var token = codec.Encode(Binding, 7);
        Assert.DoesNotContain(Binding.EmailNormalized, token); Assert.DoesNotContain(Binding.ActorId.ToString(), token);
        Assert.True(codec.TryDecode(Binding, token, out var position)); Assert.Equal(7, position);
        Assert.False(codec.TryDecode(Binding with { ActorId = Guid.NewGuid() }, token, out _));
        Assert.False(codec.TryDecode(Binding with { EmailNormalized = "NEW@EXAMPLE.TEST" }, token, out _));
        Assert.False(codec.TryDecode(Binding with { AccountVersion = 2 }, token, out _));
        Assert.False(codec.TryDecode(Binding, token + "tampered", out _));
        Assert.False(codec.TryDecode(Binding, new string('x', 4097), out _));
        clock.UtcNow = clock.UtcNow.AddMinutes(15);
        Assert.False(codec.TryDecode(Binding, token, out _));
    }
    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task PRD_60_Recipient_refuses_unbounded_reads_before_discovery(int limit)
    {
        using var provider = CodecProvider(new Clock()); var reader = new Reader();
        var result = await new InvitationRecipientSynchronizationService(reader, provider.GetRequiredService<IInvitationRecipientCursorCodec>())
            .ReadAsync(Binding.ActorId, null, limit, TestContext.Current.CancellationToken);
        Assert.Equal("invalid_sync_limit", result.ErrorCode); Assert.Equal(0, reader.ScopeReads);
    }
    [Fact]
    public async Task PRD_60_Recipient_delivery_cursor_check_rebinds_after_session_IO_without_reading_history()
    {
        using var provider = CodecProvider(new Clock()); var codec = provider.GetRequiredService<IInvitationRecipientCursorCodec>();
        var reader = new Reader(); var service = new InvitationRecipientSynchronizationService(reader, codec);
        var cursor = codec.Encode(Binding, 2); var ct = TestContext.Current.CancellationToken;
        Assert.True((await service.IsCursorCurrentAsync(Binding.ActorId, cursor, ct)).Value);
        reader.Scope = Binding with { AccountVersion = 2 };
        Assert.False((await service.IsCursorCurrentAsync(Binding.ActorId, cursor, ct)).Value);
        reader.Scope = null;
        Assert.Equal("account_unavailable", (await service.IsCursorCurrentAsync(Binding.ActorId, cursor, ct)).ErrorCode);
        Assert.Equal(0, reader.Reads);
    }
    [Fact]
    public async Task PRD_60_Recipient_envelope_contains_only_source_identity_type_order_and_timestamp()
    {
        using var provider = CodecProvider(new Clock()); var codec = provider.GetRequiredService<IInvitationRecipientCursorCodec>();
        var result = await new InvitationRecipientSynchronizationService(new Reader(), codec).ReadAsync(Binding.ActorId,
            codec.Encode(Binding, 0), cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded); Assert.Equal(2, result.Value!.Events.Count);
        var json = System.Text.Json.JsonSerializer.Serialize(result.Value.Events);
        foreach (var privateField in new[] { "Organization", "InvitationId", "Actor", "Email", "Role", "Correlation", "Metadata", "Token" })
            Assert.DoesNotContain(privateField, json);
    }
    private static ServiceProvider CodecProvider(Clock clock)
    {
        var services = new ServiceCollection(); services.AddSingleton<IClock>(clock);
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddStrataAiOnboarding(new(RuntimeMode.Production, "test", "test"), new ConfigurationBuilder().Build());
        return services.BuildServiceProvider();
    }
    private sealed class Clock : IClock { public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow; }
    private sealed class Reader : IInvitationRecipientEventReader
    {
        public InvitationRecipientCursorBinding? Scope { get; set; } = Binding;
        public Action? OnRead { get; set; }
        public int Reads { get; private set; }
        public int ScopeReads { get; private set; }
        public Task<InvitationRecipientCursorBinding?> GetScopeAsync(Guid actorId, CancellationToken cancellationToken)
        { ScopeReads++; return Task.FromResult(Scope); }
        public Task<long> GetHeadAsync(InvitationRecipientCursorBinding binding, CancellationToken cancellationToken) => Task.FromResult(2L);
        public Task<InvitationRecipientEventWindow> ReadAsync(InvitationRecipientCursorBinding binding, long since, int limit, CancellationToken cancellationToken)
        { Reads++; OnRead?.Invoke(); return Task.FromResult(InvitationRecipientEventWindow.Build(since, 2, limit, [Row(1), Row(2)])); }
    }
}
