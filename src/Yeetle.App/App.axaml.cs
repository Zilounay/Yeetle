using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Yeetle.App.ViewModels;
using Yeetle.App.Views;
using Yeetle.Application.Data;
using Yeetle.Application.Diagnostics;

namespace Yeetle.App;

// What the host (Yeetle.Desktop), which owns the services, hands to the app.
public sealed record AppServices(
    IDatabaseStartup Database,
    IAppActivation Activation,
    Func<MainWindowViewModel> CreateMainViewModel,
    Func<RecoveryViewModel> CreateRecoveryViewModel);

public partial class App : Avalonia.Application
{
    public AppServices? Services { get; init; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = Services ?? throw new InvalidOperationException($"{nameof(Services)} must be set before the app starts.");
            services.Activation.ActivationRequested += (_, _) => Dispatcher.UIThread.Post(() => BringForward(desktop.MainWindow));
            _ = StartAsync(desktop, services);
        }

        base.OnFrameworkInitializationCompleted();
    }

    // The database is checked and upgraded off the UI thread before any window uses it.
    private static async Task StartAsync(IClassicDesktopStyleApplicationLifetime desktop, AppServices services)
    {
        var check = await Task.Run(services.Database.Prepare);
        if (check.State == DatabaseState.Ready)
        {
            ShowMain(desktop, services);
            return;
        }
        var recovery = services.CreateRecoveryViewModel();
        recovery.Show(check);
        var window = new RecoveryWindow { DataContext = recovery };
        recovery.Recovered += (_, _) =>
        {
            // The main window opens first, so closing this one does not end the app.
            ShowMain(desktop, services);
            window.Close();
        };
        desktop.MainWindow = window;
        window.Show();
    }

    private static void ShowMain(IClassicDesktopStyleApplicationLifetime desktop, AppServices services)
    {
        var viewModel = services.CreateMainViewModel();
        var window = new MainWindow { DataContext = viewModel };
        desktop.MainWindow = window;
        window.Show();
    }

    private static void BringForward(Window? window)
    {
        if (window is null)
            return;
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.Activate();
    }
}
