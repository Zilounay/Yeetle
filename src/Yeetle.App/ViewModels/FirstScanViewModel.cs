using CommunityToolkit.Mvvm.ComponentModel;
using Yeetle.App.Resources;
using Yeetle.Application.Library;

namespace Yeetle.App.ViewModels;

// The first-scan question for a folder that already holds clips (spec §4).
public sealed partial class FirstScanViewModel(PendingFirstScan pending) : ViewModelBase
{
    [ObservableProperty]
    private bool _reviewAll = true;

    [ObservableProperty]
    private bool _keepAll;

    [ObservableProperty]
    private bool _reviewFrom;

    // Starts at the newest clip's day, so by default only that day is reviewed. DateTime, as
    // CalendarDatePicker.SelectedDate is one in Avalonia 12.
    [ObservableProperty]
    private DateTime? _fromDate = pending.Newest.Date;

    public string Message { get; } = string.Format(Strings.FirstScanMessage,
        pending.ClipCount, pending.Folder.Path, pending.Oldest, pending.Newest);

    public FirstScanChoice Choice =>
        KeepAll ? new FirstScanChoice(FirstScanKind.KeepAll)
        : ReviewFrom && FromDate is { } from ? new FirstScanChoice(FirstScanKind.ReviewFrom, from.Date)
        : new FirstScanChoice(FirstScanKind.ReviewAll);
}
