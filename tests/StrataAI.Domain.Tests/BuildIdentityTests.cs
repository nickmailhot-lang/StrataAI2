using System.Reflection;
using System.Reflection.Emit;
using StrataAI.Infrastructure.Runtime;
using Xunit;

namespace StrataAI.Domain.Tests;

public sealed class BuildIdentityTests
{
    // ARCH-01-AC-002 / ARCH-11: only baked assembly identity can label a host.
    [Theory]
    [InlineData("development", "0.0.0-dev")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "0.1.0-42")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "1.2.3")]
    public void Embedded_revision_and_version_are_read_exactly(string revision, string version)
    {
        var identity = BuildIdentityReader.Read(WithMetadata(("StrataAI.BuildRevision", revision), ("StrataAI.BuildVersion", version)));
        Assert.Equal(new EmbeddedBuildIdentity(revision, version), identity);
    }

    [Theory]
    [InlineData("StrataAI.BuildRevision", "runtime-spoof", "invalid embedded build revision")]
    [InlineData("StrataAI.BuildVersion", "sensitive\ncontent", "invalid embedded build version")]
    public void Invalid_embedded_values_fail_without_echoing_them(string key, string value, string error)
    {
        var revision = key == "StrataAI.BuildRevision" ? value : "development";
        var version = key == "StrataAI.BuildVersion" ? value : "1.0.0";
        var failure = Assert.Throws<InvalidOperationException>(() => BuildIdentityReader.Read(WithMetadata(("StrataAI.BuildRevision", revision), ("StrataAI.BuildVersion", version))));
        Assert.Contains(error, failure.Message);
        Assert.DoesNotContain(value, failure.Message);
    }

    [Fact]
    public void Missing_or_duplicate_identity_is_rejected()
    {
        Assert.Throws<InvalidOperationException>(() => BuildIdentityReader.Read(WithMetadata()));
        Assert.Throws<InvalidOperationException>(() => BuildIdentityReader.Read(WithMetadata(
            ("StrataAI.BuildRevision", "development"), ("StrataAI.BuildRevision", "development"), ("StrataAI.BuildVersion", "1.0.0"))));
    }

    private static Assembly WithMetadata(params (string Key, string Value)[] metadata)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName($"BuildIdentityFixture_{Guid.NewGuid():N}"), AssemblyBuilderAccess.RunAndCollect);
        var constructor = typeof(AssemblyMetadataAttribute).GetConstructor([typeof(string), typeof(string)])!;
        foreach (var (key, value) in metadata) assembly.SetCustomAttribute(new CustomAttributeBuilder(constructor, [key, value]));
        return assembly;
    }
}
