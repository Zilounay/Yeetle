using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Yeetle.App.Input;
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

    // After the list is rebuilt and laid out: the button that was clicked is gone by then, and the
    // name field may only just have become visible with the new selection (#62).
    private void OnNameFocusRequested(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() => FocusName(retryAfterLayout: true), DispatcherPriority.Background);

    private void FocusName(bool retryAfterLayout)
    {
        if (!NameBox.IsEffectivelyVisible && retryAfterLayout)
        {
            EventHandler? retry = null;
            retry = (_, _) =>
            {
                NameBox.LayoutUpdated -= retry;
                FocusName(retryAfterLayout: false);
            };
            NameBox.LayoutUpdated += retry;
            return;
        }
        Activate();
        NameBox.Focus();
        NameBox.SelectAll();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel is not { IsCapturingHotkey: true } viewModel)
            return;
        e.Handled = true;
        _ = viewModel.CaptureHotkeyAsync(KeyPress.From(e));
    }

    private void OnNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || _viewModel is null)
            return;
        e.Handled = true;
        _ = _viewModel.CommitNameAsync();
    }
}
