using Yeetle.Application.Files;
using Yeetle.Application.Library;
using Yeetle.Application.Sessions;
using Yeetle.App.ViewModels;

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

    // The read-only summary (spec §5); null goes back to the session.
    Task<SummaryChoice?> ShowSessionSummaryAsync(SessionSummary summary);

    // True only when the user chose to delete or rename.
    // fromSession: the yeeted files of a session just committed, rather than an Explorer selection.
    Task<bool> ConfirmDeleteAsync(DeletePlan plan, bool fromSession);

    Task<bool> ConfirmRenameAsync(RenamePreview preview, bool fromSession);

    // The marker window (spec §7): read-only text with a Copy all button.
    Task ShowMarkerSheetAsync(string text);

    // True when the user asked to restore the original names. With a path, Show in folder opens
    // Windows Explorer on that file.
    Task<bool> ShowFileResultsAsync(string title, IReadOnlyList<FileOutcome> outcomes, bool canRestoreNames,
        string? showInFolder = null);

    // A question with a few answers and Cancel; null on Cancel.
    Task<T?> ChooseAsync<T>(string title, string message, IReadOnlyList<(string Label, T Value)> choices) where T : struct;
}
