using Microsoft.Extensions.DependencyInjection;
using Yeetle.App.Services;
using Yeetle.App.ViewModels;
using Yeetle.Application.Data;
using Yeetle.Application.Diagnostics;
using Yeetle.Application.Files;
using Yeetle.Application.Library;
using Yeetle.Application.Playback;
using Yeetle.Application.Sessions;
using Yeetle.Application.Settings;
using Yeetle.Application.Shell;
using Yeetle.Application.Tagging;
using Yeetle.Infrastructure;
using Yeetle.Infrastructure.Clips;
using Yeetle.Infrastructure.Data;
using Yeetle.Infrastructure.Diagnostics;
using Yeetle.Infrastructure.Files;
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
            .AddSingleton(settings.Values.Library)
            .AddSingleton(paths)
            .AddSingleton(TimeProvider.System)
            .AddSingleton(activation)
            // Application
            .AddSingleton<ClipNavigator>()
            .AddSingleton<LibraryScanner>()
            .AddSingleton<ScanScheduler>()
            .AddSingleton<ClipDurations>()
            .AddSingleton<ClipLibrary>()
            .AddSingleton<SourceFolders>()
            .AddSingleton<SessionService>()
            .AddSingleton<FileOperations>()
            .AddSingleton<TagManager>()
            // Infrastructure
            .AddSingleton<ILog, FileLog>()
            .AddSingleton<SqliteConnectionFactory>()
            .AddSingleton<IBackups, SqliteBackups>()
            .AddSingleton<IDatabaseStartup, SqliteDatabaseStartup>()
            .AddSingleton<IShell, WindowsShell>()
            .AddSingleton<ILibraryStore, SqliteLibraryStore>()
            .AddSingleton<IClipFiles, FileSystemClipFiles>()
            .AddSingleton<IRecorderCheck, ObsRecorderCheck>()
            .AddSingleton<ISessionStore, SqliteSessionStore>()
            .AddSingleton<SqliteTagStore>()
            .AddSingleton<IExplorerStore, SqliteExplorerStore>()
            .AddSingleton<ITagStore>(s => s.GetRequiredService<SqliteTagStore>())
            .AddSingleton<ITagEditor>(s => s.GetRequiredService<SqliteTagStore>())
            .AddSingleton<IFileOperationStore, SqliteFileOperationStore>()
            .AddSingleton<IFileSystem, WindowsFileSystem>()
            // Media
            .AddSingleton<IClipPlayer, VlcClipPlayer>()
            .AddSingleton<IDurationReader, VlcDurationReader>()
            // App
            .AddSingleton<IFolderPicker, StorageFolderPicker>()
            .AddSingleton<IDialogs>(s => new WindowDialogs(s.GetRequiredService<SourceFoldersViewModel>,
                s.GetRequiredService<TagManagerViewModel>))
            .AddTransient<TagManagerViewModel>()
            .AddTransient<SourceFoldersViewModel>()
            .AddSingleton<PlayerViewModel>()
            // One each for the session and the clip editor.
            .AddTransient<TagPanelViewModel>()
            .AddTransient<MarkersPanelViewModel>()
            .AddSingleton<ExplorerViewModel>()
            .AddSingleton<EditorViewModel>()
            .AddSingleton<SessionViewModel>()
            .AddSingleton<MainWindowViewModel>()
            .AddTransient<RecoveryViewModel>()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
}
