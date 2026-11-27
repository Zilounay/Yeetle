using Dapper;
using Yeetle.Application.Tagging;
using Yeetle.Domain.Tagging;
using Yeetle.Infrastructure.Data;
using Yeetle.Infrastructure.Tagging;
using Yeetle.Infrastructure.Tests.Data;

namespace Yeetle.Infrastructure.Tests.Tagging;

public class TagFilesTests : IDisposable
{
    private readonly TestDataFolder _data = new();
    private readonly SqliteTagStore _tags;
    private readonly TagFileImport _import;

    public TagFilesTests()
    {
        _data.Startup.Prepare();
        _tags = new SqliteTagStore(_data.Connections);
        _import = new TagFileImport(new JsonTagFiles(_data.Paths), new SqliteVocabularyImportStore(_data.Connections), _data.Log);
    }

    public void Dispose() => _data.Dispose();

    [Fact]
    public void The_default_file_reads_with_the_games_and_their_rules()
    {
        var template = new JsonTagFiles(_data.Paths).LoadDefault();

        var game = Assert.Single(template.Boxes, b => b.Name == "Game");
        Assert.Equal((BoxRule.PickOne, true), (game.Rule, game.Required));
        Assert.Equal(["League of Legends", "Dota 2", "Counter-Strike 2", "Valorant", "Escape from Tarkov", "Arc Raiders",
            "Wardogs", "Rob Run", "Battlefield 6"], game.Tags.Select(t => t.Name));
        var csMap = game.Tags.Single(t => t.Name == "Counter-Strike 2").Boxes.Single(b => b.Name == "Map");
        Assert.Equal((BoxRule.PickOne, true), (csMap.Rule, csMap.Required));
        var arcMap = game.Tags.Single(t => t.Name == "Arc Raiders").Boxes.Single(b => b.Name == "Map");
        Assert.False(arcMap.Required);
    }

    [Fact]
    public void First_start_fills_the_library_and_writes_the_users_copy()
    {
        var result = _import.ImportAtStart();

        Assert.Null(result.Problem);
        Assert.True(result.AddedTags > 500);
        Assert.Equal(JsonTagFiles.DefaultText(), File.ReadAllText(_data.Paths.Tags));
        var vocabulary = _tags.LoadCatalog().Vocabulary;
        var ak = vocabulary.InManagerOrder.Single(t => vocabulary.Tag(t).Name == "AK-47");
        Assert.Equal(["Counter-Strike 2", "Weapon", "Rifle", "AK-47"], vocabulary.PathOf(ak));
    }

    [Fact]
    public void Later_starts_add_nothing_and_keep_renames_and_deletions()
    {
        _import.ImportAtStart();
        var vocabulary = _tags.LoadCatalog().Vocabulary;
        _tags.RenameTag(Find(vocabulary, "Counter-Strike 2"), "CS2");
        _tags.DeleteTag(Find(vocabulary, "Wardogs"));
        var before = NodeCount();

        var result = _import.ImportAtStart();

        Assert.Equal(0, result.AddedTags);
        Assert.Equal(before, NodeCount());
        var after = _tags.LoadCatalog().Vocabulary;
        Assert.Contains(after.InManagerOrder, t => after.Tag(t).Name == "CS2");
        Assert.DoesNotContain(after.InManagerOrder, t => after.Tag(t).Name is "Counter-Strike 2" or "Wardogs");
    }

    [Fact]
    public void Tags_added_to_the_users_file_are_imported_under_the_right_parent()
    {
        _import.ImportAtStart();
        File.WriteAllText(_data.Paths.Tags, """
            {
              "boxes": [
                { "box": "Game", "tags": [ { "tag": "Counter-Strike 2", "boxes": [ { "box": "Map", "tags": ["Cache"] } ] } ] },
                { "box": "People", "tags": ["ZiL", "Friend A",] },
              ]
            }
            """);

        var result = _import.ImportUserFile();

        Assert.Equal(3, result.AddedTags);
        var vocabulary = _tags.LoadCatalog().Vocabulary;
        Assert.Equal(["Counter-Strike 2", "Map", "Cache"], vocabulary.PathOf(Find(vocabulary, "Cache")));
    }

    [Fact]
    public void A_broken_file_is_reported_with_its_line_and_changes_nothing()
    {
        _import.ImportAtStart();
        File.WriteAllText(_data.Paths.Tags, "{\n  \"boxes\": [\n    { \"box\": \"Game\" \"tags\": [] }\n  ]\n}");
        var before = NodeCount();

        var result = _import.ImportUserFile();

        Assert.StartsWith("Line 3", result.Problem);
        Assert.Equal(before, NodeCount());
    }

    [Theory]
    [InlineData("""{ "boxes": [ { "box": "Game", "requierd": true } ] }""", "Game: unknown key \"requierd\"")]
    [InlineData("""{ "boxes": [ { "box": "Game", "pick": "two" } ] }""", "Game: \"pick\" must be")]
    [InlineData("""{ "boxes": [ { "box": "Game", "tags": [ { "tags": [] } ] } ] }""", "Game: An entry has no \"tag\" name.")]
    [InlineData("""{ "boxes": [ { "box": "Game", "tags": [ "  " ] } ] }""", "Game: A name is empty.")]
    [InlineData("""{ "boxes": [ { "box": "Game", "tags": [ { "tag": "CS2", "tags": "AWP" } ] } ] }""", "Game › CS2: \"tags\" must be a list")]
    [InlineData("""[]""", "The file must be")]
    public void Mistakes_say_where_they_are(string text, string problem)
    {
        var e = Assert.Throws<TagFileException>(() => JsonTagFiles.Parse(text));

        Assert.StartsWith(problem, e.Message);
    }

    [Fact]
    public void A_tag_can_hold_tags_directly_and_boxes_of_its_own()
    {
        var template = JsonTagFiles.Parse("""
            // A note.
            { "boxes": [ { "box": "Weapon", "tags": [ { "tag": "Rifle", "tags": ["AK-47"], "boxes": [ { "box": "Scope", "pick": "one" } ] } ] } ] }
            """);

        var rifle = template.Boxes[0].Tags[0];
        Assert.Equal([string.Empty, "Scope"], rifle.Boxes.Select(b => b.Name));
        Assert.Equal("AK-47", rifle.Boxes[0].Tags[0].Name);
        Assert.Equal(BoxRule.PickOne, rifle.Boxes[1].Rule);
    }

    private static TagId Find(TagVocabulary vocabulary, string name) =>
        vocabulary.InManagerOrder.First(t => vocabulary.Tag(t).Name == name);

    private int NodeCount()
    {
        using var connection = _data.Connections.Open();
        return connection.ExecuteScalar<int>("SELECT COUNT(*) FROM Nodes;");
    }
}
