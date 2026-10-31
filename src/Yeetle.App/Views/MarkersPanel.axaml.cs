using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yeetle.App.ViewModels;

namespace Yeetle.App.Views;

public partial class MarkersPanel : UserControl
{
    private MarkersPanelViewModel? _viewModel;

    public MarkersPanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Watch(DataContext as MarkersPanelViewModel);
        // Label fields: Enter saves, Esc puts the old label back; both hand the keys back to the
        // hotkeys. Leaving a field saves it too.
        List.AddHandler(KeyDownEvent, OnLabelKeyDown, RoutingStrategies.Bubble);
        List.AddHandler(LostFocusEvent, (_, e) => Commit(e.Source), RoutingStrategies.Bubble);
    }

    private void Watch(MarkersPanelViewModel? viewModel)
    {
        if (_viewModel is not null)
            _viewModel.EditRequested -= OnEditRequested;
        _viewModel = viewModel;
        if (viewModel is not null)
            viewModel.EditRequested += OnEditRequested;
    }

    // K may be pressed over the video, whose controls live in another window: this window comes
    // forward so the label gets the keys (spec §7: the label can be typed right away).
    private void OnEditRequested(object? sender, int number)
    {
        (TopLevel.GetTopLevel(this) as Window)?.Activate();
        Dispatcher.UIThread.Post(() =>
        {
            var box = List.GetVisualDescendants().OfType<TextBox>()
                .FirstOrDefault(t => t.DataContext is MarkerItemViewModel item && item.Number == number);
            box?.Focus();
            box?.SelectAll();
        }, DispatcherPriority.Background);
    }

    private void OnLabelKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Escape) || e.Source is not TextBox)
            return;
        e.Handled = true;
        if (e.Key == Key.Escape && e.Source is TextBox { DataContext: MarkerItemViewModel item })
            item.Label = item.SavedLabel;
        // Leaving the field saves the label (LostFocus).
        TopLevel.GetTopLevel(this)?.Focus();
    }

    private void Commit(object? source)
    {
        if (source is TextBox { DataContext: MarkerItemViewModel item })
            _viewModel?.CommitLabel(item);
    }
}
