using System.Collections.Immutable;
using System.Globalization;
using Yeetle.Domain.Clips;
using Yeetle.Domain.Tagging;

namespace Yeetle.Domain.Library;

// A clip as the Explorer lists it. State is the file state as the user sees it: Offline when the
// clip's whole source folder cannot be reached (spec §3). Duration is null until it was read.
public sealed record LibraryClip(
    ClipId Id,
    string FullPath,
    DateTime RecordedAt,
    TimeSpan? Duration,
    FileState State,
    Decision Decision,
    ImmutableHashSet<TagId> Tags,
    ImmutableList<Marker> Markers)
{
    public string FileName => Path.GetFileName(FullPath);
}

public enum LibrarySort
{
    RecordedAt,
    Duration,
    Name,
}

// The Explorer's filters (spec §8). Decisions and states list what is shown; Tags combine as in
// TagFilter; Text looks in file names and marker labels; IncompleteOnly keeps clips with an empty
// required box.
public sealed record LibraryFilter(
    IReadOnlySet<Decision> Decisions,
    IReadOnlySet<FileState> States,
    IReadOnlyCollection<TagId> Tags,
    string Text,
    bool IncompleteOnly)
{
    // Kept clips (spec §8), in every state but Deleted.
    public static LibraryFilter Default { get; } = new(
        new HashSet<Decision> { Decision.Kept },
        new HashSet<FileState> { FileState.Present, FileState.Missing, FileState.Offline, FileState.NeedsAttention },
        [],
        string.Empty,
        IncompleteOnly: false);
}

public static class LibraryQuery
{
    // Case and accents are ignored, in any alphabet: SQLite would only fold A–Z (spec §6).
    private const CompareOptions IgnoreCase = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

    // Newest recording first by default (spec §8). Ties keep a stable order by name.
    public static IReadOnlyList<LibraryClip> Apply(IEnumerable<LibraryClip> clips, LibraryFilter filter,
        TagVocabulary vocabulary, LibrarySort sort = LibrarySort.RecordedAt, bool descending = true)
    {
        var tags = new TagFilter(vocabulary, filter.Tags.Where(t => vocabulary.InManagerOrder.Contains(t)));
        var rules = new TagRules(vocabulary);
        var text = filter.Text.Trim();
        var matching = clips.Where(c =>
            filter.Decisions.Contains(c.Decision)
            && filter.States.Contains(c.State)
            && tags.Matches(c.Tags)
            && (text.Length == 0 || ContainsText(c, text))
            && (!filter.IncompleteOnly || rules.MissingRequired(c.Tags).Any()));
        return Sort(matching, sort, descending).ToList();
    }

    private static bool ContainsText(LibraryClip clip, string text)
    {
        var compare = CultureInfo.InvariantCulture.CompareInfo;
        return compare.IndexOf(clip.FileName, text, IgnoreCase) >= 0
               || clip.Markers.Any(m => compare.IndexOf(m.Label, text, IgnoreCase) >= 0);
    }

    private static IEnumerable<LibraryClip> Sort(IEnumerable<LibraryClip> clips, LibrarySort sort, bool descending)
    {
        var byName = StringComparer.Create(CultureInfo.CurrentCulture, CompareOptions.IgnoreCase);
        IOrderedEnumerable<LibraryClip> ordered = sort switch
        {
            // A clip whose duration is unknown sorts as the shortest.
            LibrarySort.Duration => descending
                ? clips.OrderByDescending(c => c.Duration ?? TimeSpan.Zero)
                : clips.OrderBy(c => c.Duration ?? TimeSpan.Zero),
            LibrarySort.Name => descending ? clips.OrderByDescending(c => c.FileName, byName) : clips.OrderBy(c => c.FileName, byName),
            _ => descending ? clips.OrderByDescending(c => c.RecordedAt) : clips.OrderBy(c => c.RecordedAt),
        };
        return ordered.ThenBy(c => c.FileName, byName).ThenBy(c => c.Id.Value);
    }
}
