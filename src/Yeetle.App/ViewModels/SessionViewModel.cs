using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Yeetle.App.Resources;
using Yeetle.App.Services;
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

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _clipName = string.Empty;

    [ObservableProperty]
    private string _clipDate = string.Empty;

    [ObservableProperty]
    private string _notice = string.Empty;

    public SessionViewModel(SessionService sessions, ClipNavigator navigator, PlayerViewModel player, IDialogs dialogs,
        TagPanelViewModel tags, TagManager tagManager)
    {
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
        };
    }

    public TagPanelViewModel Tags { get; }

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
                await SaveAsync(unfinished.Id, unfinished.Snapshot.Clips);
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

    // Reached by Stop or by passing the last clip. Cancel goes on reviewing.
    private async Task AskEndAsync(ActiveSession session)
    {
        var choice = await _dialogs.ChooseAsync(Strings.EndSessionTitle, Summary(Strings.EndSessionMessage, session),
            [(Strings.SaveSession, Ending.Save), (Strings.DiscardSession, Ending.Discard)]);
        if (choice is { } ending)
            await EndAsync(ending);
        else if (session.IsAtEnd)
            session.Back();
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
        IsActive = false;
        Strip.Clear();
        Counter = ClipName = ClipDate = string.Empty;
        _player.Pause();
        await RefreshHomeAsync();
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
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
        Progress = total == 0 ? 0 : (double)session.Reviews.Count(r => r.Visited) / total;
        var clip = session.Clip(session.Reviews[shown].Clip);
        ClipName = clip.FileName;
        ClipDate = string.Format(Strings.ClipDateFormat, clip.RecordedAt);
        _stripCentre = shown;
        FillStrip();
        Tags.ClearRequiredWarning();
        Tags.Refresh();
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
