using System.Diagnostics;
using Avalonia.Media.Imaging;
using ClipStrip.Ffmpeg;
using CommunityToolkit.Mvvm.ComponentModel;
using Yeetle.Application.Playback;

namespace ClipStrip.ViewModels;

// The thumbnail strip under the video (spec §5): every clip of the folder, the current one
// highlighted. Thumbnails are made lazily, for the cards on screen and the clips around the
// current one, a few at a time in the background.
public sealed partial class ClipStripViewModel : ObservableObject
{
    // Spec §5 shows 2 previous and 2 next clips; they are always loaded, even if scrolled away.
    private const int Neighbours = 2;
    // FFmpeg runs this many at once: enough to fill the strip fast without starving playback.
    private const int MaxParallel = 2;

    private readonly ClipNavigator _navigator;
    private readonly MediaTools _tools;
    private readonly SemaphoreSlim _slots = new(MaxParallel);
    private readonly Timings _probeTimes = new();
    private readonly Timings _frameTimes = new();
    private IReadOnlyList<string>? _loadedClips;
    private CancellationTokenSource _loading = new();
    private int _failures;

    [ObservableProperty]
    private IReadOnlyList<ClipCardViewModel> _cards = [];

    [ObservableProperty]
    private int _currentIndex = ClipNavigator.NoClip;

    [ObservableProperty]
    private string _summary = "Open a folder to see its clips.";

    public ClipStripViewModel(ClipNavigator navigator, MediaTools tools)
    {
        _navigator = navigator;
        _tools = tools;
        _navigator.CurrentChanged += (_, _) => OnCurrentChanged();
    }

    // Called by the view when a card comes on screen.
    public void Request(int index)
    {
        if (index >= 0 && index < Cards.Count)
            _ = LoadAsync(Cards[index], _loading.Token);
    }

    private void OnCurrentChanged()
    {
        if (!ReferenceEquals(_loadedClips, _navigator.Clips))
            Reload(_navigator.Clips);

        if (CurrentIndex >= 0 && CurrentIndex < Cards.Count)
            Cards[CurrentIndex].IsCurrent = false;
        CurrentIndex = _navigator.Index;
        if (CurrentIndex == ClipNavigator.NoClip)
            return;
        Cards[CurrentIndex].IsCurrent = true;
        // The current clip first, then outwards.
        Request(CurrentIndex);
        for (var distance = 1; distance <= Neighbours; distance++)
        {
            Request(CurrentIndex + distance);
            Request(CurrentIndex - distance);
        }
    }

    private void Reload(IReadOnlyList<string> clips)
    {
        _loading.Cancel();
        _loading = new CancellationTokenSource();
        _loadedClips = clips;
        _probeTimes.Clear();
        _frameTimes.Clear();
        _failures = 0;
        CurrentIndex = ClipNavigator.NoClip;
        Cards = clips.Select((path, i) => new ClipCardViewModel(i, path)).ToList();
        UpdateSummary();
    }

    private async Task LoadAsync(ClipCardViewModel card, CancellationToken cancellationToken)
    {
        if (card.IsRequested)
            return;
        card.IsRequested = true;
        try
        {
            await _slots.WaitAsync(cancellationToken);
            try
            {
                var clock = Stopwatch.StartNew();
                var info = await Task.Run(() => _tools.ProbeAsync(card.Path, cancellationToken), cancellationToken);
                var probeTime = clock.Elapsed;
                card.ShowInfo(info);

                clock.Restart();
                var thumbnail = await Task.Run(async () =>
                {
                    var jpeg = await _tools.ThumbnailAsync(card.Path, info.Duration, cancellationToken);
                    return new Bitmap(new MemoryStream(jpeg));
                }, cancellationToken);
                var frameTime = clock.Elapsed;

                card.ShowLoaded(thumbnail, probeTime, frameTime);
                _probeTimes.Add(probeTime);
                _frameTimes.Add(frameTime);
            }
            finally
            {
                _slots.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // Another folder was opened; this card is gone.
            return;
        }
        catch (Exception e) when (e is MediaToolException or IOException or UnauthorizedAccessException
                                      or System.Text.Json.JsonException)
        {
            card.ShowError(e.Message);
            _failures++;
        }
        UpdateSummary();
    }

    private void UpdateSummary() =>
        Summary = $"{_frameTimes.Count} / {Cards.Count} thumbnails"
                  + (_failures > 0 ? $" · {_failures} failed" : string.Empty)
                  + $"   ·   ffprobe: {_probeTimes.Summary()}   ·   ffmpeg frame: {_frameTimes.Summary()}";
}
