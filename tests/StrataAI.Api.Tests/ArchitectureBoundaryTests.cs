using System.Xml.Linq;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed class ArchitectureBoundaryTests
{
    // ARCH-01-AC-003 / ARCH-03: production project references must preserve
    // Domain <- Application <- Infrastructure and the two host boundaries.
    [Fact]
    public void Production_projects_preserve_the_adopted_dependency_direction()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Directory.Build.props"))) root = root.Parent;
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
}
