using Avalonia.Platform.Storage;

namespace Yeetle.App.Services;

// The system folder dialog, opened over the active window (a dialog, or else the main window).
public sealed class StorageFolderPicker : IFolderPicker
{
    public async Task<string?> PickFolderAsync(string title)
    {
        if (WindowDialogs.ActiveWindow() is not { } window)
            return null;
        var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title });
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }
}
