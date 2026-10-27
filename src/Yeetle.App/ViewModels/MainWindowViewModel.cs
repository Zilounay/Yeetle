namespace Yeetle.App.ViewModels;

public sealed class MainWindowViewModel(PlayerViewModel player) : ViewModelBase
{
    public PlayerViewModel Player { get; } = player;

    // Called when the window is about to close, while its video windows still exist.
    public void OnClosing() => Player.Stop();
}
