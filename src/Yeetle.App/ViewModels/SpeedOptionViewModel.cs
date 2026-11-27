using CommunityToolkit.Mvvm.ComponentModel;
using Yeetle.App.Resources;

namespace Yeetle.App.ViewModels;

public sealed partial class SpeedOptionViewModel(double speed) : ViewModelBase
{
    public double Speed { get; } = speed;

    public string Label => string.Format(Strings.SpeedFormat, Speed);

    // After a language switch (#20).
    public void Relocalize() => OnPropertyChanged(nameof(Label));

    [ObservableProperty]
    private bool _isCurrent;
}
