using Avalonia;
using ClipStrip.Ffmpeg;
using ClipStrip.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Yeetle.App.Services;
using Yeetle.App.ViewModels;
using Yeetle.Application.Clips;
using Yeetle.Application.Playback;
using Yeetle.Application.Settings;
using Yeetle.Infrastructure.Clips;
using Yeetle.Infrastructure.Settings;
using Yeetle.Media.Vlc;

namespace ClipStrip;

internal static class Program
{
    private const string FfmpegOption = "--ffmpeg";
    private const string BenchOption = "--bench";
    private const string FfmpegFolderVariable = "YEETLE_FFMPEG";

    // ClipStrip [folder] [--ffmpeg <folder with ffmpeg.exe and ffprobe.exe>] [--bench]
    [STAThread]
    public static int Main(string[] args)
    {
        string? folder = null, ffmpegFolder = Environment.GetEnvironmentVariable(FfmpegFolderVariable);
        var bench = false;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == FfmpegOption && i + 1 < args.Length)
                ffmpegFolder = args[++i];
            else if (args[i] == BenchOption)
                bench = true;
            else
                folder = args[i];
        }

        var settings = new JsonSettingsStore(JsonSettingsStore.DefaultPath).Load();
        var tools = new MediaTools(ffmpegFolder);
        if (bench)
            return Bench.RunAsync(folder, tools, settings.Values).GetAwaiter().GetResult();

        using var services = new ServiceCollection()
            .AddSingleton(settings)
            .AddSingleton(settings.Values)
            .AddSingleton(tools)
            .AddSingleton<ClipNavigator>()
            .AddSingleton<IClipFolderReader, FileSystemClipFolderReader>()
            .AddSingleton<IClipPlayer, VlcClipPlayer>()
            .AddSingleton<IFolderPicker, StorageFolderPicker>()
            .AddSingleton<PlayerViewModel>()
            .AddSingleton<ClipStripViewModel>()
            .AddSingleton<MainViewModel>()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

        return AppBuilder.Configure(() => new App
            {
                CreateMainViewModel = services.GetRequiredService<MainViewModel>,
                StartFolder = folder,
            })
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .StartWithClassicDesktopLifetime(args);
    }
}
