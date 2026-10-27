using CommunityToolkit.Mvvm.ComponentModel;
using Yeetle.App.Resources;

namespace Yeetle.App.ViewModels;

public sealed partial class SpeedOptionViewModel(double speed) : ViewModelBase
{
    public double Speed { get; } = speed;

    public string Label { get; } = string.Format(Strings.SpeedFormat, speed);

    [ObservableProperty]
    private bool _isCurrent;
}
