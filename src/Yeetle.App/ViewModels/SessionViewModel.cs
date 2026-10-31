using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Yeetle.App.Resources;
using Yeetle.App.Services;
using Yeetle.Application.Files;
using Yeetle.Application.Playback;
using Yeetle.Application.Sessions;
using Yeetle.Application.Settings;
using Yeetle.Application.Tagging;
using Yeetle.Domain.Clips;
using Yeetle.Domain.Sessions;

namespace Yeetle.App.ViewModels;

// The review loop (spec §5): the home state with its Start button, then one clip at a time with
// Keep / Yeet / Skip / Back / Undo, the clip strip and the counter, until the session is saved
// or discarded.
public sealed partial class SessionViewModel : ViewModelBase
{
    // The strip shows this many clips on each side of the centre one.
    private const int StripSide = 2;

    private readonly SessionService _sessions;
    private readonly ClipNavigator _navigator;
    private readonly PlayerViewModel _player;
    private readonly IDialogs _dialogs;
    private readonly TagManager _tagManager;
    private readonly FileOperations _files;
    private ActiveSession? _session;
    private int _stripCentre;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHome))]
    [NotifyCanExecuteChangedFor(nameof(KeepCommand), nameof(YeetCommand), nameof(SkipCommand), nameof(BackCommand),
        nameof(UndoCommand), nameof(StopCommand), nameof(StartCommand))]
    private bool _isActive;

    [ObservableProperty]
    private string _homeText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private int _newCount;

    [ObservableProperty]
    private string _counter = string.Empty;

    // The counter's bar: one segment per decision, each as long as its share of the session (#62).
    [ObservableProperty]
    private int _keptCount;

    [ObservableProperty]
    private int _yeetedCount;

    [ObservableProperty]
    private int _skippedCount;

    [ObservableProperty]
    private int _clipCount;

    [ObservableProperty]
    private string _progressTip = string.Empty;

    [ObservableProperty]
    private string _clipName = string.Empty;

    [ObservableProperty]
    private string _clipDate = string.Empty;

    [ObservableProperty]
    private string _notice = string.Empty;

    public SessionViewModel(SessionService sessions, ClipNavigator navigator, PlayerViewModel player, IDialogs dialogs,
        TagPanelViewModel tags, MarkersPanelViewModel markers, TagManager tagManager, FileOperations files)
    {
        Markers = markers;
        _files = files;
        _tagManager = tagManager;
        _tagManager.Changed += (_, _) => _ = ReloadTagsAsync();
        _sessions = sessions;
        _navigator = navigator;
        _player = player;
        _dialogs = dialogs;
        Tags = tags;
        Actions = new Dictionary<string, ICommand>
        {
            [HotkeyActions.Keep] = KeepCommand,
            [HotkeyActions.Yeet] = YeetCommand,
            [HotkeyActions.NextClip] = SkipCommand,
            [HotkeyActions.PreviousClip] = BackCommand,
            [HotkeyActions.Undo] = UndoCommand,
            [HotkeyActions.StopSession] = StopCommand,
            [HotkeyActions.TypeTag] = tags.OpenTypeTagCommand,
            [HotkeyActions.AddMarker] = markers.AddCommand,
        };
    }

    public TagPanelViewModel Tags { get; }

    public MarkersPanelViewModel Markers { get; }

    public bool IsHome => !IsActive;

    public ObservableCollection<ClipCardViewModel> Strip { get; } = [];

    // What each session hotkey action does, by its name in the settings file.
    public IReadOnlyDictionary<string, ICommand> Actions { get; }

    // Raised when the review screen should look again (decisions, tags, position).
    public event EventHandler? Changed;

    public ActiveSession? Session => _session;

    public async Task RefreshHomeAsync()
    {
        if (IsActive)
            return;
        NewCount = await _sessions.CountNewAsync();
        HomeText = NewCount > 0 ? string.Format(Strings.HomeNewClips, NewCount) : Strings.HomeNoNewClips;
    }

    // After a crash: Resume / Commit / Discard (spec §5). Closing the question asks again next start.
    public async Task OfferUnfinishedAsync()
    {
        if (await _sessions.FindUnfinishedAsync() is not { } unfinished)
            return;
        var choice = await _dialogs.ChooseAsync(Strings.InterruptedTitle,
            string.Format(Strings.InterruptedMessage, unfinished.StartedAt, unfinished.Decided),
            [(Strings.Resume, Ending.Resume), (Strings.Commit, Ending.Save), (Strings.Discard, Ending.Discard)]);
        switch (choice)
        {
            case Ending.Resume:
                Begin(await _sessions.ResumeAsync(unfinished));
                break;
            case Ending.Save:
                if (await SaveAsync(unfinished.Id, unfinished.Snapshot.Clips))
                    await OfferFileOperationsAsync(unfinished.Snapshot.Clips);
                break;
            case Ending.Discard:
                await DiscardAsync(unfinished.Id);
                break;
        }
    }

    // Asked when the window closes mid-session. False keeps the app open.
    public async Task<bool> ConfirmCloseAsync()
    {
        if (_session is not { } session)
            return true;
        var choice = await _dialogs.ChooseAsync(Strings.CloseDuringSessionTitle,
            Summary(Strings.CloseDuringSessionMessage, session),
            [(Strings.SaveAndLeave, Ending.Save), (Strings.DiscardAndLeave, Ending.Discard)]);
        return choice is { } ending && await EndAsync(ending);
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        Notice = string.Empty;
        if (await _sessions.StartAsync() is { } session)
            Begin(session);
        else
            await RefreshHomeAsync();
    }

    private bool CanStart() => !IsActive && NewCount > 0;

    [RelayCommand(CanExecute = nameof(IsActive))]
    private async Task KeepAsync()
    {
        if (_session?.Current is null)
            return;
        // A refused Keep (empty required box) changes nothing; the tag panel shows why, and the
        // next Keep goes through (spec §5).
        if (_session.Keep() == KeepOutcome.Kept)
            await AfterMoveAsync();
        else
            Tags.ShowRequiredWarning();
    }

    [RelayCommand(CanExecute = nameof(IsActive))]
    private async Task YeetAsync()
    {
        if (_session?.Current is null)
            return;
        _session.Yeet();
        await AfterMoveAsync();
    }

    [RelayCommand(CanExecute = nameof(IsActive))]
    private async Task SkipAsync()
    {
        if (_session?.Current is null)
            return;
        _session.Skip();
        await AfterMoveAsync();
    }

    [RelayCommand(CanExecute = nameof(IsActive))]
    private void Back() => _session?.Back();

    [RelayCommand(CanExecute = nameof(IsActive))]
    private void Undo() => _session?.Undo();

    [RelayCommand(CanExecute = nameof(IsActive))]
    private async Task StopAsync()
    {
        if (_session is { } session)
            await AskEndAsync(session);
    }

    // Moves the strip without changing the clip, back to the session's first clip at most.
    [RelayCommand]
    private void StripEarlier() => MoveStrip(-1);

    [RelayCommand]
    private void StripLater() => MoveStrip(1);

    private void Begin(ActiveSession session)
    {
        _session = session;
        session.Changed += OnSessionChanged;
        Tags.Attach(session);
        Markers.Attach(session);
        _navigator.Load(session.Reviews.Select(r => session.Clip(r.Clip).FullPath).ToList(), session.Position);
        IsActive = true;
        OnSessionChanged(session, EventArgs.Empty);
    }

    // Tag Manager changes apply to the running session at once (spec §5).
    private async Task ReloadTagsAsync()
    {
        if (_session is null)
            return;
        var catalog = await _tagManager.LoadAsync();
        if (_session is not { } session)
            return;
        session.UseCatalog(catalog);
        Tags.Attach(session);
    }

    private async Task AfterMoveAsync()
    {
        if (_session is { IsAtEnd: true } session)
            await AskEndAsync(session);
    }

    // Reached by Stop or by passing the last clip: the read-only summary, then Commit or Discard
    // (spec §5). Going back carries on reviewing.
    private async Task AskEndAsync(ActiveSession session)
    {
        var choice = await _dialogs.ShowSessionSummaryAsync(SessionSummary.From(session));
        if (choice is { } ending)
        {
            var reviews = session.Reviews;
            if (await EndAsync(ending == SummaryChoice.Commit ? Ending.Save : Ending.Discard) && ending == SummaryChoice.Commit)
                await OfferFileOperationsAsync(reviews);
        }
        else if (session.IsAtEnd)
        {
            session.Back();
        }
    }

    private async Task<bool> EndAsync(Ending ending)
    {
        if (_session is not { } session)
            return true;
        var saved = ending == Ending.Save
            ? await SaveAsync(session.Id, session.Reviews)
            : await DiscardAsync(session.Id);
        if (!saved)
            return false;
        session.Changed -= OnSessionChanged;
        _session = null;
        Tags.Attach(null);
        Markers.Attach(null);
        IsActive = false;
        Strip.Clear();
        Counter = ClipName = ClipDate = string.Empty;
        // The player lets go of the session's files, which may be deleted or renamed next.
        await _player.ReleaseFilesAsync();
        await RefreshHomeAsync();
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    // After a commit, two separate opt-in prompts (spec §5): delete the yeeted files, then rename
    // the kept ones by their tags.
    private async Task OfferFileOperationsAsync(IReadOnlyList<ClipReview> reviews)
    {
        try
        {
            var yeeted = reviews.Where(r => r.Decision == Decision.Yeeted).Select(r => r.Clip).ToList();
            if (yeeted.Count > 0 && await _files.PlanDeleteAsync(yeeted) is { Items.Count: > 0 } plan
                                 && await _dialogs.ConfirmDeleteAsync(plan, fromSession: true))
            {
                var outcomes = await _files.DeleteAsync(plan);
                if (outcomes.Any(o => !o.Succeeded))
                    await _dialogs.ShowFileResultsAsync(Strings.DeleteResultsTitle, outcomes, canRestoreNames: false);
                Notice = string.Format(Strings.FilesDeleted, outcomes.Count(o => o.Succeeded));
            }
            var kept = reviews.Where(r => r.Decision == Decision.Kept).Select(r => r.Clip).ToList();
            if (kept.Count > 0 && await _files.PlanRenameAsync(kept) is { ToRename.Count: > 0 } preview
                               && await _dialogs.ConfirmRenameAsync(preview, fromSession: true))
            {
                var outcomes = await _files.RenameAsync(preview);
                var renamed = outcomes.Where(o => o.Succeeded).Select(o => o.Clip).ToList();
                Notice = string.Format(Strings.FilesRenamed, renamed.Count);
                // The original names are stored, so the rename can be undone right here (spec §9).
                if (await _dialogs.ShowFileResultsAsync(Strings.RenameResultsTitle, outcomes, canRestoreNames: renamed.Count > 0,
                        preview.FirstRenamedPath(outcomes)))
                {
                    var restored = await _files.RestoreNamesAsync(renamed);
                    await _dialogs.ShowFileResultsAsync(Strings.RestoreResultsTitle, restored, canRestoreNames: false);
                    Notice = string.Format(Strings.NamesRestored, restored.Count(o => o.Succeeded));
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException
                                      or System.Data.Common.DbException)
        {
            Notice = string.Format(Strings.FileOperationsFailed, e.Message);
        }
    }

    private async Task<bool> SaveAsync(long sessionId, IReadOnlyList<ClipReview> reviews)
    {
        try
        {
            await _sessions.CommitAsync(sessionId, reviews);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException
                                      or System.Data.Common.DbException)
        {
            Notice = string.Format(Strings.SessionSaveFailed, e.Message);
            return false;
        }
        Notice = string.Format(Strings.SessionSaved,
            reviews.Count(r => r.Decision == Decision.Kept), reviews.Count(r => r.Decision == Decision.Yeeted));
        return true;
    }

    private async Task<bool> DiscardAsync(long sessionId)
    {
        await _sessions.DiscardAsync(sessionId);
        Notice = Strings.SessionDiscarded;
        return true;
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        if (_session is not { } session)
            return;
        var total = session.Reviews.Count;
        var shown = Math.Min(session.Position, total - 1);
        _navigator.Show(shown);
        Counter = string.Format(Strings.ClipCounterFormat, shown + 1, total);
        ClipCount = total;
        KeptCount = session.Reviews.Count(r => r.Decision == Decision.Kept);
        YeetedCount = session.Reviews.Count(r => r.Decision == Decision.Yeeted);
        // Seen and left undecided; the clip on screen is not skipped yet.
        SkippedCount = session.Reviews.Where((r, i) => i != shown && r.Visited && r.Decision == Decision.New).Count();
        ProgressTip = string.Format(Strings.ProgressTip, KeptCount, YeetedCount, SkippedCount);
        var clip = session.Clip(session.Reviews[shown].Clip);
        ClipName = clip.FileName;
        ClipDate = string.Format(Strings.ClipDateFormat, clip.RecordedAt);
        _stripCentre = shown;
        FillStrip();
        Tags.ClearRequiredWarning();
        Tags.Refresh();
        Markers.Refresh();
        UndoCommand.NotifyCanExecuteChanged();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void MoveStrip(int step)
    {
        if (_session is not { } session)
            return;
        _stripCentre = Math.Clamp(_stripCentre + step, 0, session.Reviews.Count - 1);
        FillStrip();
    }

    private void FillStrip()
    {
        Strip.Clear();
        if (_session is not { } session)
            return;
        var first = Math.Max(0, _stripCentre - StripSide);
        var last = Math.Min(session.Reviews.Count - 1, _stripCentre + StripSide);
        for (var i = first; i <= last; i++)
        {
            var review = session.Reviews[i];
            var clip = session.Clip(review.Clip);
            Strip.Add(new ClipCardViewModel(i + 1, clip.FileName, clip.RecordedAt,
                ClipCardViewModel.BadgeFor(review, isCurrent: i == session.Position)));
        }
    }

    private static string Summary(string format, ActiveSession session) => string.Format(format,
        session.Reviews.Count(r => r.Decision == Decision.Kept),
        session.Reviews.Count(r => r.Decision == Decision.Yeeted),
        session.Reviews.Count(r => r.Visited && r.Decision == Decision.New));

    private enum Ending
    {
        Resume,
        Save,
        Discard,
    }
}
