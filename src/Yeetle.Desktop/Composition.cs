using Microsoft.Extensions.DependencyInjection;
using Yeetle.App.Services;
using Yeetle.App.ViewModels;
using Yeetle.Application.Clips;
using Yeetle.Application.Playback;
using Yeetle.Application.Settings;
using Yeetle.Infrastructure.Clips;
using Yeetle.Media.Vlc;

namespace Yeetle.Desktop;

// The only place that knows every implementation; everything else depends on interfaces.
internal static class Composition
{
    public static ServiceProvider CreateServices(LoadedSettings settings) => new ServiceCollection()
        .AddSingleton(settings)
        .AddSingleton(settings.Values)
        // Application
        .AddSingleton<ClipNavigator>()
        // Infrastructure
        .AddSingleton<IClipFolderReader, FileSystemClipFolderReader>()
        // Media
        .AddSingleton<IClipPlayer, VlcClipPlayer>()
        // App
        .AddSingleton<IFolderPicker, StorageFolderPicker>()
        .AddSingleton<PlayerViewModel>()
        .AddSingleton<MainWindowViewModel>()
        .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
}
