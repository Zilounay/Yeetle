using Avalonia.Controls;
using Yeetle.App.Input;
using Yeetle.App.ViewModels;

namespace Yeetle.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        HotkeyRouting.Attach(this, () => ViewModel?.Player);
        Opened += (_, _) =>
        {
            if (ViewModel is { } viewModel)
                new VideoOverlayWindow { DataContext = viewModel.Player }.Follow(this, Player.Video);
        };
        Closing += (_, _) => ViewModel?.OnClosing();
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;
}
