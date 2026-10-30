using System.Globalization;

namespace Yeetle.Domain.Tagging;

// Type-to-tag matching (spec §6). Done in C# rather than SQL, since SQLite only folds A–Z: "дота"
// must find "Дота 2" as well as "dota" finds "Dota 2".
public static class TagSearch
{
    private const CompareOptions IgnoreCase = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

    // Tags whose name contains the text: names starting with it first, then Tag Manager order.
    public static IReadOnlyList<TagId> Find(TagVocabulary vocabulary, string text)
    {
        var typed = text.Trim();
        if (typed.Length == 0)
            return [];
        var compare = CultureInfo.InvariantCulture.CompareInfo;
        return vocabulary.InManagerOrder
            .Select(id => (Id: id, At: compare.IndexOf(vocabulary.Tag(id).Name, typed, IgnoreCase)))
            .Where(m => m.At >= 0)
            .OrderBy(m => m.At == 0 ? 0 : 1)
            .Select(m => m.Id)
            .ToList();
    }
}
