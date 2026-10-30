using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ClipStrip.ViewModels;

namespace ClipStrip;

public partial class App : Avalonia.Application
{
    public Func<MainViewModel>? CreateMainViewModel { get; init; }

    public string? StartFolder { get; init; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && CreateMainViewModel is not null)
        {
            var viewModel = CreateMainViewModel();
            desktop.MainWindow = new MainWindow { DataContext = viewModel };
            if (StartFolder is not null)
                _ = viewModel.Player.OpenFolderAsync(StartFolder);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
