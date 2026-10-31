using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Yeetle.App.Resources;
using Yeetle.App.ViewModels;
using Yeetle.App.Views;
using Yeetle.Application.Files;
using Yeetle.Application.Library;
using Yeetle.Application.Sessions;

namespace Yeetle.App.Services;

// Each dialog is modal over the active window, except the Tag Manager.
public sealed class WindowDialogs(Func<SourceFoldersViewModel> createSourceFolders, Func<TagManagerViewModel> createTagManager)
    : IDialogs
{
    // Not modal: the Tag Manager stays open beside a running session (spec §5).
    private TagManagerWindow? _tagManager;

    public static Window? ActiveWindow() =>
        Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.Windows.FirstOrDefault(w => w.IsActive && w is not VideoOverlayWindow) ?? desktop.MainWindow
            : null;

    public async Task ShowSourceFoldersAsync()
    {
        var viewModel = createSourceFolders();
        await viewModel.LoadAsync();
        await ShowAsync<object>(new SourceFoldersWindow { DataContext = viewModel });
    }

    public void ShowTagManager(string? newTagName = null)
    {
        if (_tagManager is { } open)
        {
            if (newTagName is not null && open.DataContext is TagManagerViewModel viewModel)
                viewModel.PendingName = newTagName;
            open.Activate();
            return;
        }
        var created = createTagManager();
        created.PendingName = newTagName ?? string.Empty;
        var window = new TagManagerWindow { DataContext = created };
        window.Closed += (_, _) => _tagManager = null;
        _tagManager = window;
        _ = created.LoadAsync();
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } main })
            window.Show(main);
        else
            window.Show();
    }

    public Task<FolderRemoval?> AskFolderRemovalAsync(string folder, int clipCount) =>
        ChooseAsync(Strings.RemoveFolderTitle, string.Format(Strings.RemoveFolderMessage, folder, clipCount),
            [(Strings.HideClips, FolderRemoval.HideClips), (Strings.StopScanningKeepClips, FolderRemoval.StopScanning)]);

    public Task<T?> ChooseAsync<T>(string title, string message, IReadOnlyList<(string Label, T Value)> choices)
        where T : struct =>
        ShowAsync<T?>(new ChoiceWindow(title, message, choices.Select(c => (c.Label, (object)c.Value))));

    public Task<SummaryChoice?> ShowSessionSummaryAsync(SessionSummary summary) =>
        ShowAsync<SummaryChoice?>(new SessionSummaryWindow { DataContext = new SessionSummaryViewModel(summary) });

    public Task<bool> ConfirmDeleteAsync(DeletePlan plan) =>
        ShowAsync<bool>(new DeletePromptWindow { DataContext = new DeletePromptViewModel(plan) });

    public Task<bool> ConfirmRenameAsync(RenamePreview preview) =>
        ShowAsync<bool>(new RenamePromptWindow { DataContext = new RenamePromptViewModel(preview) });

    public Task<bool> ShowFileResultsAsync(string title, IReadOnlyList<FileOutcome> outcomes, bool canRestoreNames) =>
        ShowAsync<bool>(new FileResultsWindow { DataContext = new FileResultsViewModel(title, outcomes, canRestoreNames) });

    public Task<FirstScanChoice?> AskFirstScanAsync(PendingFirstScan pending) =>
        ShowAsync<FirstScanChoice?>(new FirstScanWindow { DataContext = new FirstScanViewModel(pending) });

    private static async Task<T?> ShowAsync<T>(Window dialog)
    {
        if (ActiveWindow() is not { } owner)
            return default;
        return await dialog.ShowDialog<T?>(owner);
    }
}
