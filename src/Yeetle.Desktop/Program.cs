using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Yeetle.Application.Settings;
using Yeetle.Infrastructure.Settings;

namespace Yeetle.Desktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Read before anything starts, since every part may depend on the settings.
        var settings = new JsonSettingsStore(JsonSettingsStore.DefaultPath).Load();
        using var services = Composition.CreateServices(settings);
        return BuildAvaloniaApp(services).StartWithClassicDesktopLifetime(args);
    }

    // Also used by the visual designer, which never creates the main window.
    public static AppBuilder BuildAvaloniaApp() =>
        BuildAvaloniaApp(Composition.CreateServices(new LoadedSettings(new AppSettings(), string.Empty)));

    private static AppBuilder BuildAvaloniaApp(IServiceProvider services) =>
        AppBuilder.Configure(() => new App.App
            {
                CreateMainViewModel = services.GetRequiredService<App.ViewModels.MainWindowViewModel>,
            })
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
