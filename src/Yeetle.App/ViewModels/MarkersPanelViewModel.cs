using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Yeetle.App.Resources;
using Yeetle.Application.Sessions;
using Yeetle.Application.Settings;
using Yeetle.Domain.Clips;

namespace Yeetle.App.ViewModels;

// One row of the markers list: its time, its label (edited in place), and what can be done to it.
public sealed partial class MarkerItemViewModel(Marker marker, ICommand seek, ICommand moveHere, ICommand delete)
    : ViewModelBase
{
    [ObservableProperty]
    private string _label = marker.Label;

    public int Number { get; } = marker.Number;
    public string SavedLabel { get; } = marker.Label;
    public string TimeText { get; } = TimeFormat.Clip(marker.Position);
    public ICommand Seek { get; } = seek;
    public ICommand MoveHere { get; } = moveHere;
    public ICommand Delete { get; } = delete;
}

// A tick on the seek bar; hovering shows the label.
public sealed record MarkerTick(double Fraction, string Tip, ICommand Seek);

// Markers of the current clip (spec §7): K adds "Marker N" a moment before the key press while
// playback goes on, the label can be typed at once, and the list sits under the tags.
public sealed partial class MarkersPanelViewModel : ViewModelBase
{
    private const string TipSeparator = " · ";

    private readonly PlayerViewModel _player;
    private readonly ReviewSettings _settings;
    private ActiveSession? _session;
    private IReadOnlyList<Marker> _shown = [];

    [ObservableProperty]
    private bool _isEmpty = true;

    public MarkersPanelViewModel(PlayerViewModel player, LoadedSettings settings)
    {
        _player = player;
        _settings = settings.Values.Review;
        _player.PropertyChanged += OnPlayerChanged;
    }

    public ObservableCollection<MarkerItemViewModel> Items { get; } = [];

    public ObservableCollection<MarkerTick> Ticks { get; } = [];

    // Asks the view to put the cursor in a marker's label, right after K.
    public event EventHandler<int>? EditRequested;

    public void Attach(ActiveSession? session)
    {
        _session = session;
        Refresh();
    }

    // Called after every change in the session; the list is rebuilt only when its markers changed,
    // so a label being typed is not lost to an unrelated change.
    public void Refresh()
    {
        var markers = _session?.CurrentReview?.Markers ?? [];
        if (markers.SequenceEqual(_shown) && Items.Count == markers.Count)
            return;
        _shown = markers;
        Items.Clear();
        foreach (var marker in markers)
        {
            var number = marker.Number;
            Items.Add(new MarkerItemViewModel(marker,
                new RelayCommand(() => _player.SeekTo(marker.Position)),
                new RelayCommand(() => _session?.MoveMarker(number, _player.Position)),
                new RelayCommand(() => _session?.DeleteMarker(number))));
        }
        IsEmpty = Items.Count == 0;
        RefreshTicks();
    }

    // Enter or leaving the field saves the label; an empty label puts the old one back.
    public void CommitLabel(MarkerItemViewModel item)
    {
        var label = item.Label.Trim();
        if (label.Length == 0 || label == item.SavedLabel)
        {
            item.Label = item.SavedLabel;
            return;
        }
        _session?.RenameMarker(item.Number, label);
    }

    [RelayCommand]
    private void Add()
    {
        if (_session?.CurrentReview is null)
            return;
        var position = _player.Position - TimeSpan.FromSeconds(_settings.MarkerOffsetSeconds);
        var marker = _session.AddMarker(position, number => string.Format(Strings.MarkerDefaultLabel, number));
        EditRequested?.Invoke(this, marker.Number);
    }

    private void OnPlayerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerViewModel.DurationSeconds))
            RefreshTicks();
    }

    private void RefreshTicks()
    {
        Ticks.Clear();
        var duration = _player.DurationSeconds;
        if (duration <= 0)
            return;
        foreach (var marker in _shown)
        {
            var tip = TimeFormat.Clip(marker.Position) + TipSeparator + marker.Label;
            Ticks.Add(new MarkerTick(marker.Position.TotalSeconds / duration, tip,
                new RelayCommand(() => _player.SeekTo(marker.Position))));
        }
    }
}
