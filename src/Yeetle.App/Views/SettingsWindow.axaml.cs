using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Yeetle.App.Input;
using Yeetle.App.ViewModels;

namespace Yeetle.App.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        // Read before any control, so the key waited for never reaches a focused button.
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not SettingsViewModel { Hotkeys.IsCapturing: true } viewModel)
            return;
        e.Handled = true;
        viewModel.Hotkeys.Capture(KeyPress.From(e));
    }
}
