namespace Yeetle.Infrastructure;

// Everything Yeetle writes lives under %LOCALAPPDATA%\Yeetle (spec §10).
public sealed class AppPaths(string root)
{
    private const string AppFolderName = "Yeetle";

    public static AppPaths Default { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName));

    public string Root { get; } = root;

    public string Database => Path.Combine(Root, "yeetle.db");

    public string Backups => Path.Combine(Root, "Backups");

    public string Logs => Path.Combine(Root, "Logs");
}
