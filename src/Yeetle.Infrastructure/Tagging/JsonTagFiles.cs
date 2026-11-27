using System.Text.Json;
using Yeetle.Application.Tagging;
using Yeetle.Domain.Tagging;

namespace Yeetle.Infrastructure.Tagging;

// Tags files in JSON (spec §6), with // notes and trailing commas allowed so people can edit them
// by hand. The format is described at the top of default-tags.json.
public sealed class JsonTagFiles(AppPaths paths) : ITagFiles
{
    private const string DefaultResource = "Yeetle.Infrastructure.Tagging.default-tags.json";

    private const string BoxesKey = "boxes";
    private const string BoxKey = "box";
    private const string PickKey = "pick";
    private const string RequiredKey = "required";
    private const string TagsKey = "tags";
    private const string TagKey = "tag";
    private const string PickOne = "one";
    private const string PickMany = "many";

    // How an entry's place is written in a message: Game › Counter-Strike 2 › Map.
    private const string PlaceSeparator = " › ";

    private static readonly JsonDocumentOptions Options = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public string UserFilePath => paths.Tags;

    public VocabularyTemplate LoadDefault() => Parse(DefaultText());

    public VocabularyTemplate LoadUserFile()
    {
        if (!File.Exists(paths.Tags))
        {
            Directory.CreateDirectory(paths.Root);
            File.WriteAllText(paths.Tags, DefaultText());
        }
        return Parse(File.ReadAllText(paths.Tags));
    }

    public static string DefaultText()
    {
        using var stream = typeof(JsonTagFiles).Assembly.GetManifestResourceStream(DefaultResource)
            ?? throw new InvalidOperationException($"{DefaultResource} is not embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static VocabularyTemplate Parse(string text)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text, Options);
        }
        catch (JsonException e)
        {
            // Line numbers count from 0.
            throw new TagFileException($"Line {e.LineNumber + 1}: {e.Message}", e);
        }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new TagFileException($"The file must be {{ \"{BoxesKey}\": [ ... ] }}.");
            CheckKeys(root, [BoxesKey], string.Empty);
            return new VocabularyTemplate(Boxes(root, BoxesKey, []));
        }
    }

    private static List<TemplateBox> Boxes(JsonElement owner, string key, IReadOnlyList<string> place) =>
        Items(owner, key, place).Select(e => Box(e, place)).ToList();

    private static TemplateBox Box(JsonElement element, IReadOnlyList<string> place)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw Problem(place, $"A box must be {{ \"{BoxKey}\": \"Name\", \"{TagsKey}\": [ ... ] }}.");
        var name = Name(element, BoxKey, place);
        List<string> boxPlace = [.. place, name];
        CheckKeys(element, [BoxKey, PickKey, RequiredKey, TagsKey], Describe(boxPlace));
        var rule = BoxRule.PickMany;
        if (element.TryGetProperty(PickKey, out var pick))
        {
            rule = (pick.ValueKind == JsonValueKind.String ? pick.GetString() : null) switch
            {
                PickOne => BoxRule.PickOne,
                PickMany => BoxRule.PickMany,
                _ => throw Problem(boxPlace, $"\"{PickKey}\" must be \"{PickOne}\" or \"{PickMany}\"."),
            };
        }
        var required = false;
        if (element.TryGetProperty(RequiredKey, out var flag))
        {
            required = flag.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => throw Problem(boxPlace, $"\"{RequiredKey}\" must be true or false."),
            };
        }
        return new TemplateBox(name, rule, required, Items(element, TagsKey, boxPlace).Select(e => Tag(e, boxPlace)).ToList());
    }

    private static TemplateTag Tag(JsonElement element, IReadOnlyList<string> place)
    {
        if (element.ValueKind == JsonValueKind.String)
            return new TemplateTag(CheckName(element.GetString(), place));
        if (element.ValueKind != JsonValueKind.Object)
            throw Problem(place, $"A tag must be a name in quotes, or {{ \"{TagKey}\": \"Name\", \"{TagsKey}\": [ ... ] }}.");
        var name = Name(element, TagKey, place);
        List<string> tagPlace = [.. place, name];
        CheckKeys(element, [TagKey, TagsKey, BoxesKey], Describe(tagPlace));
        var boxes = new List<TemplateBox>();
        if (element.TryGetProperty(TagsKey, out _))
            boxes.Add(TemplateBox.Unnamed(Items(element, TagsKey, tagPlace).Select(e => Tag(e, tagPlace)).ToList()));
        boxes.AddRange(Boxes(element, BoxesKey, tagPlace));
        return new TemplateTag(name, boxes);
    }

    // The array under key, or nothing when the key is absent.
    private static IEnumerable<JsonElement> Items(JsonElement owner, string key, IReadOnlyList<string> place)
    {
        if (!owner.TryGetProperty(key, out var items))
            return [];
        if (items.ValueKind != JsonValueKind.Array)
            throw Problem(place, $"\"{key}\" must be a list: [ ... ].");
        return items.EnumerateArray().ToList();
    }

    private static string Name(JsonElement element, string key, IReadOnlyList<string> place) =>
        element.TryGetProperty(key, out var name) && name.ValueKind == JsonValueKind.String
            ? CheckName(name.GetString(), place)
            : throw Problem(place, $"An entry has no \"{key}\" name.");

    private static string CheckName(string? name, IReadOnlyList<string> place) =>
        string.IsNullOrWhiteSpace(name) ? throw Problem(place, "A name is empty.") : name.Trim();

    // Catches typing mistakes such as "requierd", which would otherwise be ignored without a word.
    private static void CheckKeys(JsonElement element, IReadOnlyList<string> allowed, string place)
    {
        foreach (var property in element.EnumerateObject().Where(p => !allowed.Contains(p.Name)))
        {
            var where = place.Length > 0 ? $"{place}: " : string.Empty;
            throw new TagFileException($"{where}unknown key \"{property.Name}\" (expected {string.Join(", ", allowed.Select(a => $"\"{a}\""))}).");
        }
    }

    private static TagFileException Problem(IReadOnlyList<string> place, string message) =>
        new(place.Count > 0 ? $"{Describe(place)}: {message}" : message);

    private static string Describe(IReadOnlyList<string> place) => string.Join(PlaceSeparator, place);
}
