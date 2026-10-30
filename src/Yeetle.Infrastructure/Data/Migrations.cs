using System.Reflection;

namespace Yeetle.Infrastructure.Data;

public sealed record Migration(int Version, string Name, string Sql);

// The numbered scripts in Migrations/, embedded in this assembly: NNNN_description.sql.
public static class Migrations
{
    private const string Folder = ".Migrations.";
    private const string Extension = ".sql";
    private const char NumberSeparator = '_';

    public static IReadOnlyList<Migration> All { get; } = Load();

    public static int Latest => All[^1].Version;

    private static List<Migration> Load()
    {
        var assembly = typeof(Migrations).Assembly;
        var migrations = assembly.GetManifestResourceNames()
            .Where(n => n.Contains(Folder) && n.EndsWith(Extension, StringComparison.Ordinal))
            .Select(n => Read(assembly, n))
            .OrderBy(m => m.Version)
            .ToList();
        for (var i = 0; i < migrations.Count; i++)
        {
            if (migrations[i].Version != i + 1)
                throw new InvalidOperationException($"Migration {i + 1} is missing or numbered twice.");
        }
        return migrations;
    }

    private static Migration Read(Assembly assembly, string resource)
    {
        var name = resource[(resource.IndexOf(Folder, StringComparison.Ordinal) + Folder.Length)..^Extension.Length];
        using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!);
        return new Migration(int.Parse(name[..name.IndexOf(NumberSeparator)]), name, reader.ReadToEnd());
    }
}
