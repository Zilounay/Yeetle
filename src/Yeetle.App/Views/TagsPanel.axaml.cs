using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Yeetle.App.ViewModels;

namespace Yeetle.App.Views;

public partial class TagsPanel : UserControl
{
    private TagPanelViewModel? _viewModel;

    public TagsPanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Watch(DataContext as TagPanelViewModel);
        TypedBox.KeyDown += OnTypedKeyDown;
        // A click picks the suggestion under the pointer, like Enter on the highlighted one.
        SuggestionList.Tapped += (_, _) => _viewModel?.ApplyTypedCommand.Execute(null);
    }

    private void Watch(TagPanelViewModel? viewModel)
    {
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelChanged;
        _viewModel = viewModel;
        if (viewModel is not null)
            viewModel.PropertyChanged += OnViewModelChanged;
    }

    // T may be pressed over the video, whose controls live in another window: this window comes
    // forward so the box gets the keys. Once the box closes, focus leaves it so hotkeys work again.
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TagPanelViewModel.IsTyping) || _viewModel is null)
            return;
        if (_viewModel.IsTyping)
        {
            (TopLevel.GetTopLevel(this) as Window)?.Activate();
            Dispatcher.UIThread.Post(() => TypedBox.Focus(), DispatcherPriority.Input);
        }
        else
        {
            TopLevel.GetTopLevel(this)?.Focus();
        }
    }

    private void OnTypedKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel is null)
            return;
        switch (e.Key)
        {
            case Key.Enter:
                _viewModel.ApplyTypedCommand.Execute(null);
                break;
            case Key.Escape:
                _viewModel.CloseTypingCommand.Execute(null);
                break;
            case Key.Down:
                _viewModel.MoveSuggestion(1);
                break;
            case Key.Up:
                _viewModel.MoveSuggestion(-1);
                break;
            default:
                return;
        }
        e.Handled = true;
    }
}
