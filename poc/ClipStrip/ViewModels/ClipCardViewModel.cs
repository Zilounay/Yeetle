using Avalonia.Media.Imaging;
using ClipStrip.Ffmpeg;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClipStrip.ViewModels;

// One clip in the strip: its thumbnail, name and recording date, plus how long FFmpeg took.
public sealed partial class ClipCardViewModel(int index, string path) : ObservableObject
{
    private const string RecordedFormat = "yyyy-MM-dd HH:mm";

    public int Index { get; } = index;

    public string Path { get; } = path;

    public string FileName { get; } = System.IO.Path.GetFileNameWithoutExtension(path);

    // Set once loading starts, so a card is only ever loaded once.
    public bool IsRequested { get; set; }

    [ObservableProperty]
    private bool _isCurrent;

    [ObservableProperty]
    private Bitmap? _thumbnail;

    [ObservableProperty]
    private string _recorded = string.Empty;

    // Where the date came from: shown so the POC answers "does the file carry it?" at a glance.
    [ObservableProperty]
    private bool _isDateFromFile;

    [ObservableProperty]
    private string _status = "…";

    [ObservableProperty]
    private bool _isFailed;

    public void ShowInfo(ClipInfo info)
    {
        Recorded = info.RecordedUtc.ToLocalTime().ToString(RecordedFormat);
        IsDateFromFile = info.Source == RecordingTimeSource.File;
    }

    public void ShowLoaded(Bitmap thumbnail, TimeSpan probe, TimeSpan frame)
    {
        Thumbnail = thumbnail;
        Status = $"probe {probe.TotalMilliseconds:0} · frame {frame.TotalMilliseconds:0} ms";
    }

    public void ShowError(string message)
    {
        IsFailed = true;
        Status = message;
    }
}
