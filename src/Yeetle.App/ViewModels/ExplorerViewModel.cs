using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Yeetle.App.Resources;
using Yeetle.App.Services;
using Yeetle.Application.Files;
using Yeetle.Application.Library;
using Yeetle.Application.Sessions;
using Yeetle.Application.Shell;
using Yeetle.Domain.Clips;
using Yeetle.Domain.Library;
using Yeetle.Domain.Tagging;

namespace Yeetle.App.ViewModels;

// One row of the clip list: name, recording date, duration and tags; Missing, Offline and Deleted
// clips are dimmed with a badge (spec §8, layout from #9).
public sealed class ExplorerRowViewModel(LibraryClip clip, string tags)
{
    public LibraryClip Clip { get; } = clip;
    public string Name { get; } = clip.FileName;
    public string Date { get; } = string.Format(Strings.ClipDateFormat, clip.RecordedAt);
    public string Duration { get; } = clip.Duration is { } duration ? TimeFormat.Clip(duration) : Strings.UnknownDuration;
    public string Tags { get; } = tags;
    public string StateBadge { get; } = clip.State switch
    {
        FileState.Missing => Strings.StateMissing,
        FileState.Offline => Strings.StateOffline,
        FileState.Deleted => Strings.StateDeleted,
        FileState.NeedsAttention => Strings.StateNeedsAttention,
        _ => string.Empty,
    };
    public bool HasStateBadge => StateBadge.Length > 0;
    public bool IsDimmed => clip.State != FileState.Present;
    // Shown when New or Yeeted clips are listed, so they stand out from Kept ones.
    public string DecisionBadge { get; } = clip.Decision switch
    {
        Decision.New => Strings.DecisionNew,
        Decision.Yeeted => Strings.DecisionYeeted,
        _ => string.Empty,
    };
    public bool HasDecisionBadge => DecisionBadge.Length > 0;
}

// A line of the tag filter: a box title, or a tag to tick. Boxes under a tag are indented.
public sealed partial class TagFilterItem(string label, int level, TagId? tag) : ObservableObject
{
    private const double IndentPerLevel = 16;

    [ObservableProperty]
    private bool _isChecked;

    public string Label { get; } = label;
    public TagId? Tag { get; } = tag;
    public bool IsTag => Tag is not null;
    public bool IsHeader => Tag is null;
    public Avalonia.Thickness Indent { get; } = new(level * IndentPerLevel, 0, 0, 0);
}

// The Explorer (spec §8): every clip of the library, filtered, sorted and acted on.
public sealed partial class ExplorerViewModel : ViewModelBase
{
    private const string TagSeparator = ", ";
    private const string SortUp = " ▲";
    private const string SortDown = " ▼";

    private readonly ClipLibrary _library;
    private readonly FileOperations _files;
    private readonly PlayerViewModel _player;
    private readonly IDialogs _dialogs;
    private readonly IShell _shell;
    private IReadOnlyList<LibraryClip> _all = [];
    private TagCatalog _catalog = TagCatalog.Empty;
    private IReadOnlyList<ExplorerRowViewModel> _selected = [];
    // Set while the filters are filled in, so each change does not query again.
    private bool _loading;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _showKept = true;

    [ObservableProperty]
    private bool _showNew;

    [ObservableProperty]
    private bool _showYeeted;

    [ObservableProperty]
    private bool _showPresent = true;

    [ObservableProperty]
    private bool _showMissing = true;

    [ObservableProperty]
    private bool _showOffline = true;

    [ObservableProperty]
    private bool _showDeleted;

    [ObservableProperty]
    private bool _showNeedsAttention = true;

    [ObservableProperty]
    private bool _incompleteOnly;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameHeader), nameof(DateHeader), nameof(DurationHeader))]
    private LibrarySort _sort = LibrarySort.RecordedAt;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameHeader), nameof(DateHeader), nameof(DurationHeader))]
    private bool _descending = true;

    [ObservableProperty]
    private string _matchText = string.Empty;

    [ObservableProperty]
    private string _selectionText = string.Empty;

    [ObservableProperty]
    private string _notice = string.Empty;

    [ObservableProperty]
    private bool _isEmpty;

    public ExplorerViewModel(ClipLibrary library, FileOperations files, PlayerViewModel player, IDialogs dialogs, IShell shell)
    {
        _library = library;
        _files = files;
        _player = player;
        _dialogs = dialogs;
        _shell = shell;
        PropertyChanged += OnFilterChanged;
    }

    public ObservableCollection<ExplorerRowViewModel> Rows { get; } = [];

    public ObservableCollection<TagFilterItem> TagFilters { get; } = [];

    public bool HasTagFilters => TagFilters.Any(f => f.IsTag);

    public string NameHeader => Strings.ColumnName + Arrow(LibrarySort.Name);
    public string DateHeader => Strings.ColumnRecorded + Arrow(LibrarySort.RecordedAt);
    public string DurationHeader => Strings.ColumnDuration + Arrow(LibrarySort.Duration);

    public bool HasSelection => _selected.Count > 0;

    public bool HasSingleSelection => _selected.Count == 1;

    // Yeetle's own entries on top of the Windows context menu, in the order RunMenuEntryAsync expects.
    public IReadOnlyList<string> MenuEntries { get; } =
    [
        Strings.OpenInYeetle, Strings.ShowMarkers, Strings.SetKept, Strings.SetYeeted, Strings.RenameByTags,
        Strings.RestoreNames, Strings.WipeData, Strings.DeleteFromDisk,
    ];

    // Raised to open a clip in the editor (spec §8).
    public event EventHandler<ClipId>? OpenRequested;

    // Loads the library again, keeping the filters; called when the Explorer is shown and after
    // every action.
    public async Task LoadAsync()
    {
        _all = await _library.LoadAsync();
        var catalog = await _library.LoadCatalogAsync();
        if (!ReferenceEquals(catalog.Vocabulary, _catalog.Vocabulary))
            FillTagFilters(catalog);
        _catalog = catalog;
        Apply();
    }

    // The view hands over the list's selection whenever it changes.
    public void SetSelection(IEnumerable<object> selected)
    {
        _selected = selected.OfType<ExplorerRowViewModel>().ToList();
        SelectionText = _selected.Count == 0 ? string.Empty : string.Format(Strings.SelectedCount, _selected.Count);
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasSingleSelection));
        foreach (var command in new IRelayCommand[]
                 {
                     OpenCommand, ShowInFolderCommand, OpenWithDefaultAppCommand, SetKeptCommand, SetYeetedCommand,
                     ShowMarkersCommand, RenameByTagsCommand, RestoreNamesCommand, WipeCommand, DeleteCommand,
                 })
            command.NotifyCanExecuteChanged();
    }

    // The files a drag takes (spec §8): Missing, Offline and Deleted clips are left out with a notice.
    public IReadOnlyList<string> PathsToDrag()
    {
        var present = _selected.Where(r => r.Clip.State == FileState.Present).Select(r => r.Clip.FullPath).ToList();
        var leftOut = _selected.Count - present.Count;
        Notice = leftOut > 0 ? string.Format(Strings.DragLeftOut, leftOut) : string.Empty;
        return present;
    }

    // Right-click: Windows' own menu for the selected files, Yeetle's entries on top (spec §8).
    // Files that are not on disk get Yeetle's entries only.
    public async Task ShowMenuAsync(nint window, int screenX, int screenY)
    {
        var paths = _selected.All(r => r.Clip.State == FileState.Present) ? _selected.Select(r => r.Clip.FullPath).ToList() : [];
        if (_shell.ShowFileMenu(window, screenX, screenY, paths, MenuEntries) is { } entry)
            await RunMenuEntryAsync(entry);
    }

    [RelayCommand]
    private void SortBy(LibrarySort column)
    {
        // The same column again turns the order around; dates start newest first.
        Descending = column == Sort ? !Descending : column != LibrarySort.Name;
        Sort = column;
    }

    [RelayCommand]
    private void ClearFilters()
    {
        _loading = true;
        foreach (var filter in TagFilters)
            filter.IsChecked = false;
        (ShowKept, ShowNew, ShowYeeted) = (true, false, false);
        (ShowPresent, ShowMissing, ShowOffline, ShowDeleted, ShowNeedsAttention) = (true, true, true, false, true);
        IncompleteOnly = false;
        SearchText = string.Empty;
        _loading = false;
        Apply();
    }

    [RelayCommand(CanExecute = nameof(HasSingleSelection))]
    private void Open() => OpenRequested?.Invoke(this, _selected[0].Clip.Id);

    [RelayCommand(CanExecute = nameof(HasSingleSelection))]
    private void ShowInFolder() => _shell.ShowInFolder(_selected[0].Clip.FullPath);

    [RelayCommand(CanExecute = nameof(HasSingleSelection))]
    private void OpenWithDefaultApp() => _shell.OpenWithDefaultApp(_selected[0].Clip.FullPath);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private Task SetKeptAsync() => SetDecisionAsync(Decision.Kept);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private Task SetYeetedAsync() => SetDecisionAsync(Decision.Yeeted);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private Task ShowMarkersAsync() => _dialogs.ShowMarkerSheetAsync(
        MarkerSheet.Write(_selected.Select(r => r.Clip), _catalog.Vocabulary, Strings.MarkerSheetTags, Strings.MarkerSheetMarkers));

    // Only kept clips are ever renamed (spec §5).
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task RenameByTagsAsync()
    {
        var kept = _selected.Where(r => r.Clip.Decision == Decision.Kept).Select(r => r.Clip.Id).ToList();
        if (kept.Count == 0)
        {
            Notice = Strings.RenameOnlyKept;
            return;
        }
        await RunFileOperationAsync(async () =>
        {
            var preview = await _files.PlanRenameAsync(kept);
            if (preview.ToRename.Count == 0)
            {
                Notice = Strings.NothingToRename;
                return;
            }
            if (!await _dialogs.ConfirmRenameAsync(preview, fromSession: false))
                return;
            var outcomes = await _files.RenameAsync(preview);
            var renamed = outcomes.Where(o => o.Succeeded).Select(o => o.Clip).ToList();
            Notice = string.Format(Strings.FilesRenamed, renamed.Count);
            if (await _dialogs.ShowFileResultsAsync(Strings.RenameResultsTitle, outcomes, canRestoreNames: renamed.Count > 0))
                await RestoreAsync(renamed);
        });
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private Task RestoreNamesAsync() => RunFileOperationAsync(() => RestoreAsync(_selected.Select(r => r.Clip.Id).ToList()));

    // Asked with the count, after a backup (spec §8).
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task WipeAsync()
    {
        var clips = _selected.Select(r => r.Clip.Id).ToList();
        var answer = await _dialogs.ChooseAsync(Strings.WipeTitle, string.Format(Strings.WipeMessage, clips.Count),
            [(Strings.WipeData, true)]);
        if (answer != true)
            return;
        await _library.WipeAsync(clips);
        Notice = string.Format(Strings.ClipsWiped, clips.Count);
        await LoadAsync();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private Task DeleteAsync() => RunFileOperationAsync(async () =>
    {
        var plan = await _files.PlanDeleteAsync(_selected.Select(r => r.Clip.Id).ToList());
        if (plan.Items.Count == 0)
        {
            Notice = Strings.NothingToDelete;
            return;
        }
        if (!await _dialogs.ConfirmDeleteAsync(plan, fromSession: false))
            return;
        var outcomes = await _files.DeleteAsync(plan);
        if (outcomes.Any(o => !o.Succeeded))
            await _dialogs.ShowFileResultsAsync(Strings.DeleteResultsTitle, outcomes, canRestoreNames: false);
        Notice = string.Format(Strings.FilesDeleted, outcomes.Count(o => o.Succeeded));
    });

    private async Task RunMenuEntryAsync(int entry)
    {
        var command = entry switch
        {
            0 => OpenCommand,
            1 => ShowMarkersCommand,
            2 => SetKeptCommand,
            3 => SetYeetedCommand,
            4 => RenameByTagsCommand,
            5 => RestoreNamesCommand,
            6 => WipeCommand,
            _ => (System.Windows.Input.ICommand)DeleteCommand,
        };
        if (command is IAsyncRelayCommand asyncCommand && asyncCommand.CanExecute(null))
            await asyncCommand.ExecuteAsync(null);
        else if (command.CanExecute(null))
            command.Execute(null);
    }

    private async Task SetDecisionAsync(Decision decision)
    {
        var clips = _selected.Select(r => r.Clip.Id).ToList();
        await _library.SetDecisionAsync(clips, decision);
        Notice = string.Format(decision == Decision.Kept ? Strings.ClipsSetKept : Strings.ClipsSetYeeted, clips.Count);
        await LoadAsync();
    }

    private async Task RestoreAsync(IReadOnlyCollection<ClipId> clips)
    {
        var restored = await _files.RestoreNamesAsync(clips);
        if (restored.Count == 0)
        {
            Notice = Strings.NoNamesToRestore;
            return;
        }
        await _dialogs.ShowFileResultsAsync(Strings.RestoreResultsTitle, restored, canRestoreNames: false);
        Notice = string.Format(Strings.NamesRestored, restored.Count(o => o.Succeeded));
    }

    // The player lets go of its files first: an opened clip would otherwise be in use.
    private async Task RunFileOperationAsync(Func<Task> operation)
    {
        try
        {
            await _player.ReleaseFilesAsync();
            await operation();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException
                                      or System.Data.Common.DbException)
        {
            Notice = string.Format(Strings.FileOperationsFailed, e.Message);
        }
        await LoadAsync();
    }

    private void FillTagFilters(TagCatalog catalog)
    {
        var checkedTags = TagFilters.Where(f => f.IsChecked).Select(f => f.Tag).ToHashSet();
        foreach (var filter in TagFilters)
            filter.PropertyChanged -= OnTagFilterChanged;
        TagFilters.Clear();
        var vocabulary = catalog.Vocabulary;
        AddBoxes(null, level: 0);
        foreach (var filter in TagFilters)
        {
            filter.IsChecked = filter.Tag is { } tag && checkedTags.Contains(tag);
            filter.PropertyChanged += OnTagFilterChanged;
        }
        OnPropertyChanged(nameof(HasTagFilters));

        void AddBoxes(TagId? parent, int level)
        {
            foreach (var box in vocabulary.BoxesUnder(parent))
            {
                if (box.IsNamed)
                    TagFilters.Add(new TagFilterItem(box.Name, level, null));
                foreach (var tag in vocabulary.TagsIn(box.Id))
                {
                    TagFilters.Add(new TagFilterItem(tag.Name, level, tag.Id));
                    AddBoxes(tag.Id, level + 1);
                }
            }
        }
    }

    private void OnTagFilterChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TagFilterItem.IsChecked) && !_loading)
            Apply();
    }

    private void OnFilterChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_loading && e.PropertyName is nameof(SearchText) or nameof(ShowKept) or nameof(ShowNew) or nameof(ShowYeeted)
                or nameof(ShowPresent) or nameof(ShowMissing) or nameof(ShowOffline) or nameof(ShowDeleted)
                or nameof(ShowNeedsAttention) or nameof(IncompleteOnly) or nameof(Sort) or nameof(Descending))
            Apply();
    }

    private void Apply()
    {
        var decisions = new HashSet<Decision>();
        if (ShowKept)
            decisions.Add(Decision.Kept);
        if (ShowNew)
            decisions.Add(Decision.New);
        if (ShowYeeted)
            decisions.Add(Decision.Yeeted);
        var states = new HashSet<FileState>();
        if (ShowPresent)
            states.Add(FileState.Present);
        if (ShowMissing)
            states.Add(FileState.Missing);
        if (ShowOffline)
            states.Add(FileState.Offline);
        if (ShowDeleted)
            states.Add(FileState.Deleted);
        if (ShowNeedsAttention)
            states.Add(FileState.NeedsAttention);
        var tags = TagFilters.Where(f => f.IsChecked).Select(f => f.Tag).OfType<TagId>().ToList();
        var filter = new LibraryFilter(decisions, states, tags, SearchText, IncompleteOnly);
        var vocabulary = _catalog.Vocabulary;
        var shown = LibraryQuery.Apply(_all, filter, vocabulary, Sort, Descending);
        Rows.Clear();
        foreach (var clip in shown)
            Rows.Add(new ExplorerRowViewModel(clip, string.Join(TagSeparator,
                vocabulary.InManagerOrder.Where(clip.Tags.Contains).Select(t => vocabulary.Tag(t).Name))));
        MatchText = string.Format(Strings.MatchCount, shown.Count, _all.Count);
        IsEmpty = shown.Count == 0;
        SetSelection([]);
    }

    private string Arrow(LibrarySort column) => column != Sort ? string.Empty : Descending ? SortDown : SortUp;
}
