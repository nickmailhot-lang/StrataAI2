using System.Xml.Linq;
using System.Runtime.CompilerServices;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed class ArchitectureBoundaryTests
{
    // ARCH-01-AC-003 / ARCH-03: production project references must preserve
    // Domain <- Application <- Infrastructure and the two host boundaries.
    [Fact]
    public void Production_projects_preserve_the_adopted_dependency_direction()
    {
        var root = FindRepositoryRoot();
        Assert.NotNull(root);
        var allowed = new Dictionary<string, string[]>
        {
            ["StrataAI.Domain"] = [],
            ["StrataAI.Application"] = ["StrataAI.Domain"],
            ["StrataAI.Infrastructure"] = ["StrataAI.Application", "StrataAI.Domain"],
            ["StrataAI.Api"] = ["StrataAI.Application", "StrataAI.Infrastructure"],
            ["StrataAI.Worker"] = ["StrataAI.Application", "StrataAI.Infrastructure"],
        };
        foreach (var (project, dependencies) in allowed)
        {
            var source = XDocument.Load(Path.Combine(root.FullName, "src", project, $"{project}.csproj"));
            foreach (var reference in source.Descendants("ProjectReference"))
            {
                var path = reference.Attribute("Include")?.Value;
                Assert.NotNull(path);
                var dependency = Path.GetFileNameWithoutExtension(path.Replace('\\', '/'));
                Assert.Contains(dependency, dependencies);
                var expected = Path.Combine(root.FullName, "src", dependency, $"{dependency}.csproj");
                var actual = Path.GetFullPath(Path.Combine(root.FullName, "src", project, path.Replace('\\', '/')));
                Assert.Equal(expected, actual);
            }
            if (project == "StrataAI.Domain") Assert.Empty(source.Descendants("PackageReference"));
        }
    }

    // Independent compiled-runtime evidence lives outside the checkout. Keep
    // checking the actual source projects rather than requiring in-place output.
    // Missing source still fails the architecture assertion above.
    private static DirectoryInfo? FindRepositoryRoot([CallerFilePath] string sourceFile = "")
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Path.GetDirectoryName(sourceFile), Environment.CurrentDirectory })
        {
            if (string.IsNullOrEmpty(start)) continue;
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props"))) return directory;
                directory = directory.Parent;
            }
        }
        return null;
    }
}
