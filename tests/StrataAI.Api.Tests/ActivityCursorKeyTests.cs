using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Api.WorkManagement;
using StrataAI.Application.Common;
using StrataAI.Application.Runtime;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;
public sealed partial class ApiHostTests
{
    [Fact]
    public void Activity_continuation_survives_reopened_shared_keys_and_refuses_unrelated_key_domains()
    {
        var clock = new ReceiptTestClock();
        var directory = new DirectoryInfo(Path.Combine(Path.GetTempPath(), "strataai-activity-keys-" + Guid.NewGuid().ToString("N")));
        directory.Create();
        ServiceProvider Provider(string path, string? application = null)
        {
            var services = new ServiceCollection(); services.AddLogging(); services.AddSingleton<IClock>(clock);
            services.AddStrataAiWorkManagement(new(RuntimeMode.Demo, "test", "test"));
            services.AddStrataAiActivityKeys(new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["STRATAAI_ACTIVITY_KEY_DIRECTORY"] = path }).Build());
            if (application is not null) services.AddDataProtection().SetApplicationName(application);
            return services.BuildServiceProvider();
        }
        try
        {
            var binding = new ActivityCursorBinding(Guid.NewGuid(), Guid.NewGuid(), ActivityTargetKind.Card, Guid.NewGuid());
            var position = new ActivityCursor(new DateTimeOffset(clock.UtcNow.UtcTicks / 10 * 10, TimeSpan.Zero), Guid.NewGuid());
            string token;
            using (var first = Provider(directory.FullName)) token = first.GetRequiredService<IActivityCursorCodec>().Encode(binding, position);
            Assert.NotEmpty(directory.GetFiles("key-*.xml"));
            using var reopened = Provider(directory.FullName);
            Assert.True(reopened.GetRequiredService<IActivityCursorCodec>().TryDecode(binding, token, out var decoded)); Assert.Equal(position, decoded);
            using var unrelated = Provider(directory.FullName, "UnrelatedApplication");
            Assert.False(unrelated.GetRequiredService<IActivityCursorCodec>().TryDecode(binding, token, out var rejected)); Assert.Null(rejected);
        }
        finally
        {
            // Remove only this test's exact, freshly created directory/files.
            foreach (var file in directory.GetFiles()) file.Delete(); directory.Delete();
        }
    }
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("relative/activity-keys")]
    public void Activity_key_storage_rejects_ambiguous_directory_configuration(string path)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["STRATAAI_ACTIVITY_KEY_DIRECTORY"] = path }).Build();
        Assert.Throws<InvalidOperationException>(() => services.AddStrataAiActivityKeys(configuration));
    }
}
