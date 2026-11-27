using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Yeetle.App.Input;
using Yeetle.App.Resources;
using Yeetle.App.Services;
using Yeetle.Application.Sessions;
using Yeetle.Application.Settings;
using Yeetle.Application.Shell;
using Yeetle.Application.Tagging;
using Yeetle.Domain.Files;
using Yeetle.Domain.Tagging;

namespace Yeetle.App.ViewModels;

// A tag chip in a box card. Clicking a tag with boxes under it selects it and opens them below
// the card; clicking it again closes them (#81).
public sealed partial class TagNodeViewModel(Tag tag, string? colour, string? hotkey, bool hasBoxes, ICommand select)
    : ViewModelBase
{
    [ObservableProperty]
    private bool _isSelected;

    // On the path to the selection: its boxes are open below the card.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExpandGlyph))]
    private bool _isOpen;

    public TagId Id { get; } = tag.Id;
    public string Name { get; } = tag.Name;
    public IBrush? ColourBrush { get; } = TagColours.Brush(colour);
    public bool HasColour => ColourBrush is not null;
    public string? Hotkey { get; } = hotkey is null ? null : HotkeyLabels.Short(hotkey);
    public bool HasHotkey => Hotkey is not null;
    public bool HasBoxes { get; } = hasBoxes;
    public string ExpandGlyph => IsOpen ? "▾" : "▸";
    public ICommand Select { get; } = select;
}

// A box: its name, its Pick one / Pick many switch and Required flag, and its tags.
public sealed partial class BoxCardViewModel : ViewModelBase
{
    private readonly Action<BoxCardViewModel> _rulesChanged;

    [ObservableProperty]
    private bool _isPickOne;

    [ObservableProperty]
    private bool _isRequired;

    [ObservableProperty]
    private bool _isSelected;

    // The boxes under this card's open tag, shown below its chips (#81).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOpenBoxes))]
    private IReadOnlyList<BoxCardViewModel> _openBoxes = [];

    public BoxCardViewModel(Box box, string title, IReadOnlyList<TagNodeViewModel> tags, ICommand select, ICommand addTag,
        Action<BoxCardViewModel> rulesChanged)
    {
        Id = box.Id;
        Title = title;
        IsNamed = box.IsNamed;
        Tags = tags;
        Select = select;
        AddTag = addTag;
        _isPickOne = box.Rule == BoxRule.PickOne;
        _isRequired = box.Required;
        _rulesChanged = rulesChanged;
    }

    public BoxId Id { get; }
    public string Title { get; }
    public bool IsNamed { get; }
    public IReadOnlyList<TagNodeViewModel> Tags { get; }
    public bool HasOpenBoxes => OpenBoxes.Count > 0;

    // The tag whose boxes are open, if any.
    public TagId? OpenTag { get; set; }
    public ICommand Select { get; }
    public ICommand AddTag { get; }

    partial void OnIsPickOneChanged(bool value) => _rulesChanged(this);

    partial void OnIsRequiredChanged(bool value) => _rulesChanged(this);
}

public sealed record ColourChoice(string? Colour, IBrush? Brush, string Tip);

// The Tag Manager (spec §6, layout from #9): the vocabulary as nested box cards on the left, the
// selected tag or box on the right. Every change is saved at once.
public sealed partial class TagManagerViewModel : ViewModelBase
{
    private const string PathSeparator = " › ";

    private readonly TagManager _tags;
    private readonly IDialogs _dialogs;
    private readonly IShell _shell;
    private readonly HotkeyMap _actionKeys;
    private TagCatalog _catalog = TagCatalog.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingName), nameof(PendingNameHint))]
    private string _pendingName = string.Empty;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private bool _isEmpty;

    // Until the first load, placeholders show in place of the cards (#78).
    [ObservableProperty]
    private bool _isLoaded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTagSelected), nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand), nameof(MoveUpCommand), nameof(MoveDownCommand))]
    private TagId? _selectedTag;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBoxSelected), nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand), nameof(MoveUpCommand), nameof(MoveDownCommand))]
    private BoxId? _selectedBox;

    [ObservableProperty]
    private string _editName = string.Empty;

    [ObservableProperty]
    private string _selectedPath = string.Empty;

    [ObservableProperty]
    private string _selectedHotkey = string.Empty;

    [ObservableProperty]
    private bool _isCapturingHotkey;

    [ObservableProperty]
    private string _clipCountText = string.Empty;

    [ObservableProperty]
    private string _fileNameForm = string.Empty;

    [ObservableProperty]
    private bool _canRename;

    [ObservableProperty]
    private string _tagFileMessage = string.Empty;

    public TagManagerViewModel(TagManager tags, IDialogs dialogs, IShell shell, LoadedSettings settings)
    {
        _tags = tags;
        _dialogs = dialogs;
        _shell = shell;
        var actions = settings.Values.Hotkeys;
        _actionKeys = new HotkeyMap(actions, actions.Keys.ToHashSet());
        Colours = TagColours.Palette.Select(c => new ColourChoice(c, TagColours.Brush(c), c))
            .Prepend(new ColourChoice(null, null, Strings.NoColour)).ToList();
    }

    public ObservableCollection<BoxCardViewModel> Boxes { get; } = [];

    public IReadOnlyList<ColourChoice> Colours { get; }

    public bool HasTagSelected => SelectedTag is not null;

    public bool HasBoxSelected => SelectedBox is not null;

    public bool HasSelection => HasTagSelected || HasBoxSelected;

    public bool HasPendingName => PendingName.Length > 0;

    public string PendingNameHint => string.Format(Strings.PendingTagHint, PendingName);

    // Asks the view to put the cursor in the name field, after a box or tag was created.
    public event EventHandler? NameFocusRequested;

    public async Task LoadAsync()
    {
        _catalog = await _tags.LoadAsync();
        var vocabulary = _catalog.Vocabulary;
        Boxes.Clear();
        foreach (var box in vocabulary.BoxesUnder(null))
            Boxes.Add(Card(box));
        IsEmpty = Boxes.Count == 0;
        IsLoaded = true;
        if (SelectedTag is { } tag && !vocabulary.InManagerOrder.Contains(tag))
            SelectedTag = null;
        if (SelectedBox is { } selectedBox && AllCards().All(c => c.Id != selectedBox))
            SelectedBox = null;
        ShowSelection();
    }

    // Called by the view for the key pressed while waiting for a hotkey. Esc cancels.
    public async Task CaptureHotkeyAsync(KeyPress press)
    {
        if (SelectedTag is not { } tag)
            return;
        if (press.Position == PhysicalKey.Escape)
        {
            IsCapturingHotkey = false;
            return;
        }
        if (HotkeyNames.FromKey(press.Modifiers, press.Position) is not { } text)
            return;
        IsCapturingHotkey = false;
        // One key, one action (spec §14): keys of the settings file come first.
        if (_actionKeys.Find(press) is { } action)
        {
            Message = string.Format(Strings.HotkeyTakenByAction, HotkeyLabels.Short(text), action);
            return;
        }
        var other = _catalog.Hotkeys.FirstOrDefault(h =>
            h.Key != tag && Hotkey.Parse(h.Value) is { } used && used.Matches(press));
        if (other.Value is not null)
        {
            Message = string.Format(Strings.HotkeyTakenByTag, HotkeyLabels.Short(text), _catalog.Vocabulary.Tag(other.Key).Name);
            return;
        }
        if (await _tags.SetHotkeyAsync(tag, text) == HotkeyChange.Taken)
        {
            Message = string.Format(Strings.HotkeyTaken, HotkeyLabels.Short(text));
            return;
        }
        Message = string.Empty;
        await LoadAsync();
    }

    // Enter or leaving the name field saves the name. An empty name puts the old one back.
    public async Task CommitNameAsync()
    {
        var name = EditName.Trim();
        var vocabulary = _catalog.Vocabulary;
        if (SelectedTag is { } tag && name.Length > 0 && name != vocabulary.Tag(tag).Name)
            await _tags.RenameTagAsync(tag, name);
        else if (SelectedBox is { } box && CanRename && name.Length > 0 && name != vocabulary.Box(box).Name)
            await _tags.RenameBoxAsync(box, name);
        else
        {
            ShowSelection();
            return;
        }
        await LoadAsync();
    }

    [RelayCommand]
    private async Task AddTopLevelBoxAsync()
    {
        var box = await _tags.AddBoxAsync(null, Strings.NewBoxName, BoxRule.PickMany);
        await SelectAndRenameAsync(box: box);
    }

    // + Tag in a box card. Text typed in the session's tag box and not found becomes its name.
    private async Task AddTagAsync(BoxId box)
    {
        var name = HasPendingName ? PendingName : Strings.NewTagName;
        PendingName = string.Empty;
        var tag = await _tags.AddTagAsync(box, name);
        await SelectAndRenameAsync(tag: tag);
    }

    [RelayCommand]
    private async Task AddBoxUnderSelectedAsync()
    {
        if (SelectedTag is not { } tag)
            return;
        var box = await _tags.AddBoxAsync(tag, Strings.NewBoxName, BoxRule.PickMany);
        await SelectAndRenameAsync(box: box);
    }

    // Tags held by a tag directly sit in its unnamed pick-many box (spec §6).
    [RelayCommand]
    private async Task AddTagUnderSelectedAsync()
    {
        if (SelectedTag is not { } tag)
            return;
        var box = _catalog.Vocabulary.BoxesUnder(tag).FirstOrDefault(b => !b.IsNamed)?.Id
                  ?? await _tags.AddBoxAsync(tag, string.Empty, BoxRule.PickMany);
        await AddTagAsync(box);
    }

    [RelayCommand]
    private void CancelPendingName() => PendingName = string.Empty;

    // Windows opens it with the app it uses for .json files.
    [RelayCommand]
    private void OpenTagFile() => _shell.OpenWithDefaultApp(_tags.TagFilePath);

    // Adds what is new in the user's tags file; nothing is removed or renamed (spec §6).
    [RelayCommand]
    private async Task ImportTagFileAsync()
    {
        var result = await _tags.ImportTagFileAsync();
        TagFileMessage = result.Problem is { } problem ? string.Format(Strings.TagFileProblem, problem)
            : result.AddedTags > 0 ? string.Format(Strings.TagFileAdded, result.AddedTags)
            : Strings.TagFileNothingNew;
        await LoadAsync();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private Task MoveUpAsync() => MoveAsync(-1);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private Task MoveDownAsync() => MoveAsync(1);

    [RelayCommand]
    private async Task SetColourAsync(ColourChoice choice)
    {
        if (SelectedTag is not { } tag)
            return;
        await _tags.SetColourAsync(tag, choice.Colour);
        await LoadAsync();
    }

    [RelayCommand]
    private void StartHotkeyCapture()
    {
        Message = string.Empty;
        IsCapturingHotkey = SelectedTag is not null;
    }

    [RelayCommand]
    private async Task ClearHotkeyAsync()
    {
        if (SelectedTag is not { } tag)
            return;
        IsCapturingHotkey = false;
        await _tags.SetHotkeyAsync(tag, null);
        await LoadAsync();
    }

    // Warns with the clip count; a backup is taken before deleting (spec §5, §10).
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task DeleteSelectedAsync()
    {
        var vocabulary = _catalog.Vocabulary;
        if (SelectedTag is { } tag)
        {
            var count = await _tags.CountClipsBelowTagAsync(tag);
            if (await ConfirmDeleteAsync(vocabulary.Tag(tag).Name, count))
                await _tags.DeleteTagAsync(tag);
        }
        else if (SelectedBox is { } box)
        {
            var count = await _tags.CountClipsInBoxAsync(box);
            if (await ConfirmDeleteAsync(BoxTitle(vocabulary.Box(box)), count))
                await _tags.DeleteBoxAsync(box);
        }
        await LoadAsync();
    }

    private async Task<bool> ConfirmDeleteAsync(string name, int clipCount)
    {
        var answer = await _dialogs.ChooseAsync(Strings.DeleteTagTitle,
            string.Format(Strings.DeleteTagMessage, name, clipCount), [(Strings.Delete, true)]);
        return answer == true;
    }

    private async Task MoveAsync(int step)
    {
        if (SelectedTag is { } tag)
            await _tags.MoveTagAsync(tag, step);
        else if (SelectedBox is { } box)
            await _tags.MoveBoxAsync(box, step);
        await LoadAsync();
    }

    private async Task SelectAndRenameAsync(TagId? tag = null, BoxId? box = null)
    {
        SelectedTag = tag;
        SelectedBox = box;
        await LoadAsync();
        NameFocusRequested?.Invoke(this, EventArgs.Empty);
    }

    // Clicking the selected tag or box again unselects it, which closes the boxes under it (#81).
    private async Task SelectAsync(TagId? tag, BoxId? box)
    {
        if ((tag is not null && tag == SelectedTag) || (box is not null && box == SelectedBox))
            (tag, box) = (null, null);
        // A name being typed is saved before the selection moves on.
        await CommitNameAsync();
        IsCapturingHotkey = false;
        Message = string.Empty;
        SelectedTag = tag;
        SelectedBox = box;
        ShowSelection();
    }

    private void ShowSelection()
    {
        OpenPath(Boxes, PathToSelection());
        foreach (var card in AllCards())
        {
            card.IsSelected = card.Id == SelectedBox;
            foreach (var node in card.Tags)
                node.IsSelected = node.Id == SelectedTag;
        }
        var vocabulary = _catalog.Vocabulary;
        if (SelectedTag is { } tag)
        {
            var name = vocabulary.Tag(tag).Name;
            EditName = name;
            CanRename = true;
            SelectedPath = string.Join(PathSeparator, vocabulary.PathOf(tag));
            SelectedHotkey = _catalog.Hotkeys.TryGetValue(tag, out var key) ? HotkeyLabels.Short(key) : Strings.NoHotkey;
            ClipCountText = string.Format(Strings.TagClipCount, _catalog.Usage.GetValueOrDefault(tag));
            FileNameForm = FileNameBuilder.ForFileName(name);
        }
        else if (SelectedBox is { } box)
        {
            var selected = vocabulary.Box(box);
            EditName = selected.Name;
            CanRename = selected.IsNamed;
            SelectedPath = BoxTitle(selected);
            SelectedHotkey = ClipCountText = FileNameForm = string.Empty;
        }
        else
        {
            EditName = SelectedPath = SelectedHotkey = ClipCountText = FileNameForm = string.Empty;
            CanRename = false;
        }
    }

    // The selected tag and the tags above it, or the tags above the selected box: their boxes are open.
    private HashSet<TagId> PathToSelection()
    {
        var vocabulary = _catalog.Vocabulary;
        var start = SelectedTag ?? (SelectedBox is { } box ? vocabulary.Box(box).ParentTag : null);
        return start is { } tag ? vocabulary.AncestorsOf(tag).Prepend(tag).ToHashSet() : [];
    }

    // Opens the boxes under each tag on the path, one level at a time. Cards already open stay as
    // they are, so a click only creates what was not on screen yet.
    private void OpenPath(IEnumerable<BoxCardViewModel> cards, HashSet<TagId> path)
    {
        foreach (var card in cards)
        {
            foreach (var node in card.Tags)
                node.IsOpen = path.Contains(node.Id);
            var open = card.Tags.FirstOrDefault(t => t.IsOpen && t.HasBoxes)?.Id;
            if (open != card.OpenTag)
            {
                card.OpenTag = open;
                card.OpenBoxes = open is { } tag ? _catalog.Vocabulary.BoxesUnder(tag).Select(Card).ToList() : [];
            }
            OpenPath(card.OpenBoxes, path);
        }
    }

    // Only the box and its chips: the boxes under its tags are made when they open (#81).
    private BoxCardViewModel Card(Box box)
    {
        var vocabulary = _catalog.Vocabulary;
        var tags = vocabulary.TagsIn(box.Id).Select(tag => new TagNodeViewModel(tag,
            _catalog.Colours.GetValueOrDefault(tag.Id), _catalog.Hotkeys.GetValueOrDefault(tag.Id),
            vocabulary.BoxesUnder(tag.Id).Any(), new AsyncRelayCommand(() => SelectAsync(tag.Id, null)))).ToList();
        return new BoxCardViewModel(box, BoxTitle(box), tags, new AsyncRelayCommand(() => SelectAsync(null, box.Id)),
            new AsyncRelayCommand(() => AddTagAsync(box.Id)), card => _ = SaveRulesAsync(card));
    }

    private async Task SaveRulesAsync(BoxCardViewModel card)
    {
        await _tags.SetBoxRuleAsync(card.Id, card.IsPickOne ? BoxRule.PickOne : BoxRule.PickMany, card.IsRequired);
        _catalog = await _tags.LoadAsync();
    }

    private string BoxTitle(Box box) => box.IsNamed
        ? box.Name
        : string.Format(Strings.DirectlyUnderTag, box.ParentTag is { } parent ? _catalog.Vocabulary.Tag(parent).Name : string.Empty);

    private IEnumerable<BoxCardViewModel> AllCards()
    {
        var pending = new Stack<BoxCardViewModel>(Boxes);
        while (pending.TryPop(out var card))
        {
            yield return card;
            foreach (var child in card.OpenBoxes)
                pending.Push(child);
        }
    }
}
