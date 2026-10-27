using Avalonia.Controls;

namespace Yeetle.App.Views;

public partial class PlayerView : UserControl
{
    public PlayerView() => InitializeComponent();

    // The area the overlay window covers.
    public Control Video => VideoArea;
}
