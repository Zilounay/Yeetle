using Microsoft.Extensions.DependencyInjection;
using Yeetle.App.Services;
using Yeetle.App.ViewModels;
using Yeetle.Application.Clips;
using Yeetle.Application.Data;
using Yeetle.Application.Diagnostics;
using Yeetle.Application.Playback;
using Yeetle.Application.Settings;
using Yeetle.Application.Shell;
using Yeetle.Infrastructure;
using Yeetle.Infrastructure.Clips;
using Yeetle.Infrastructure.Data;
using Yeetle.Infrastructure.Diagnostics;
using Yeetle.Infrastructure.Shell;
using Yeetle.Media.Vlc;

namespace Yeetle.Desktop;

// The only place that knows every implementation; everything else depends on interfaces.
internal static class Composition
{
    public static ServiceProvider CreateServices(LoadedSettings settings, AppPaths paths, IAppActivation activation) =>
        new ServiceCollection()
            .AddSingleton(settings)
            .AddSingleton(settings.Values)
            .AddSingleton(settings.Values.Backups)
            .AddSingleton(paths)
            .AddSingleton(TimeProvider.System)
            .AddSingleton(activation)
            // Application
            .AddSingleton<ClipNavigator>()
            // Infrastructure
            .AddSingleton<ILog, FileLog>()
            .AddSingleton<SqliteConnectionFactory>()
            .AddSingleton<IBackups, SqliteBackups>()
            .AddSingleton<IDatabaseStartup, SqliteDatabaseStartup>()
            .AddSingleton<IShell, WindowsShell>()
            .AddSingleton<IClipFolderReader, FileSystemClipFolderReader>()
            // Media
            .AddSingleton<IClipPlayer, VlcClipPlayer>()
            // App
            .AddSingleton<IFolderPicker, StorageFolderPicker>()
            .AddSingleton<PlayerViewModel>()
            .AddSingleton<MainWindowViewModel>()
            .AddTransient<RecoveryViewModel>()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
}
