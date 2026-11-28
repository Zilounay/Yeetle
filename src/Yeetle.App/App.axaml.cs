using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Yeetle.App.Theming;
using Yeetle.App.ViewModels;
using Yeetle.App.Views;
using Yeetle.Application.Data;
using Yeetle.Application.Diagnostics;
using Yeetle.Application.Settings;
using Yeetle.Application.Tagging;

namespace Yeetle.App;

// What the host (Yeetle.Desktop), which owns the services, hands to the app.
public sealed record AppServices(
    IDatabaseStartup Database,
    TagFileImport TagFiles,
    IAppActivation Activation,
    Func<MainWindowViewModel> CreateMainViewModel,
    Func<RecoveryViewModel> CreateRecoveryViewModel,
    SettingsService Settings);

public partial class App : Avalonia.Application
{
    public AppServices? Services { get; init; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = Services ?? throw new InvalidOperationException($"{nameof(Services)} must be set before the app starts.");
            // The colours, before any window opens, and live after each change (#69).
            Appearance.Instance.Apply(services.Settings.Values.Appearance);
            services.Settings.Changed += (_, _) => Appearance.Instance.Apply(services.Settings.Values.Appearance);
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
            await ShowMainAsync(desktop, services);
            return;
        }
        var recovery = services.CreateRecoveryViewModel();
        recovery.Show(check);
        var window = new RecoveryWindow { DataContext = recovery };
        recovery.Recovered += async (_, _) =>
        {
            // The main window opens first, so closing this one does not end the app.
            await ShowMainAsync(desktop, services);
            window.Close();
        };
        desktop.MainWindow = window;
        window.Show();
    }

    private static async Task ShowMainAsync(IClassicDesktopStyleApplicationLifetime desktop, AppServices services)
    {
        // New tags from the tags files arrive before any screen loads the vocabulary (spec §6). A
        // file that cannot be read is logged and leaves the tags as they are.
        await Task.Run(services.TagFiles.ImportAtStart);
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
