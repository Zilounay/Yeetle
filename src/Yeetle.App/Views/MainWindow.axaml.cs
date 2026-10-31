using Avalonia.Controls;
using Yeetle.App.Input;
using Yeetle.App.ViewModels;

namespace Yeetle.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        HotkeyRouting.Attach(this, () => ViewModel);
        Opened += (_, _) =>
        {
            if (ViewModel is not { } viewModel)
                return;
            new VideoOverlayWindow { DataContext = viewModel }.Follow(this, Player.Video);
            _ = viewModel.StartAsync();
        };
        Activated += (_, _) => ViewModel?.OnActivated();
        Closing += (_, _) => ViewModel?.OnClosing();
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;
}
