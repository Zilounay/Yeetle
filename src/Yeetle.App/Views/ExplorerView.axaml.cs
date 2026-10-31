using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Yeetle.App.ViewModels;

namespace Yeetle.App.Views;

// View wiring for the clip list: selection, double-click, the Windows context menu and dragging
// files out (spec §8; drag-out from POC #6).
public partial class ExplorerView : UserControl
{
    // Distance the mouse must travel with the button down before a press becomes a drag. Windows
    // uses 4 px by default (SM_CXDRAG), so this feels like Explorer.
    private const double DragThreshold = 4;

    private PointerPressedEventArgs? _press;
    private Point _pressPoint;
    // Set when the press landed on an already selected row with no modifier: Explorer keeps the
    // whole selection so it can be dragged, and narrows it to this row only on release.
    private ExplorerRowViewModel? _pressedSelectedRow;

    public ExplorerView()
    {
        InitializeComponent();
        List.SelectionChanged += (_, _) => ViewModel?.SetSelection(List.SelectedItems?.Cast<object>() ?? []);
        List.DoubleTapped += (_, e) =>
        {
            if (RowAt(e.Source) is not null && ViewModel?.OpenCommand.CanExecute(null) == true)
                ViewModel.OpenCommand.Execute(null);
        };
        List.ContextRequested += OnContextRequested;
        // Tunnel, so these run before the ListBox changes the selection on press.
        List.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        List.AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel);
        List.AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel);
    }

    private ExplorerViewModel? ViewModel => DataContext as ExplorerViewModel;

    private static ExplorerRowViewModel? RowAt(object? source) =>
        (source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext as ExplorerRowViewModel;

    // A right-click on a row outside the selection selects that row first, as in Explorer.
    private async void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (ViewModel is not { } viewModel || RowAt(e.Source) is not { } row)
            return;
        e.Handled = true;
        if (List.SelectedItems?.Contains(row) != true)
            List.SelectedItems = new List<object> { row };
        if (TopLevel.GetTopLevel(this) is not { } top || top.TryGetPlatformHandle()?.Handle is not { } window)
            return;
        var point = e.TryGetPosition(List, out var at) ? at : new Point(0, 0);
        var screen = List.PointToScreen(point);
        await viewModel.ShowMenuAsync(window, screen.X, screen.Y);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _press = null;
        _pressedSelectedRow = null;
        if (!e.GetCurrentPoint(List).Properties.IsLeftButtonPressed || RowAt(e.Source) is not { } row)
            return;
        _press = e;
        _pressPoint = e.GetPosition(List);
        if (e.KeyModifiers == KeyModifiers.None && e.ClickCount == 1 && List.SelectedItems?.Contains(row) == true)
        {
            _pressedSelectedRow = row;
            e.Handled = true;
        }
    }

    private async void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_press is not { } press || !e.GetCurrentPoint(List).Properties.IsLeftButtonPressed)
            return;
        var delta = e.GetPosition(List) - _pressPoint;
        if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold)
            return;
        _press = null;
        _pressedSelectedRow = null;
        await DragSelectionAsync(press);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_pressedSelectedRow is { } row)
            List.SelectedItems = new List<object> { row };
        _press = null;
        _pressedSelectedRow = null;
    }

    // Copy is the only effect offered, so a target can never move the files, even with Shift.
    private async Task DragSelectionAsync(PointerPressedEventArgs press)
    {
        if (ViewModel is not { } viewModel || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return;
        var transfer = new DataTransfer();
        foreach (var path in viewModel.PathsToDrag())
        {
            if (await storage.TryGetFileFromPathAsync(path) is { } file)
                transfer.Add(DataTransferItem.CreateFile(file));
        }
        if (transfer.Items.Count > 0)
            await DragDrop.DoDragDropAsync(press, transfer, DragDropEffects.Copy);
    }
}
