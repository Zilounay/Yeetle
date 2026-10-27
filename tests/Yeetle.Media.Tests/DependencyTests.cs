using System.Reflection;

namespace Yeetle.Media.Tests;

public class DependencyTests
{
    // Dependency direction from CLAUDE.md, "Solution layout".
    private static readonly string[] AllowedYeetleReferences = ["Yeetle.Application"];

    [Fact]
    public void References_only_allowed_Yeetle_projects()
    {
        var referenced = Assembly.Load("Yeetle.Media")
            .GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => name.StartsWith("Yeetle.", StringComparison.Ordinal));

        Assert.Empty(referenced.Except(AllowedYeetleReferences));
    }
}
