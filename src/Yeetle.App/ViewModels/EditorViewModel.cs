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
    [NotifyCanExecuteChangedFor(nameof(KeepCommand), nameof(YeetCommand), nameof(UndoCommand), nameof(CloseCommand))]
    private bool _isOpen;

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

    // False when the clip is gone from the library.
    public async Task<bool> OpenAsync(ClipId clip)
    {
        await CloseEditorAsync();
        if (await _library.OpenAsync(clip) is not { } editor)
            return false;
        _editor = editor;
        editor.Changed += OnChanged;
        editor.SaveFailed += OnSaveFailed;
        Tags.Attach(editor);
        Markers.Attach(editor);
        ClipName = editor.Clip.FileName;
        ClipDate = string.Format(Strings.ClipDateFormat, editor.Clip.RecordedAt);
        Notice = editor.Clip.State == FileState.Present ? string.Empty : Strings.EditorFileNotPresent;
        _navigator.Load([editor.Clip.FullPath]);
        IsOpen = true;
        OnChanged(editor, EventArgs.Empty);
        return true;
    }

    [RelayCommand(CanExecute = nameof(IsOpen))]
    private void Keep() => _editor?.SetDecision(Decision.Kept);

    [RelayCommand(CanExecute = nameof(IsOpen))]
    private void Yeet() => _editor?.SetDecision(Decision.Yeeted);

    [RelayCommand(CanExecute = nameof(IsOpen))]
    private void Undo() => _editor?.Undo();

    [RelayCommand(CanExecute = nameof(IsOpen))]
    private async Task CloseAsync()
    {
        await CloseEditorAsync();
        Closed?.Invoke(this, EventArgs.Empty);
    }

    // Every change is saved before the player lets go of the file.
    private async Task CloseEditorAsync()
    {
        if (_editor is not { } editor)
            return;
        _editor = null;
        IsOpen = false;
        editor.Changed -= OnChanged;
        editor.SaveFailed -= OnSaveFailed;
        Tags.Attach(null);
        Markers.Attach(null);
        await editor.FlushAsync();
        await _player.ReleaseFilesAsync();
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
