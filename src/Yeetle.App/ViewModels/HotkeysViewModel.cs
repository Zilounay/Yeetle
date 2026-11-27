using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Yeetle.App.Input;
using Yeetle.App.Resources;
using Yeetle.App.Services;
using Yeetle.Application.Playback;
using Yeetle.Application.Settings;
using Yeetle.Application.Tagging;

namespace Yeetle.App.ViewModels;

// One key of an action, with its × button.
public sealed record HotkeyKeyViewModel(string Text, string Label, IRelayCommand Remove);

// One action of the settings file and its keys.
public sealed partial class HotkeyActionViewModel(string action, string label) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoKey))]
    private IReadOnlyList<HotkeyKeyViewModel> _keys = [];

    [ObservableProperty]
    private bool _isCapturing;

    public string Action { get; } = action;
    [ObservableProperty]
    private string _label = label;
    public bool HasNoKey => Keys.Count == 0;
}

public sealed record TagHotkeyViewModel(string Tag, string Key);

// Settings › Hotkeys (spec §11, §14, #20): every action's keys, one key one action, changes at once.
// Tag hotkeys are set in the Tag Manager and only listed here, but no action may take one of them.
public sealed partial class HotkeysViewModel : ViewModelBase
{
    private readonly SettingsService _settings;
    private readonly TagManager _tags;
    private readonly IDialogs _dialogs;
    private IReadOnlyDictionary<Domain.Tagging.TagId, string> _tagKeys = new Dictionary<Domain.Tagging.TagId, string>();
    private Domain.Tagging.TagVocabulary _vocabulary = Domain.Tagging.TagVocabulary.Empty;

    [ObservableProperty]
    private HotkeyActionViewModel? _capturing;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<TagHotkeyViewModel> _tagHotkeys = [];

    public HotkeysViewModel(SettingsService settings, TagManager tags, IDialogs dialogs)
    {
        _settings = settings;
        _tags = tags;
        _dialogs = dialogs;
        // In the defaults' order (Keep, Yeet first), whatever order the file has.
        Actions = HotkeyDefaults.Create().Keys.Where(settings.Values.Hotkeys.ContainsKey)
            .Select(a => new HotkeyActionViewModel(a, LabelOf(a))).ToList();
        Fill();
    }

    public IReadOnlyList<HotkeyActionViewModel> Actions { get; }

    public bool IsCapturing => Capturing is not null;

    // Tag keys are read again whenever the section is shown: the Tag Manager may have changed them.
    public async Task LoadTagKeysAsync()
    {
        var catalog = await _tags.LoadAsync();
        _vocabulary = catalog.Vocabulary;
        _tagKeys = catalog.Hotkeys;
        TagHotkeys = catalog.Hotkeys.Select(h => new TagHotkeyViewModel(_vocabulary.Tag(h.Key).Name, HotkeyLabels.Short(h.Value)))
            .OrderBy(t => t.Tag, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    // Called by the window for each key pressed while one is awaited. A modifier alone waits for
    // the real key; Esc cancels.
    public void Capture(KeyPress press)
    {
        if (Capturing is not { } row)
            return;
        if (press.Position == Avalonia.Input.PhysicalKey.Escape)
        {
            StopCapture();
            return;
        }
        if (HotkeyNames.FromKey(press.Modifiers, press.Position) is not { } text)
            return;
        var shown = HotkeyLabels.Short(text);
        if (ActionUsing(press) is { } other)
        {
            Message = string.Format(Strings.KeyTakenByAction, shown, LabelOf(other));
            StopCapture(keepMessage: true);
            return;
        }
        if (_tagKeys.FirstOrDefault(t => Hotkey.Parse(t.Value) is { } key && key.Matches(press)) is { Value: not null } tag)
        {
            Message = string.Format(Strings.KeyTakenByTag, shown, _vocabulary.Tag(tag.Key).Name);
            StopCapture(keepMessage: true);
            return;
        }
        StopCapture();
        _ = _settings.ChangeAsync(s => s.Hotkeys[row.Action] = [.. s.Hotkeys[row.Action], text]);
        Fill();
    }

    // After a language switch (#20).
    public void Relocalize()
    {
        foreach (var row in Actions)
            row.Label = LabelOf(row.Action);
        Fill();
        Message = string.Empty;
    }

    [RelayCommand]
    private void StartCapture(HotkeyActionViewModel row)
    {
        StopCapture();
        Capturing = row;
        row.IsCapturing = true;
        Message = string.Format(Strings.PressKeyFor, row.Label);
        OnPropertyChanged(nameof(IsCapturing));
    }

    [RelayCommand]
    private async Task RestoreDefaultsAsync()
    {
        var answer = await _dialogs.ChooseAsync(Strings.RestoreDefaultKeys, Strings.RestoreDefaultKeysMessage,
            [(Strings.RestoreDefaultKeys, true)]);
        if (answer != true)
            return;
        await _settings.ChangeAsync(s => s.Hotkeys = HotkeyDefaults.Create());
        Fill();
    }

    [RelayCommand]
    private void OpenTagManager() => _dialogs.ShowTagManager();

    private void StopCapture(bool keepMessage = false)
    {
        if (Capturing is { } row)
            row.IsCapturing = false;
        Capturing = null;
        if (!keepMessage)
            Message = string.Empty;
        OnPropertyChanged(nameof(IsCapturing));
    }

    // The action, if any, that a key already triggers (spec §14: one key, one action).
    private string? ActionUsing(KeyPress press) => _settings.Values.Hotkeys
        .FirstOrDefault(a => a.Value.Any(k => Hotkey.Parse(k) is { } key && key.Matches(press))).Key;

    private void Fill()
    {
        foreach (var row in Actions)
        {
            var keys = _settings.Values.Hotkeys.GetValueOrDefault(row.Action) ?? [];
            row.Keys = keys.Select(k => new HotkeyKeyViewModel(k, HotkeyLabels.Short(k),
                new RelayCommand(() => Remove(row.Action, k)))).ToList();
        }
    }

    private void Remove(string action, string key)
    {
        _ = _settings.ChangeAsync(s => s.Hotkeys[action] = s.Hotkeys[action].Where(k => k != key).ToList());
        Fill();
    }

    private static string LabelOf(string action) => action switch
    {
        HotkeyActions.Keep => Strings.ActionKeep,
        HotkeyActions.Yeet => Strings.ActionYeet,
        HotkeyActions.Rescan => Strings.ActionRescan,
        HotkeyActions.Undo => Strings.ActionUndo,
        HotkeyActions.StopSession => Strings.ActionStopSession,
        HotkeyActions.TypeTag => Strings.ActionTypeTag,
        HotkeyActions.AddMarker => Strings.ActionAddMarker,
        HotkeyActions.TogglePause => Strings.ActionTogglePause,
        HotkeyActions.NextClip => Strings.ActionNextClip,
        HotkeyActions.PreviousClip => Strings.ActionPreviousClip,
        HotkeyActions.SeekForward => Strings.ActionSeekForward,
        HotkeyActions.SeekBack => Strings.ActionSeekBack,
        HotkeyActions.LongSeekForward => Strings.ActionLongSeekForward,
        HotkeyActions.LongSeekBack => Strings.ActionLongSeekBack,
        HotkeyActions.VolumeUp => Strings.ActionVolumeUp,
        HotkeyActions.VolumeDown => Strings.ActionVolumeDown,
        HotkeyActions.Mute => Strings.ActionMute,
        HotkeyActions.NextAudioTrack => Strings.ActionNextAudioTrack,
        HotkeyActions.Slower => Strings.ActionSlower,
        HotkeyActions.Faster => Strings.ActionFaster,
        HotkeyActions.NormalSpeed => Strings.ActionNormalSpeed,
        HotkeyActions.NextFrame => Strings.ActionNextFrame,
        HotkeyActions.TheatreMode => Strings.ActionTheatreMode,
        HotkeyActions.MarkerWindow => Strings.ActionMarkerWindow,
        HotkeyActions.Settings => Strings.ActionSettings,
        _ => SpeedLabel(action) ?? action,
    };

    // "Speed3" is the third speed of PlaybackSpeeds.All: "Speed 1.5x".
    private static string? SpeedLabel(string action) =>
        PlaybackSpeeds.Numbered.FirstOrDefault(s => HotkeyActions.Speed(s.Number) == action) is { Number: > 0 } speed
            ? string.Format(Strings.ActionSpeedFormat, string.Format(Strings.SpeedFormat, speed.Speed))
            : null;
}
