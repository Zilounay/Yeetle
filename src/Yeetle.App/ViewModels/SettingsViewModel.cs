using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Yeetle.App.Resources;
using Yeetle.App.Services;
using Yeetle.Application.Data;
using Yeetle.Application.Library;
using Yeetle.Application.Playback;
using Yeetle.Application.Settings;
using Yeetle.Application.Shell;
using Yeetle.Application.Updates;
using Yeetle.Domain.Clips;

namespace Yeetle.App.ViewModels;

public enum SettingsSection
{
    Library,
    Tags,
    Hotkeys,
    Player,
    Review,
    LibraryView,
    Data,
    General,
}

public sealed record SettingsSectionItem(SettingsSection Section, string Label);

public sealed record BackupChoice(BackupInfo Backup, string Label);

// The Settings window (spec §11, decided in #20): sections on the left, each change applied and
// saved at once, with no OK or Cancel. Privacy comes with usage statistics (#21) and Appearance
// with the visual pass (#69).
public sealed partial class SettingsViewModel : ViewModelBase, IDisposable
{
    private const string ExtensionSeparators = ", ;";
    private const char ExtensionDot = '.';
    private const int MaxBackupCount = 50;

    private readonly SettingsService _settings;
    private readonly IDialogs _dialogs;
    private readonly ScanScheduler _scans;
    private readonly ExplorerViewModel _explorer;
    private readonly MainWindowViewModel _main;
    private readonly IBackups _backups;
    private readonly IShell _shell;
    private readonly IAppRestart _restart;
    private readonly string _dataFolder;
    // Set while the fields are filled from the settings, so filling them saves nothing.
    private bool _filling;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLibrary), nameof(IsTags), nameof(IsHotkeys), nameof(IsPlayer), nameof(IsReview),
        nameof(IsLibraryView), nameof(IsData), nameof(IsGeneral))]
    private SettingsSectionItem _selectedSection;

    // Library
    [ObservableProperty]
    private string _extensionsText = string.Empty;

    [ObservableProperty]
    private string _extensionsProblem = string.Empty;

    // Player
    [ObservableProperty]
    private SpeedOptionViewModel? _defaultSpeed;

    [ObservableProperty]
    private double _defaultVolume;

    [ObservableProperty]
    private decimal _seekSeconds;

    [ObservableProperty]
    private decimal _longSeekSeconds;

    [ObservableProperty]
    private decimal _volumeStep;

    [ObservableProperty]
    private bool _startInTheatreMode;

    [ObservableProperty]
    private bool _prepareNeighbours;

    // Review
    [ObservableProperty]
    private bool _stickyTags;

    [ObservableProperty]
    private decimal _markerOffsetSeconds;

    // Library view: the filters the Explorer opens with.
    [ObservableProperty]
    private bool _defaultKept;

    [ObservableProperty]
    private bool _defaultNew;

    [ObservableProperty]
    private bool _defaultYeeted;

    [ObservableProperty]
    private bool _defaultPresent;

    [ObservableProperty]
    private bool _defaultMissing;

    [ObservableProperty]
    private bool _defaultOffline;

    [ObservableProperty]
    private bool _defaultDeleted;

    [ObservableProperty]
    private bool _defaultNeedsAttention;

    // Data
    [ObservableProperty]
    private decimal _sessionBackups;

    [ObservableProperty]
    private decimal _dailyBackups;

    [ObservableProperty]
    private decimal _safetyBackups;

    [ObservableProperty]
    private IReadOnlyList<BackupChoice> _backupChoices = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreBackupCommand))]
    private BackupChoice? _selectedBackup;

    [ObservableProperty]
    private string _restoreProblem = string.Empty;

    // General
    [ObservableProperty]
    private bool _checkAtStartup;

    [ObservableProperty]
    private string _updateResult = string.Empty;

    public SettingsViewModel(SettingsService settings, IDialogs dialogs, ScanScheduler scans, ExplorerViewModel explorer,
        MainWindowViewModel main, IBackups backups, IShell shell, IAppRestart restart, IDatabaseStartup database,
        HotkeysViewModel hotkeys)
    {
        Hotkeys = hotkeys;
        _settings = settings;
        _dialogs = dialogs;
        _scans = scans;
        _explorer = explorer;
        _main = main;
        _backups = backups;
        _shell = shell;
        _restart = restart;
        _dataFolder = database.DataFolder;
        _sections = MakeSections();
        _selectedSection = _sections[0];
        Loc.Instance.Changed += OnLanguageChanged;
        Speeds = PlaybackSpeeds.All.Select(s => new SpeedOptionViewModel(s)).ToList();
        Fill();
        PropertyChanged += (_, e) =>
        {
            if (!_filling && e.PropertyName is { } name)
                Save(name);
        };
        main.Session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SessionViewModel.IsActive))
            {
                OnPropertyChanged(nameof(CanRestoreNow));
                RestoreBackupCommand.NotifyCanExecuteChanged();
            }
        };
    }

    [ObservableProperty]
    private IReadOnlyList<SettingsSectionItem> _sections;

    public bool IsEnglish => Loc.Instance.Current == Loc.English;

    public bool IsFrench => Loc.Instance.Current == Loc.French;

    // Stops following the language once the window is closed.
    public void Dispose() => Loc.Instance.Changed -= OnLanguageChanged;

    public HotkeysViewModel Hotkeys { get; }

    public IReadOnlyList<SpeedOptionViewModel> Speeds { get; }

    public bool IsLibrary => SelectedSection.Section == SettingsSection.Library;
    public bool IsTags => SelectedSection.Section == SettingsSection.Tags;
    public bool IsHotkeys => SelectedSection.Section == SettingsSection.Hotkeys;
    public bool IsPlayer => SelectedSection.Section == SettingsSection.Player;
    public bool IsReview => SelectedSection.Section == SettingsSection.Review;
    public bool IsLibraryView => SelectedSection.Section == SettingsSection.LibraryView;
    public bool IsData => SelectedSection.Section == SettingsSection.Data;
    public bool IsGeneral => SelectedSection.Section == SettingsSection.General;

    // A backup cannot replace the database under a running session.
    public bool CanRestoreNow => !_main.Session.IsActive;

    public string Version => string.Format(Strings.VersionFormat,
        typeof(SettingsViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .Split('+')[0] ?? string.Empty);

    public string SettingsFile => string.Format(Strings.SettingsFileHint, _settings.Location);

    // Called when the window opens, and when the Data section is shown, so the list is current.
    public async Task LoadBackupsAsync()
    {
        var backups = await Task.Run(_backups.List);
        BackupChoices = backups.Select(b => new BackupChoice(b, string.Format(Strings.BackupLabelFormat, PoolName(b.Pool),
            b.CreatedAt, SizeFormat.Bytes(SizeOf(b.Path))))).ToList();
    }

    partial void OnSelectedSectionChanged(SettingsSectionItem value)
    {
        if (value.Section == SettingsSection.Data)
            _ = LoadBackupsAsync();
        if (value.Section == SettingsSection.Hotkeys)
            _ = Hotkeys.LoadTagKeysAsync();
    }

    // The language applies at once, everywhere, and is saved (#20).
    [RelayCommand]
    private void UseLanguage(string code)
    {
        _ = _settings.ChangeAsync(s => s.General.Language = code);
        Loc.Instance.Use(code);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        var shown = SelectedSection.Section;
        Sections = MakeSections();
        SelectedSection = Sections.First(s => s.Section == shown);
        OnPropertyChanged(nameof(IsEnglish));
        OnPropertyChanged(nameof(IsFrench));
        OnPropertyChanged(nameof(Version));
        OnPropertyChanged(nameof(SettingsFile));
        UpdateResult = string.Empty;
        Hotkeys.Relocalize();
        Speeds.ToList().ForEach(s => s.Relocalize());
        if (shown == SettingsSection.Data)
            _ = LoadBackupsAsync();
    }

    private static IReadOnlyList<SettingsSectionItem> MakeSections() =>
    [
        new(SettingsSection.Library, Strings.SettingsLibrary),
        new(SettingsSection.Tags, Strings.SettingsTags),
        new(SettingsSection.Hotkeys, Strings.SettingsHotkeys),
        new(SettingsSection.Player, Strings.SettingsPlayer),
        new(SettingsSection.Review, Strings.SettingsReview),
        new(SettingsSection.LibraryView, Strings.SettingsLibraryView),
        new(SettingsSection.Data, Strings.SettingsData),
        new(SettingsSection.General, Strings.SettingsGeneral),
    ];

    [RelayCommand]
    private Task OpenSourceFoldersAsync() => _dialogs.ShowSourceFoldersAsync();

    [RelayCommand]
    private Task RescanAsync() => _scans.RequestAsync(ScanTrigger.Rescan);

    [RelayCommand]
    private void OpenTagManager() => _dialogs.ShowTagManager();

    [RelayCommand]
    private void OpenDataFolder() => _shell.OpenWithDefaultApp(_dataFolder);

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        UpdateResult = Strings.UpdateChecking;
        var check = await _main.CheckForUpdateNowAsync();
        UpdateResult = check.Outcome switch
        {
            UpdateOutcome.Found => string.Format(Strings.UpdateFound, check.Update!.Version),
            UpdateOutcome.UpToDate => Strings.UpdateUpToDate,
            UpdateOutcome.NotInstalled => Strings.UpdateNotInstalled,
            _ => Strings.UpdateCheckFailed,
        };
    }

    // Asked first; a safety backup of today's database is taken, then Yeetle restarts on the
    // chosen backup. Clips are never touched (#20).
    [RelayCommand(CanExecute = nameof(CanRestore))]
    private async Task RestoreBackupAsync()
    {
        if (SelectedBackup is not { } chosen)
            return;
        var answer = await _dialogs.ChooseAsync(Strings.RestoreBackupTitle,
            string.Format(Strings.RestoreBackupMessage, chosen.Label), [(Strings.RestoreAndRestart, true)]);
        if (answer != true)
            return;
        try
        {
            await Task.Run(() => _backups.ScheduleRestore(chosen.Backup));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Data.Common.DbException)
        {
            RestoreProblem = string.Format(Strings.RestoreFailed, e.Message);
            return;
        }
        await _settings.FlushAsync();
        _restart.Restart();
    }

    private bool CanRestore() => SelectedBackup is not null && CanRestoreNow;

    private void Fill()
    {
        _filling = true;
        var values = _settings.Values;
        ExtensionsText = string.Join(", ", values.Library.VideoExtensions);
        DefaultSpeed = Speeds.FirstOrDefault(s => s.Speed.Equals(values.Player.DefaultSpeed)) ?? Speeds[0];
        DefaultVolume = values.Player.DefaultVolume;
        SeekSeconds = (decimal)values.Player.SeekSeconds;
        LongSeekSeconds = (decimal)values.Player.LongSeekSeconds;
        VolumeStep = values.Player.VolumeStep;
        StartInTheatreMode = values.Player.TheatreMode;
        PrepareNeighbours = values.Player.PrepareNeighbours;
        StickyTags = values.Review.StickyTags;
        MarkerOffsetSeconds = (decimal)values.Review.MarkerOffsetSeconds;
        var view = values.LibraryView;
        (DefaultKept, DefaultNew, DefaultYeeted) = (view.DefaultDecisions.Contains(Decision.Kept),
            view.DefaultDecisions.Contains(Decision.New), view.DefaultDecisions.Contains(Decision.Yeeted));
        (DefaultPresent, DefaultMissing, DefaultOffline, DefaultDeleted, DefaultNeedsAttention) = (
            view.DefaultStates.Contains(FileState.Present), view.DefaultStates.Contains(FileState.Missing),
            view.DefaultStates.Contains(FileState.Offline), view.DefaultStates.Contains(FileState.Deleted),
            view.DefaultStates.Contains(FileState.NeedsAttention));
        SessionBackups = values.Backups.SessionCount;
        DailyBackups = values.Backups.DailyCount;
        SafetyBackups = values.Backups.SafetyCount;
        CheckAtStartup = values.General.CheckForUpdatesAtStartup;
        _filling = false;
    }

    // Each field writes its own setting; the file is saved in the background.
    private void Save(string property)
    {
        switch (property)
        {
            case nameof(ExtensionsText):
                SaveExtensions();
                break;
            case nameof(DefaultSpeed) when DefaultSpeed is { } speed:
                _ = _settings.ChangeAsync(s => s.Player.DefaultSpeed = speed.Speed);
                break;
            case nameof(DefaultVolume):
                _ = _settings.ChangeAsync(s => s.Player.DefaultVolume = (int)Math.Round(DefaultVolume));
                break;
            case nameof(SeekSeconds):
                _ = _settings.ChangeAsync(s => s.Player.SeekSeconds = (double)SeekSeconds);
                break;
            case nameof(LongSeekSeconds):
                _ = _settings.ChangeAsync(s => s.Player.LongSeekSeconds = (double)LongSeekSeconds);
                break;
            case nameof(VolumeStep):
                _ = _settings.ChangeAsync(s => s.Player.VolumeStep = (int)VolumeStep);
                break;
            case nameof(StartInTheatreMode):
                _ = _settings.ChangeAsync(s => s.Player.TheatreMode = StartInTheatreMode);
                break;
            case nameof(PrepareNeighbours):
                _ = _settings.ChangeAsync(s => s.Player.PrepareNeighbours = PrepareNeighbours);
                break;
            case nameof(StickyTags):
                _ = _settings.ChangeAsync(s => s.Review.StickyTags = StickyTags);
                break;
            case nameof(MarkerOffsetSeconds):
                _ = _settings.ChangeAsync(s => s.Review.MarkerOffsetSeconds = (double)MarkerOffsetSeconds);
                break;
            case nameof(DefaultKept) or nameof(DefaultNew) or nameof(DefaultYeeted) or nameof(DefaultPresent)
                or nameof(DefaultMissing) or nameof(DefaultOffline) or nameof(DefaultDeleted) or nameof(DefaultNeedsAttention):
                SaveDefaultFilters();
                break;
            case nameof(SessionBackups):
                _ = _settings.ChangeAsync(s => s.Backups.SessionCount = (int)SessionBackups);
                break;
            case nameof(DailyBackups):
                _ = _settings.ChangeAsync(s => s.Backups.DailyCount = (int)DailyBackups);
                break;
            case nameof(SafetyBackups):
                _ = _settings.ChangeAsync(s => s.Backups.SafetyCount = (int)SafetyBackups);
                break;
            case nameof(CheckAtStartup):
                _ = _settings.ChangeAsync(s => s.General.CheckForUpdatesAtStartup = CheckAtStartup);
                break;
        }
    }

    // ".mp4, mkv ; .MOV" becomes .mp4, .mkv, .mov. An empty list is refused: nothing would be found.
    private void SaveExtensions()
    {
        var extensions = ExtensionsText.Split(ExtensionSeparators.ToCharArray(), StringSplitOptions.RemoveEmptyEntries)
            .Select(e => ExtensionDot + e.Trim().TrimStart(ExtensionDot).ToLowerInvariant())
            .Where(e => e.Length > 1)
            .Distinct()
            .ToList();
        if (extensions.Count == 0)
        {
            ExtensionsProblem = Strings.ExtensionsEmpty;
            return;
        }
        ExtensionsProblem = string.Empty;
        _ = _settings.ChangeAsync(s => s.Library.VideoExtensions = extensions);
    }

    // The defaults apply to the Explorer now, and replace the filters it remembered (#20).
    private void SaveDefaultFilters()
    {
        var decisions = new List<Decision>();
        if (DefaultKept)
            decisions.Add(Decision.Kept);
        if (DefaultNew)
            decisions.Add(Decision.New);
        if (DefaultYeeted)
            decisions.Add(Decision.Yeeted);
        var states = new List<FileState>();
        if (DefaultPresent)
            states.Add(FileState.Present);
        if (DefaultMissing)
            states.Add(FileState.Missing);
        if (DefaultOffline)
            states.Add(FileState.Offline);
        if (DefaultDeleted)
            states.Add(FileState.Deleted);
        if (DefaultNeedsAttention)
            states.Add(FileState.NeedsAttention);
        _ = _settings.ChangeAsync(s =>
        {
            s.LibraryView.DefaultDecisions = decisions;
            s.LibraryView.DefaultStates = states;
            s.LibraryView.LastUsed = null;
        });
        _explorer.UseDefaultFilters();
    }

    private static string PoolName(BackupPool pool) => pool switch
    {
        BackupPool.Session => Strings.BackupPoolSession,
        BackupPool.Daily => Strings.BackupPoolDaily,
        _ => Strings.BackupPoolSafety,
    };

    private static long SizeOf(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (IOException)
        {
            return 0;
        }
    }

    // Shown in the backup counts' tooltips and bounds.
    public static decimal BackupCountMaximum => MaxBackupCount;
}
