using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Yeetle.App.Resources;
using Yeetle.App.ViewModels;
using Yeetle.App.Views;
using Yeetle.Application.Library;

namespace Yeetle.App.Services;

// Each dialog is modal over the active window.
public sealed class WindowDialogs(Func<SourceFoldersViewModel> createSourceFolders) : IDialogs
{
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

    public Task<FolderRemoval?> AskFolderRemovalAsync(string folder, int clipCount) =>
        ShowAsync<FolderRemoval?>(new ChoiceWindow(
            Strings.RemoveFolderTitle,
            string.Format(Strings.RemoveFolderMessage, folder, clipCount),
            [(Strings.HideClips, FolderRemoval.HideClips), (Strings.StopScanningKeepClips, FolderRemoval.StopScanning)]));

    public Task<FirstScanChoice?> AskFirstScanAsync(PendingFirstScan pending) =>
        ShowAsync<FirstScanChoice?>(new FirstScanWindow { DataContext = new FirstScanViewModel(pending) });

    private static async Task<T?> ShowAsync<T>(Window dialog)
    {
        if (ActiveWindow() is not { } owner)
            return default;
        return await dialog.ShowDialog<T?>(owner);
    }
}
