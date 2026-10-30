using Yeetle.App.ViewModels;

namespace ClipStrip.ViewModels;

public sealed class MainViewModel(PlayerViewModel player, ClipStripViewModel strip)
{
    public PlayerViewModel Player { get; } = player;

    public ClipStripViewModel Strip { get; } = strip;
}
