using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using SukiUI;
using SukiUI.Models;
using Yeetle.Application.Settings;

namespace Yeetle.App.Theming;

// Settings › Appearance, applied live (#69): the colour theme, the moving background glow and
// Reduce motion. The theme colours go into the application resources under the keys below, which
// the views use as DynamicResource, so a change repaints every open window at once.
public sealed class Appearance
{
    public const string PrimaryBrush = "YtPrimaryBrush";
    public const string PrimaryHoverBrush = "YtPrimaryHoverBrush";
    public const string PrimaryLightBrush = "YtPrimaryLightBrush";
    public const string PrimarySoftBrush = "YtPrimarySoftBrush";
    public const string AccentBrush = "YtAccentBrush";
    public const string AccentSoftBrush = "YtAccentSoftBrush";
    public const string PageBrush = "YtPageBrush";
    public const string ChipSelectedBrush = "YtChipSelectedBrush";
    public const string ChipSelectedBorderBrush = "YtChipSelectedBorderBrush";
    public const string RowSelectedBrush = "YtRowSelectedBrush";
    public const string RowSelectedBorderBrush = "YtRowSelectedBorderBrush";
    public const string TagPillBrush = "YtTagPillBrush";
    public const string TagPillTextBrush = "YtTagPillTextBrush";
    // Set on every window while Reduce motion is on; YeetleTheme.axaml turns motion off under it.
    public const string ReduceMotionClass = "reduceMotion";

    private readonly Dictionary<ColourTheme, SukiColorTheme> _sukiThemes = [];
    private bool _windowsFollowed;

    public static Appearance Instance { get; } = new();

    public Palette Palette { get; private set; } = Palettes.SukiBlue;

    // The glow drifts slowly: on unless turned off, or Reduce motion is on.
    public bool AnimateBackground { get; private set; } = true;

    public bool ReduceMotion { get; private set; }

    // Raised on the UI thread after a change was applied.
    public event EventHandler? Changed;

    // Called at start, before the first window opens, and after each settings change.
    public void Apply(AppearanceSettings settings)
    {
        var palette = Palettes.Of(settings.ColourTheme);
        var animate = settings.AnimatedBackground && !settings.ReduceMotion;
        var changed = palette != Palette || animate != AnimateBackground || settings.ReduceMotion != ReduceMotion;
        Palette = palette;
        AnimateBackground = animate;
        ReduceMotion = settings.ReduceMotion;
        if (Avalonia.Application.Current is not { } app)
            return;
        PutColours(app.Resources, palette);
        UseSukiTheme(palette);
        FollowWindows();
        foreach (var window in OpenWindows())
            window.Classes.Set(ReduceMotionClass, ReduceMotion);
        if (changed)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    private static void PutColours(IResourceDictionary resources, Palette p)
    {
        resources[PrimaryBrush] = new SolidColorBrush(p.Primary);
        resources[PrimaryHoverBrush] = new SolidColorBrush(p.PrimaryHover);
        resources[PrimaryLightBrush] = new SolidColorBrush(p.PrimaryLight);
        resources[PrimarySoftBrush] = new SolidColorBrush(p.PrimarySoft);
        resources[AccentBrush] = new SolidColorBrush(p.Accent);
        resources[AccentSoftBrush] = new SolidColorBrush(p.AccentSoft);
        resources[PageBrush] = new SolidColorBrush(p.Page);
        resources[ChipSelectedBrush] = new SolidColorBrush(p.ChipSelected);
        resources[ChipSelectedBorderBrush] = new SolidColorBrush(p.ChipSelectedBorder);
        resources[RowSelectedBrush] = new SolidColorBrush(p.RowSelected);
        resources[RowSelectedBorderBrush] = new SolidColorBrush(p.RowSelectedBorder);
        resources[TagPillBrush] = new SolidColorBrush(p.TagPill);
        resources[TagPillTextBrush] = new SolidColorBrush(p.TagText);
    }

    // SukiUI's own controls (switches, sliders, combo boxes, progress bars) follow the same colours.
    private void UseSukiTheme(Palette p)
    {
        if (SukiTheme.GetInstance() is not { } suki)
            return;
        if (!_sukiThemes.TryGetValue(p.Theme, out var theme))
        {
            theme = new SukiColorTheme(p.Theme.ToString(), p.Primary, p.Accent);
            suki.AddColorTheme(theme);
            _sukiThemes[p.Theme] = theme;
        }
        suki.ChangeColorTheme(theme);
    }

    // A window opened later gets the Reduce motion class too.
    private void FollowWindows()
    {
        if (_windowsFollowed)
            return;
        _windowsFollowed = true;
        Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
            window.Classes.Set(ReduceMotionClass, ReduceMotion), RoutingStrategies.Direct);
    }

    private static IEnumerable<Window> OpenWindows() =>
        Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.Windows
            : [];
}
