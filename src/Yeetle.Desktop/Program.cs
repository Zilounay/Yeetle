using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Velopack;
using Yeetle.App;
using Yeetle.App.ViewModels;
using Yeetle.Application.Data;
using Yeetle.Application.Settings;
using Yeetle.Infrastructure;
using Yeetle.Infrastructure.Diagnostics;
using Yeetle.Infrastructure.Settings;

namespace Yeetle.Desktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // First of all (#22): when the installer or an update runs Yeetle to finish its work, this
        // does it and exits, before any window or lock exists.
        VelopackApp.Build().Run();
        int exitCode;
        // One instance per data folder, so a test copy on a throwaway folder can run beside the real one.
        using (var instance = new SingleInstance(AppPaths.Default.Root))
        {
            if (!instance.IsFirst)
            {
                instance.ActivateFirst();
                return 0;
            }
            // Read before anything starts, since every part may depend on the settings.
            var settings = new JsonSettingsStore(AppPaths.Default.Settings).Load();
            // Before any text is made (#20).
            App.Resources.Loc.Instance.Use(settings.Values.General.Language);
            using var services = Composition.CreateServices(settings, AppPaths.Default, instance);
            exitCode = BuildAvaloniaApp(services).StartWithClassicDesktopLifetime(args);
        }
        // A restart asked from Settings (#20): the lock is released above, so the new copy is the first.
        if (DesktopRestart.Requested && Environment.ProcessPath is { } exe)
            System.Diagnostics.Process.Start(exe, args);
        return exitCode;
    }

    // Also used by the visual designer, which never creates the main window.
    public static AppBuilder BuildAvaloniaApp() =>
        BuildAvaloniaApp(Composition.CreateServices(
            new LoadedSettings(new AppSettings(), string.Empty), AppPaths.Default, new NoActivation()));

    private static AppBuilder BuildAvaloniaApp(IServiceProvider services) =>
        AppBuilder.Configure(() => new App.App
            {
                Services = new AppServices(
                    services.GetRequiredService<IDatabaseStartup>(),
                    services.GetRequiredService<Application.Tagging.TagFileImport>(),
                    services.GetRequiredService<Application.Diagnostics.IAppActivation>(),
                    services.GetRequiredService<MainWindowViewModel>,
                    services.GetRequiredService<RecoveryViewModel>,
                    services.GetRequiredService<SettingsService>()),
            })
            .UsePlatformDetect()
            .LogToTrace();

    // The designer runs alongside the real app, so it must not take the single-instance lock.
    private sealed class NoActivation : Application.Diagnostics.IAppActivation
    {
        public event EventHandler? ActivationRequested
        {
            add { }
            remove { }
        }
    }
}
