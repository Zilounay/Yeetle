namespace Yeetle.Infrastructure;

// Everything Yeetle writes lives under %LOCALAPPDATA%\Yeetle (spec §10).
public sealed class AppPaths(string root)
{
    // For testing: points Yeetle at a throwaway folder, so a test run never touches the real
    // library, settings or backups.
    public const string DataFolderVariable = "YEETLE_DATA_FOLDER";

    private const string AppFolderName = "Yeetle";

    public static AppPaths Default { get; } = new(
        Environment.GetEnvironmentVariable(DataFolderVariable) is { Length: > 0 } testFolder
            ? Path.GetFullPath(testFolder)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName));

    public string Root { get; } = root;

    public string Database => Path.Combine(Root, "yeetle.db");

    public string Settings => Path.Combine(Root, "settings.json");

    public string Backups => Path.Combine(Root, "Backups");

    public string Logs => Path.Combine(Root, "Logs");
}
