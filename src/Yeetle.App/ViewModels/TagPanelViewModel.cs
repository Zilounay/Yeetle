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
using Yeetle.Domain.Tagging;

namespace Yeetle.App.ViewModels;

public sealed class ChipViewModel(Chip chip, string? colour, ICommand toggle)
{
    public IBrush? ColourBrush { get; } = TagColours.Brush(colour);
    public bool HasColour => ColourBrush is not null;
    public TagId Tag { get; } = chip.Tag;
    public string Name { get; } = chip.Name;
    public string? Hotkey { get; } = chip.Hotkey is { } key ? HotkeyLabels.Short(key) : null;
    public bool HasHotkey => Hotkey is not null;
    public bool IsSelected { get; } = chip.IsSelected;
    public bool IsCarriedOver { get; } = chip.IsCarriedOver;
    public ICommand Toggle { get; } = toggle;
}

public sealed class ChipGroupViewModel(string title, bool showRequiredWarning, IReadOnlyList<ChipViewModel> chips)
{
    public string Title { get; } = title;
    public bool ShowRequiredWarning { get; } = showRequiredWarning;
    public IReadOnlyList<ChipViewModel> Chips { get; } = chips;
}

public sealed record TagSuggestion(TagId Tag, string Path);

// The Tags panel of the session screen (spec §5, §6, layout from #9): one group per visible box,
// tag hotkeys, and the T type-to-tag box.
public sealed partial class TagPanelViewModel : ViewModelBase
{
    private const string PathSeparator = " › ";
    private const string RuleSeparator = " · ";

    private readonly HashSet<string> _actionKeys;
    private readonly IDialogs _dialogs;
    private readonly List<(Hotkey Key, TagId Tag)> _tagKeys = [];
    private ActiveSession? _session;

    [ObservableProperty]
    private bool _isTyping;

    [ObservableProperty]
    private string _typedText = string.Empty;

    [ObservableProperty]
    private TagSuggestion? _selectedSuggestion;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private bool _hasCarriedOver;

    [ObservableProperty]
    private bool _isEmpty;

    // Set when Keep was refused because a visible required box is empty; cleared by any change.
    private bool _showRequiredWarning;

    public TagPanelViewModel(LoadedSettings settings, IDialogs dialogs)
    {
        _dialogs = dialogs;
        _actionKeys = settings.Values.Hotkeys.Values.SelectMany(k => k).Select(Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public ObservableCollection<ChipGroupViewModel> Groups { get; } = [];

    public ObservableCollection<TagSuggestion> Suggestions { get; } = [];

    public void Attach(ActiveSession? session)
    {
        _session = session;
        _tagKeys.Clear();
        Message = string.Empty;
        IsTyping = false;
        if (session is not null)
        {
            var clashes = new List<string>();
            foreach (var (tag, text) in session.Catalog.Hotkeys)
            {
                if (_actionKeys.Contains(Normalize(text)) || Hotkey.Parse(text) is not { } key)
                    clashes.Add(string.Format(Strings.TagHotkeyClash, text, session.Catalog.Vocabulary.Tag(tag).Name));
                else
                    _tagKeys.Add((key, tag));
            }
            Message = string.Join(Environment.NewLine, clashes);
        }
        Refresh();
    }

    public void ShowRequiredWarning()
    {
        _showRequiredWarning = true;
        Refresh();
    }

    // Called after every change in the session.
    public void Refresh()
    {
        Groups.Clear();
        if (_session?.CurrentReview is not { } review)
            return;
        var catalog = _session.Catalog;
        foreach (var group in TagChips.For(catalog, review.Tags, review.CarriedOver))
        {
            var chips = group.Chips.Select(c =>
                new ChipViewModel(c, catalog.Colours.GetValueOrDefault(c.Tag), new RelayCommand(() => Toggle(c.Tag)))).ToList();
            Groups.Add(new ChipGroupViewModel(Title(group), _showRequiredWarning && group.IsMissingRequired, chips));
        }
        HasCarriedOver = !review.CarriedOver.IsEmpty;
        IsEmpty = catalog.Vocabulary.InManagerOrder.Count == 0;
    }

    public void ClearRequiredWarning() => _showRequiredWarning = false;

    // A tag hotkey toggles that tag on the current clip.
    public ICommand? FindTagCommand(KeyModifiers modifiers, PhysicalKey key, string? symbol)
    {
        if (_session?.CurrentReview is null)
            return null;
        foreach (var (hotkey, tag) in _tagKeys)
        {
            if (hotkey.Matches(modifiers, key, symbol))
                return new RelayCommand(() => Toggle(tag));
        }
        return null;
    }

    [RelayCommand]
    private void OpenTypeTag()
    {
        if (_session?.CurrentReview is null)
            return;
        TypedText = string.Empty;
        IsTyping = true;
    }

    // Enter: applies the highlighted suggestion. Without a match the Tag Manager opens with the text
    // ready as a new tag (spec §6).
    [RelayCommand]
    private void ApplyTyped()
    {
        if (SelectedSuggestion is { } suggestion)
        {
            if (_session?.CurrentReview is { } review && !review.Tags.Contains(suggestion.Tag))
                _session.SelectTag(suggestion.Tag);
            Message = string.Empty;
        }
        else if (TypedText.Trim().Length > 0)
        {
            Message = string.Empty;
            _dialogs.ShowTagManager(TypedText.Trim());
        }
        IsTyping = false;
    }

    [RelayCommand]
    private void CloseTyping() => IsTyping = false;

    public void MoveSuggestion(int step)
    {
        if (Suggestions.Count == 0)
            return;
        var index = SelectedSuggestion is { } current ? Suggestions.IndexOf(current) : -1;
        SelectedSuggestion = Suggestions[Math.Clamp(index + step, 0, Suggestions.Count - 1)];
    }

    partial void OnTypedTextChanged(string value)
    {
        Suggestions.Clear();
        if (_session is not { } session)
            return;
        var vocabulary = session.Catalog.Vocabulary;
        foreach (var tag in TagSearch.Find(vocabulary, value))
            Suggestions.Add(new TagSuggestion(tag, string.Join(PathSeparator, vocabulary.PathOf(tag))));
        SelectedSuggestion = Suggestions.FirstOrDefault();
        Message = Suggestions.Count == 0 && value.Trim().Length > 0 ? string.Format(Strings.NoTagMatch, value.Trim()) : string.Empty;
    }

    private void Toggle(TagId tag)
    {
        if (_session?.CurrentReview is not { } review)
            return;
        if (review.Tags.Contains(tag))
            _session.DeselectTag(tag);
        else
            _session.SelectTag(tag);
    }

    private static string Title(ChipGroup group)
    {
        var rule = group.Box.Rule == BoxRule.PickOne ? Strings.RulePickOne : Strings.RulePickMany;
        var title = string.Join(PathSeparator, group.Path) + RuleSeparator + rule;
        return group.Box.Required ? title + RuleSeparator + Strings.RuleRequired : title;
    }

    // "Ctrl + M" and "Ctrl+M" are the same key.
    private static string Normalize(string key) => string.Concat(key.Where(c => !char.IsWhiteSpace(c)));
}
