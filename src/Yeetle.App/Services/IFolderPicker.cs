namespace Yeetle.App.Services;

public interface IFolderPicker
{
    // The chosen folder's local path, or null when the user cancelled.
    Task<string?> PickFolderAsync(string title);
}
