using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Yeetle.App.Resources;
using Yeetle.Application.Data;
using Yeetle.Application.Shell;

namespace Yeetle.App.ViewModels;

public sealed record BackupItemViewModel(BackupInfo Backup, string Label);

// Shown instead of the main window when the database cannot be used (spec §10): it explains
// why, and for a damaged file offers the backups to restore. Nothing is ever reset.
public sealed partial class RecoveryViewModel : ViewModelBase
{
    private readonly IDatabaseStartup _database;
    private readonly IBackups _backups;
    private readonly IShell _shell;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private string _problem = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
    private BackupItemViewModel? _selectedBackup;

    public RecoveryViewModel(IDatabaseStartup database, IBackups backups, IShell shell)
    {
        _database = database;
        _backups = backups;
        _shell = shell;
    }

    public IReadOnlyList<BackupItemViewModel> Backups { get; private set; } = [];

    public bool CanRestoreBackups { get; private set; }

    public bool HasNoBackups => CanRestoreBackups && Backups.Count == 0;

    // Raised once the database is ready, so the app can open its main window.
    public event EventHandler? Recovered;

    public void Show(DatabaseCheck check)
    {
        Message = check.State switch
        {
            DatabaseState.Damaged => Strings.DatabaseDamaged,
            DatabaseState.NewerSchema => string.Format(Strings.DatabaseNewer, check.SchemaVersion, _database.LatestSchemaVersion),
            _ => string.Format(Strings.DatabaseUnavailable, check.Problem),
        };
        CanRestoreBackups = check.State == DatabaseState.Damaged;
        Backups = CanRestoreBackups
            ? _backups.List().Select(b => new BackupItemViewModel(b, string.Format(Strings.BackupItemFormat, b.CreatedAt, PoolName(b.Pool)))).ToList()
            : [];
        OnPropertyChanged(nameof(Backups));
        OnPropertyChanged(nameof(CanRestoreBackups));
        OnPropertyChanged(nameof(HasNoBackups));
    }

    [RelayCommand(CanExecute = nameof(CanRestore))]
    private async Task RestoreAsync()
    {
        if (SelectedBackup is not { } selected)
            return;
        try
        {
            await Task.Run(() => _backups.Restore(selected.Backup));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Problem = string.Format(Strings.RestoreFailed, e.Message);
            return;
        }
        var check = await Task.Run(_database.Prepare);
        if (check.State == DatabaseState.Ready)
            Recovered?.Invoke(this, EventArgs.Empty);
        else
            Problem = string.Format(Strings.RestoredButNotReady, check.Problem);
    }

    private bool CanRestore() => SelectedBackup is not null;

    [RelayCommand]
    private void OpenDataFolder() => _shell.OpenFolder(_database.DataFolder);

    private static string PoolName(BackupPool pool) => pool switch
    {
        BackupPool.Session => Strings.BackupPoolSession,
        BackupPool.Daily => Strings.BackupPoolDaily,
        _ => Strings.BackupPoolSafety,
    };
}
