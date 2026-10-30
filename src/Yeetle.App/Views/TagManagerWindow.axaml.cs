using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Yeetle.App.ViewModels;

namespace Yeetle.App.Views;

public partial class TagManagerWindow : Window
{
    private TagManagerViewModel? _viewModel;

    public TagManagerWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Watch(DataContext as TagManagerViewModel);
        // Read before any control, so the key waited for never reaches a focused button.
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        NameBox.KeyDown += OnNameKeyDown;
        NameBox.LostFocus += (_, _) => _ = _viewModel?.CommitNameAsync();
    }

    private void Watch(TagManagerViewModel? viewModel)
    {
        if (_viewModel is not null)
            _viewModel.NameFocusRequested -= OnNameFocusRequested;
        _viewModel = viewModel;
        if (viewModel is not null)
            viewModel.NameFocusRequested += OnNameFocusRequested;
    }

    private void OnNameFocusRequested(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        NameBox.Focus();
        NameBox.SelectAll();
    }, DispatcherPriority.Input);

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel is not { IsCapturingHotkey: true } viewModel)
            return;
        e.Handled = true;
        _ = viewModel.CaptureHotkeyAsync(e.KeyModifiers, e.PhysicalKey, e.KeySymbol);
    }

    private void OnNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || _viewModel is null)
            return;
        e.Handled = true;
        _ = _viewModel.CommitNameAsync();
    }
}
