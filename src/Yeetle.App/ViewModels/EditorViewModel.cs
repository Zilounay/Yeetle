using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Yeetle.App.Input;
using Yeetle.App.Resources;
using Yeetle.Application.Library;
using Yeetle.Application.Playback;
using Yeetle.Application.Settings;
using Yeetle.Application.Tagging;
using Yeetle.Domain.Clips;

namespace Yeetle.App.ViewModels;

// A clip opened from the Explorer (spec §8): the player without clip strip or Skip. Its decision,
// tags and markers are saved at once, with undo; Esc or Back to library returns to the list.
public sealed partial class EditorViewModel : ViewModelBase
{
    private readonly ClipLibrary _library;
    private readonly ClipNavigator _navigator;
    private readonly PlayerViewModel _player;
    private readonly TagManager _tagManager;
    private ClipEditor? _editor;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CloseCommand))]
    private bool _isOpen;

    // A clip whose file is not on disk opens with its name and why it cannot play, and nothing to
    // play, decide or tag (#79).
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(KeepCommand), nameof(YeetCommand), nameof(UndoCommand))]
    private bool _isFilePresent;

    [ObservableProperty]
    private string _clipName = string.Empty;

    [ObservableProperty]
    private string _clipDate = string.Empty;

    [ObservableProperty]
    private bool _isKept;

    [ObservableProperty]
    private bool _isYeeted;

    [ObservableProperty]
    private string _notice = string.Empty;

    public EditorViewModel(ClipLibrary library, ClipNavigator navigator, PlayerViewModel player, TagManager tagManager,
        TagPanelViewModel tags, MarkersPanelViewModel markers)
    {
        _library = library;
        _navigator = navigator;
        _player = player;
        _tagManager = tagManager;
        Tags = tags;
        Markers = markers;
        _tagManager.Changed += (_, _) => _ = ReloadTagsAsync();
        Actions = new Dictionary<string, ICommand>
        {
            [HotkeyActions.Keep] = KeepCommand,
            [HotkeyActions.Yeet] = YeetCommand,
            [HotkeyActions.Undo] = UndoCommand,
            [HotkeyActions.StopSession] = CloseCommand,
            [HotkeyActions.TypeTag] = tags.OpenTypeTagCommand,
            [HotkeyActions.AddMarker] = markers.AddCommand,
        };
    }

    public TagPanelViewModel Tags { get; }

    public MarkersPanelViewModel Markers { get; }

    // What each hotkey action does while a clip is open, by its name in the settings file.
    public IReadOnlyDictionary<string, ICommand> Actions { get; }

    // Raised when the editor closes, so the window can show the list again.
    public event EventHandler? Closed;

    // Done once the last clip's changes are saved and its file let go. Closing does that behind the
    // page switch, so the next page shows at once (#78).
    public Task Closing { get; private set; } = Task.CompletedTask;

    // Leaving for another page: closes without raising Closed.
    public Task LeaveAsync() => CloseEditorAsync();

    // False when the clip is gone from the library.
    public async Task<bool> OpenAsync(ClipId clip)
    {
        await CloseEditorAsync();
        if (await _library.OpenAsync(clip) is not { } editor)
            return false;
        _editor = editor;
        editor.Changed += OnChanged;
        editor.SaveFailed += OnSaveFailed;
        var present = editor.Clip.State == FileState.Present;
        Tags.Attach(present ? editor : null);
        Markers.Attach(present ? editor : null);
        ClipName = editor.Clip.FileName;
        ClipDate = string.Format(Strings.ClipDateFormat, editor.Clip.RecordedAt);
        Notice = present ? string.Empty : Strings.EditorFileNotPresent;
        _navigator.Load(present ? [editor.Clip.FullPath] : []);
        IsFilePresent = present;
        IsOpen = true;
        OnChanged(editor, EventArgs.Empty);
        return true;
    }

    [RelayCommand(CanExecute = nameof(IsFilePresent))]
    private void Keep() => _editor?.SetDecision(Decision.Kept);

    [RelayCommand(CanExecute = nameof(IsFilePresent))]
    private void Yeet() => _editor?.SetDecision(Decision.Yeeted);

    [RelayCommand(CanExecute = nameof(IsFilePresent))]
    private void Undo() => _editor?.Undo();

    [RelayCommand(CanExecute = nameof(IsOpen))]
    private void Close()
    {
        _ = CloseEditorAsync();
        Closed?.Invoke(this, EventArgs.Empty);
    }

    // The screen lets go of the clip at once; saving and releasing the file follow in the
    // background, and Closing tells when they are done.
    private Task CloseEditorAsync()
    {
        if (_editor is not { } editor)
            return Closing;
        _editor = null;
        IsOpen = false;
        IsFilePresent = false;
        editor.Changed -= OnChanged;
        editor.SaveFailed -= OnSaveFailed;
        Tags.Attach(null);
        Markers.Attach(null);
        return Closing = FinishClosingAsync(Closing, editor);
    }

    // The player lets go of the file while the last changes are saved.
    private async Task FinishClosingAsync(Task previous, ClipEditor editor)
    {
        await previous;
        var release = _player.ReleaseFilesAsync();
        await editor.FlushAsync();
        await release;
    }

    private async Task ReloadTagsAsync()
    {
        if (_editor is null)
            return;
        var catalog = await _tagManager.LoadAsync();
        if (_editor is not { } editor)
            return;
        editor.UseCatalog(catalog);
        Tags.Attach(editor);
    }

    private void OnChanged(object? sender, EventArgs e)
    {
        if (_editor is not { } editor)
            return;
        IsKept = editor.CurrentReview.Decision == Decision.Kept;
        IsYeeted = editor.CurrentReview.Decision == Decision.Yeeted;
        Tags.ClearRequiredWarning();
        Tags.Refresh();
        Markers.Refresh();
    }

    private void OnSaveFailed(object? sender, string reason) => Notice = string.Format(Strings.EditorSaveFailed, reason);
}
