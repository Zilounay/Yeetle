using System.Reflection;

namespace Yeetle.App.Tests;

public class DependencyTests
{
    // Dependency direction from CLAUDE.md, "Solution layout".
    private static readonly string[] AllowedYeetleReferences = ["Yeetle.Application", "Yeetle.Domain"];

    [Fact]
    public void References_only_allowed_Yeetle_projects()
    {
        var referenced = Assembly.Load("Yeetle.App")
            .GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => name.StartsWith("Yeetle.", StringComparison.Ordinal));

        Assert.Empty(referenced.Except(AllowedYeetleReferences));
    }
}
