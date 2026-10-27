using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Yeetle.App.ViewModels;
using Yeetle.App.Views;

namespace Yeetle.App;

public partial class App : Avalonia.Application
{
    // Set by the host (Yeetle.Desktop), which owns the services.
    public Func<MainWindowViewModel>? CreateMainViewModel { get; init; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = (CreateMainViewModel ?? throw new InvalidOperationException(
                $"{nameof(CreateMainViewModel)} must be set before the app starts."))();
            desktop.MainWindow = new MainWindow { DataContext = viewModel };
            // Development shortcut until source folders exist (#12): Yeetle.exe "D:\clips"
            if (desktop.Args is [var folder, ..] && Directory.Exists(folder))
                _ = viewModel.Player.OpenFolderAsync(folder);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
