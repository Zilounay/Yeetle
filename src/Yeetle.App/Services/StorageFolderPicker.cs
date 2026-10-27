using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace Yeetle.App.Services;

// The system folder dialog, opened over the main window.
public sealed class StorageFolderPicker : IFolderPicker
{
    public async Task<string?> PickFolderAsync(string title)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime
            { MainWindow: { } window })
            return null;
        var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title });
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }
}