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
    Task<bool> ConfirmDeleteAsync(DeletePlan plan);

    Task<bool> ConfirmRenameAsync(RenamePreview preview);

    // True when the user asked to restore the original names.
    Task<bool> ShowFileResultsAsync(string title, IReadOnlyList<FileOutcome> outcomes, bool canRestoreNames);

    // A question with a few answers and Cancel; null on Cancel.
    Task<T?> ChooseAsync<T>(string title, string message, IReadOnlyList<(string Label, T Value)> choices) where T : struct;
}
