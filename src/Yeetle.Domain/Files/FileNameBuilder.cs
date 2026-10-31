using System.Globalization;

namespace Yeetle.Domain.Files;

// FileName is null when even the date alone does not fit in the path limit.
// Shortened: tags were cut to fit the path limit. Numbered: a suffix was added to avoid a clash.
public sealed record RenamePlan(string? FileName, bool Shortened, bool Numbered);

// Builds the "rename by tags" filename (spec §6, §9):
// CounterStrike2_Dust2_M4_Funny_2026-10-11_22-15-36.mp4
public static class FileNameBuilder
{
    public const int MaxTagLength = 15;

    // Windows MAX_PATH is 260 characters including the terminating null.
    public const int MaxPathLength = 259;

    private const string Separator = "_";
    private const string DateFormat = "yyyy-MM-dd_HH-mm-ss";

    // Removed from tag names: characters Windows forbids, plus spaces, hyphens and underscores so
    // the underscore stays the only separator between tags.
    private const string RemovedCharacters = "\\/:*?\"<>|-_";

    // Text elements, so an accented letter or an emoji is never cut in half.
    public static string ForFileName(string tagName)
    {
        var kept = Elements(tagName).Where(e => !IsRemoved(e));
        return string.Concat(kept.Take(MaxTagLength));
    }

    // tagNames come in Tag Manager order. isTaken tells whether a name is already used in the
    // folder (case-insensitively, as Windows does), not counting the file being renamed. The date
    // always comes after the tags, so the name can never be reserved (CON, NUL) or end in a dot.
    public static RenamePlan Build(
        string folder, IEnumerable<string> tagNames, DateTime recordedAt, string extension, Func<string, bool> isTaken)
    {
        var tags = tagNames.Select(ForFileName).Where(t => t.Length > 0).Select(t => Elements(t).ToList()).ToList();
        var date = recordedAt.ToString(DateFormat, CultureInfo.InvariantCulture);
        for (var number = 1; ; number++)
        {
            var suffix = number == 1 ? string.Empty : Separator + number.ToString(CultureInfo.InvariantCulture);
            var (name, shortened) = Fit(folder, tags, date + suffix + extension);
            if (name is null || !isTaken(name))
                return new RenamePlan(name, shortened, Numbered: name is not null && number > 1);
        }
    }

    // Shortens the tags, last in order first, one element at a time, down to one element each and
    // then dropping them, until the full path fits.
    private static (string? Name, bool Shortened) Fit(string folder, List<List<string>> tags, string ending)
    {
        var cut = tags.Select(t => t.ToList()).ToList();
        var shortened = false;
        while (true)
        {
            var name = string.Join(Separator, cut.Select(t => string.Concat(t)).Append(ending));
            if (Path.Join(folder, name).Length <= MaxPathLength)
                return (name, shortened);
            if (cut.Count == 0)
                return (null, shortened);
            shortened = true;
            var longer = cut.FindLastIndex(t => t.Count > 1);
            if (longer >= 0)
                cut[longer].RemoveAt(cut[longer].Count - 1);
            else
                cut.RemoveAt(cut.Count - 1);
        }
    }

    private static bool IsRemoved(string element) =>
        element.Length == 1 && (RemovedCharacters.Contains(element[0]) || char.IsWhiteSpace(element[0]) || char.IsControl(element[0]));

    private static IEnumerable<string> Elements(string text)
    {
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
            yield return enumerator.GetTextElement();
    }
}
