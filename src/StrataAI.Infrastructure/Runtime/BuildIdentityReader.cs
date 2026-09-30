using System.Reflection;
using System.Text.RegularExpressions;

namespace StrataAI.Infrastructure.Runtime;

public sealed record EmbeddedBuildIdentity(string Revision, string Version);

public static class BuildIdentityReader
{
    public static EmbeddedBuildIdentity Read(Assembly hostAssembly)
    {
        var attributes = hostAssembly.GetCustomAttributes<AssemblyMetadataAttribute>().ToArray();
        var revisions = attributes.Where(value => value.Key == "StrataAI.BuildRevision").ToArray();
        var versions = attributes.Where(value => value.Key == "StrataAI.BuildVersion").ToArray();
        if (revisions.Length != 1 || versions.Length != 1)
            throw new InvalidOperationException("Host assembly requires one embedded build revision and version.");
        var revision = revisions[0].Value;
        var version = versions[0].Value;
        if (revision is null || (revision != "development" && !Regex.IsMatch(revision, "\\A(?:[a-f0-9]{40}|[a-f0-9]{64})\\z", RegexOptions.CultureInvariant)))
            throw new InvalidOperationException("Host assembly has an invalid embedded build revision.");
        if (version is null || !Regex.IsMatch(version, "\\A[0-9A-Za-z.+_-]{1,80}\\z", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("Host assembly has an invalid embedded build version.");
        return new EmbeddedBuildIdentity(revision, version);
    }
}
