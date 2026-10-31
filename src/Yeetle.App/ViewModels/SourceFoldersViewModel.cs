using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Yeetle.App.Resources;
using Yeetle.App.Services;
using Yeetle.Application.Library;

namespace Yeetle.App.ViewModels;

public sealed partial class SourceFolderItemViewModel(SourceFolder folder, SourceFoldersViewModel owner) : ViewModelBase
{
    [ObservableProperty]
    private bool _includeSubfolders = folder.IncludeSubfolders;

    public SourceFolder Folder { get; } = folder;

    public string Path => Folder.Path;

    public string Status => Folder.IsOffline ? Strings.FolderOffline
        : Folder.Status switch
        {
            FolderStatus.ScanStopped => Strings.FolderScanStopped,
            FolderStatus.Hidden => Strings.FolderHidden,
            _ => string.Empty,
        };

    public bool IsStopped => Folder.Status != FolderStatus.Active;

    partial void OnIncludeSubfoldersChanged(bool value) => _ = owner.SetIncludeSubfoldersAsync(this, value);

    [RelayCommand]
    private Task RemoveAsync() => owner.RemoveAsync(this);

    [RelayCommand]
    private Task ResumeAsync() => owner.ResumeAsync(this);
}

// The Library part of Settings (spec §11): source folders and Rescan.
public sealed partial class SourceFoldersViewModel(
    SourceFolders folders, ScanScheduler scans, IFolderPicker picker, IDialogs dialogs) : ViewModelBase
{
    [ObservableProperty]
    private string _problem = string.Empty;

    [ObservableProperty]
    private bool _isEmpty;

    public ObservableCollection<SourceFolderItemViewModel> Folders { get; } = [];

    public async Task LoadAsync()
    {
        var list = await folders.ListAsync();
        Folders.Clear();
        foreach (var folder in list)
            Folders.Add(new SourceFolderItemViewModel(folder, this));
        IsEmpty = Folders.Count == 0;
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        if (await picker.PickFolderAsync(Strings.PickSourceFolderTitle) is not { } path)
            return;
        Problem = await folders.AddAsync(path, includeSubfolders: false) is null
            ? string.Format(Strings.FolderAlreadyAdded, path)
            : string.Empty;
        await RescanAsync();
    }

    // A rescan also brings up the first-scan question for a new folder (MainWindowViewModel).
    [RelayCommand]
    private async Task RescanAsync()
    {
        await scans.RequestAsync(ScanTrigger.Rescan);
        await LoadAsync();
    }

    public async Task SetIncludeSubfoldersAsync(SourceFolderItemViewModel item, bool include)
    {
        await folders.SetIncludeSubfoldersAsync(item.Folder, include);
        await RescanAsync();
    }

    public async Task RemoveAsync(SourceFolderItemViewModel item)
    {
        var clips = await folders.CountClipsAsync(item.Folder);
        // Without clips there is nothing to ask: the folder is removed silently (spec §4).
        var removal = clips == 0 ? FolderRemoval.HideClips : await dialogs.AskFolderRemovalAsync(item.Path, clips);
        if (removal is not { } chosen)
            return;
        await folders.RemoveAsync(item.Folder, chosen);
        await LoadAsync();
    }

    public async Task ResumeAsync(SourceFolderItemViewModel item)
    {
        await folders.ResumeAsync(item.Folder);
        await RescanAsync();
    }
}
