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

    // Opens the Tag Manager, or brings it forward. newTagName comes from the session's tag box
    // when the typed text matched no tag (spec §6).
    void ShowTagManager(string? newTagName = null);

    // A question with a few answers and Cancel; null on Cancel.
    Task<T?> ChooseAsync<T>(string title, string message, IReadOnlyList<(string Label, T Value)> choices) where T : struct;
}
