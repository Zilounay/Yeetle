using Yeetle.Application.Library;

namespace Yeetle.App.Services;

// The app's own dialogs, kept behind an interface so view models stay testable.
public interface IDialogs
{
    Task ShowSourceFoldersAsync();

    // Null when the user cancelled.
    Task<FolderRemoval?> AskFolderRemovalAsync(string folder, int clipCount);

    // Null when the user closed the dialog; the folder is asked about again on the next scan.
    Task<FirstScanChoice?> AskFirstScanAsync(PendingFirstScan pending);

    // A question with a few answers and Cancel; null on Cancel.
    Task<T?> ChooseAsync<T>(string title, string message, IReadOnlyList<(string Label, T Value)> choices) where T : struct;
}
