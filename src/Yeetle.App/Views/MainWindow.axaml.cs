using Avalonia.Controls;
using Yeetle.App.Input;
using Yeetle.App.ViewModels;

namespace Yeetle.App.Views;

public partial class MainWindow : Window
{
    // Set once the user answered the close question, so the second Close goes through.
    private bool _closeConfirmed;

    public MainWindow()
    {
        InitializeComponent();
        HotkeyRouting.Attach(this, () => ViewModel);
        Opened += (_, _) =>
        {
            if (ViewModel is not { } viewModel)
                return;
            var overlay = new VideoOverlayWindow { DataContext = viewModel };
            overlay.Follow(this, Player.Video);
            viewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainWindowViewModel.ShowPlayer))
                    overlay.SetShown(viewModel.ShowPlayer);
            };
            _ = viewModel.StartAsync();
        };
        Activated += (_, _) => ViewModel?.OnActivated();
        Closing += OnClosing;
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    // Closing mid-session asks Save and leave / Discard and leave / Cancel (spec §5).
    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (ViewModel is not { } viewModel)
            return;
        if (!_closeConfirmed && viewModel.Session.IsActive)
        {
            e.Cancel = true;
            if (!await viewModel.ConfirmCloseAsync())
                return;
            _closeConfirmed = true;
            Close();
            return;
        }
        viewModel.OnClosing();
    }
}
